// SAM Enhanced modifications, 2026. Distributed under the included zlib license.
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SAM.Enhanced;

namespace SAM.Game
{
    internal static class HeadlessWorker
    {
        public static int Run(string[] args)
        {
            if (args.Length != 2 || (args[0] != "--probe" && args[0] != "--apply")) return 10;
            BatchRequest request = null;
            bool applying = false;
            try
            {
                request = Protocol.Read<BatchRequest>(args[1]);
                if (request.GameId == 0 || string.IsNullOrEmpty(request.RequestId)) return 10;
                string requestDirectory = Path.GetDirectoryName(Path.GetFullPath(args[1]));
                string resultDirectory = Path.GetDirectoryName(Path.GetFullPath(request.ResultPath));
                if (!requestDirectory.Equals(resultDirectory, StringComparison.OrdinalIgnoreCase)) return 10;
                using (var parent = Process.GetProcessById(request.ParentId))
                    if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != request.ParentStartTicks) return 11;
                var guardian = new Thread(() =>
                {
                    try
                    {
                        using var parent = Process.GetProcessById(request.ParentId);
                        while (!parent.HasExited) Thread.Sleep(1000);
                    }
                    catch { }
                    Environment.Exit(11);
                }) { IsBackground = true };
                guardian.Start();

                using var client = new API.Client();
                client.Initialize(request.GameId);
                ulong userId = client.SteamUser.GetSteamId();
                if (userId != request.ExpectedSteamId)
                    throw new InvalidOperationException("The active Steam account changed. Nothing applied.");
                if (!client.SteamApps008.IsSubscribedApp(request.GameId))
                {
                    Protocol.Write(request.ResultPath, new BatchResult { RequestId = request.RequestId,
                        Inspection = new GameInspection { GameId = request.GameId, SteamId = userId, Live = true, Availability = Availability.NoLicense, Detail = "Steam reports no usable game license." },
                        Outcome = SaveOutcome.Failed, Detail = "No usable game license. Nothing applied." });
                    return 0;
                }

                bool received = false;
                int receivedResult = 0;
                var statsCallback = client.CreateAndRegisterCallback<API.Callbacks.UserStatsReceived>();
                // Match the local user as well as the game; the steamclient callback uses the original packed ABI.
                statsCallback.OnRun += data => { if (data.GameId == request.GameId && data.SteamIdUser == userId) { receivedResult = data.Result; received = true; } };
                bool localRequest = client.RequestCurrentStats();
                var userRequest = client.SteamUserStats.RequestUserStats(userId);
                if (!localRequest && userRequest == API.CallHandle.Invalid)
                    throw new InvalidOperationException("Steam did not accept the statistics request. Retry later.");
                var waiting = Stopwatch.StartNew();
                while (!received && waiting.Elapsed.TotalSeconds < 60) { client.RunCallbacks(false); Thread.Sleep(30); }
                if (!received || receivedResult != 1)
                    throw new InvalidOperationException(!received ? "Timed out while reading stats; editability is still unknown." : "Stats request rejected by Steam (result " + receivedResult + ").");

                var refresh = Stopwatch.StartNew();
                GameInspection inspection = null;
                do
                {
                    client.RunCallbacks(false);
                    var schema = SchemaInspector.ReadCached(API.Steam.GetInstallPath(), request.GameId);
                    schema.SteamId = userId;
                    string name = client.SteamApps001.GetAppData(request.GameId, "name");
                    schema.GameName = string.IsNullOrWhiteSpace(name) ? null : name;
                    uint count = client.SteamUserStats.GetNumAchievements();
                    if (count > 65536) throw new InvalidOperationException("Steam returned an invalid achievement count.");
                    var achievements = new List<AchievementRule>();
                    for (uint index = 0; index < count; index++)
                    {
                        string id = client.SteamUserStats.GetAchievementName(index);
                        bool readable = !string.IsNullOrWhiteSpace(id) && client.SteamUserStats.GetAchievement(id, out _);
                        bool achieved = false;
                        if (readable) client.SteamUserStats.GetAchievement(id, out achieved);
                        achievements.Add(new AchievementRule { Id = id, Readable = readable, Achieved = achieved });
                    }
                    inspection = LiveInspector.Merge(schema, achievements);
                    if (inspection.Total > 0 && inspection.Achievements.All(a => a.PermissionKnown && a.Readable)) break;
                    // A callback may precede disk schema/local state readiness. Let both settle.
                    Thread.Sleep(250);
                } while (refresh.Elapsed.TotalSeconds < 8);
                if (!client.SteamUser.IsLoggedIn() || client.SteamUser.GetSteamId() != userId) throw new InvalidOperationException("Steam account changed during the check. Nothing applied.");

                BatchResult result;
                if (args[0] == "--apply")
                {
                    applying = request.Approved && request.Action != BatchAction.Check;
                    result = BatchEngine.Apply(request, inspection, new SteamStore(client, request.GameId));
                }
                else result = new BatchResult { RequestId = request.RequestId, Inspection = inspection, Outcome = SaveOutcome.NotApplied, Detail = inspection.Detail };
                Protocol.Write(request.ResultPath, result);
                return 0;
            }
            catch (Exception e)
            {
                if (request != null && !string.IsNullOrEmpty(request.ResultPath))
                {
                    try { Protocol.Write(request.ResultPath, new BatchResult { RequestId = request.RequestId, Inspection = new GameInspection { GameId = request.GameId, Availability = Availability.Unavailable, Detail = e.Message }, Outcome = applying ? SaveOutcome.Unconfirmed : SaveOutcome.Failed, Detail = e.Message + (applying ? " Save status is unknown; staged changes may still be retried by Steam." : "") }); } catch { }
                }
                return 1;
            }
        }

        private sealed class SteamStore : IAchievementStore
        {
            private readonly API.Client client;
            private readonly uint gameId;
            private bool saved;
            private int result;
            public SteamStore(API.Client client, uint gameId)
            {
                this.client = client; this.gameId = gameId;
                var callback = client.CreateAndRegisterCallback<API.Callbacks.UserStatsStored>();
                callback.OnRun += data => { if (data.GameId == this.gameId) { this.result = data.Result; this.saved = true; } };
            }
            public bool Read(string id, out bool achieved) => this.client.SteamUserStats.GetAchievement(id, out achieved);
            public bool Set(string id, bool achieved) => this.client.SteamUserStats.SetAchievement(id, achieved);
            public int StoreAndWait()
            {
                if (!this.client.SteamUserStats.StoreStats()) return -1;
                var timeout = Stopwatch.StartNew();
                while (!this.saved && timeout.Elapsed.TotalSeconds < 60) { this.client.RunCallbacks(false); Thread.Sleep(30); }
                return this.saved ? this.result : -2;
            }
        }
    }
}
