// SAM Enhanced tests. Uses synthetic data and never writes to a real Steam account.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SAM.Enhanced;
using SAM.Game;

internal sealed class FakeStore : IAchievementStore
{
    public Dictionary<string, bool> Values = new();
    public HashSet<string> Reject = new();
    public List<string> Written = new();
    public int SaveCode = 1, Saves;
    public bool Read(string id, out bool value) => this.Values.TryGetValue(id, out value);
    public bool Set(string id, bool value) { if (this.Reject.Contains(id)) return false; this.Written.Add(id); this.Values[id] = value; return true; }
    public int StoreAndWait() { this.Saves++; return this.SaveCode; }
}

internal static class Program
{
    private static int passed;
    private static void Assert(bool value, string detail) { if (!value) throw new Exception(detail); }
    private static void Test(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    private static KeyValue Object(string name, params KeyValue[] children) => new() { Name = name, Type = KeyValueType.None, Valid = true, Children = children.ToList() };
    private static KeyValue Text(string name, string value) => new() { Name = name, Type = KeyValueType.String, Valid = true, Value = value };
    private static KeyValue Number(string name, int value) => new() { Name = name, Type = KeyValueType.Int32, Valid = true, Value = value };
    private static KeyValue Bit(string id, int permission = 0) => Object(id, Text("name", id), Number("permission", permission));
    private static KeyValue Schema(params KeyValue[] stats) => Object("root", Object("100", Object("stats", stats)));
    private static KeyValue Group(params KeyValue[] bits) => Object("ACH", Text("type", "Achievements"), Object("bits", bits));
    private static GameInspection Live() => new() { GameId = 100, SteamId = 42, Live = true, Availability = Availability.PartlyProtected,
        Achievements = new() { new AchievementRule { Id = "A", Readable = true }, new AchievementRule { Id = "B", Readable = true }, new AchievementRule { Id = "P", Readable = true, Protected = true } } };
    private static BatchRequest Request() => new() { RequestId = "test", GameId = 100, ExpectedSteamId = 42, Approved = true, Action = BatchAction.Unlock, AchievementIds = new() { "A", "B", "P" } };
    private static FakeStore Store() => new() { Values = new() { ["A"] = false, ["B"] = false, ["P"] = false } };

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--live-probe") return LiveProbe(args[1]);
            Test("Editable cached schema is classified without a write", () =>
            {
                var info = SchemaInspector.Inspect(Schema(Group(Bit("A"), Bit("B"))), 100);
                Assert(info.Availability == Availability.ClientEditable && info.EditableCount == 2 && !info.Live, "classification");
            });
            Test("Protected and partly protected schemas are visible", () =>
            {
                var protectedInfo = SchemaInspector.Inspect(Schema(Group(Bit("P", 3))), 100);
                var mixed = SchemaInspector.Inspect(Schema(Group(Bit("A"), Bit("P", 1))), 100);
                Assert(protectedInfo.Availability == Availability.Protected && mixed.Availability == Availability.PartlyProtected && mixed.ProtectedCount == 1, "protected classification");
            });
            Test("Parent permissions and trusted-server flags are inherited", () =>
            {
                var group = Group(Bit("A")); group.Children.Add(Number("permission", 2));
                Assert(SchemaInspector.Inspect(Schema(group), 100).ProtectedCount == 1, "parent permission ignored");
                group = Group(Bit("A")); group.Children.Add(Text("bSetByTrustedGS", "true"));
                Assert(SchemaInspector.Inspect(Schema(group), 100).ProtectedCount == 1, "server flag ignored");
            });
            Test("Legacy integer schema types remain supported", () =>
            {
                var group = Object("ACH", Number("type", 4), Object("bits", Bit("A")));
                Assert(SchemaInspector.Inspect(Schema(group), 100).EditableCount == 1, "legacy format");
            });
            Test("Stats-only games have no achievements; missing schema stays unknown", () =>
            {
                Assert(SchemaInspector.Inspect(Schema(Object("score", Text("type", "Integer"))), 100).Availability == Availability.NoAchievements, "stats-only");
                Assert(SchemaInspector.Inspect(Object("root"), 100).Availability == Availability.Unknown, "missing schema falsely protected");
            });
            Test("Duplicate achievement IDs are rejected", () =>
            {
                bool rejected = false;
                try { SchemaInspector.Inspect(Schema(Group(Bit("A"), Bit("A"))), 100); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "duplicates accepted");
            });
            Test("A read-only or unapproved request makes no changes", () =>
            {
                var request = Request(); request.Approved = false; var store = Store();
                BatchEngine.Apply(request, Live(), store); Assert(store.Written.Count == 0 && store.Saves == 0, "unapproved write");
                request.Approved = true; request.Action = BatchAction.Check;
                BatchEngine.Apply(request, Live(), store); Assert(store.Written.Count == 0, "check wrote data");
            });
            Test("Different game or account blocks all writes", () =>
            {
                var request = Request(); var store = Store(); request.GameId = 101;
                BatchEngine.Apply(request, Live(), store); request.GameId = 100; request.ExpectedSteamId = 43;
                BatchEngine.Apply(request, Live(), store); Assert(store.Written.Count == 0 && store.Saves == 0, "cross-account write");
            });
            Test("Unknown and non-live results cannot be applied", () =>
            {
                var info = Live(); info.Live = false; var store = Store();
                BatchEngine.Apply(Request(), info, store); info.Live = true; info.Availability = Availability.Unknown;
                BatchEngine.Apply(Request(), info, store); Assert(store.Written.Count == 0, "unknown write");
            });
            Test("Only previewed editable achievement IDs are changed", () =>
            {
                var request = Request(); request.AchievementIds = new() { "A", "P", "NOT_IN_SCHEMA", "A" }; var store = Store();
                var result = BatchEngine.Apply(request, Live(), store);
                Assert(store.Written.SequenceEqual(new[] { "A" }) && result.Confirmed == 1 && store.Saves == 1 && !store.Values["P"], "scope/protection");
            });
            Test("Bulk unlock confirms the Steam save result", () =>
            {
                var store = Store(); var result = BatchEngine.Apply(Request(), Live(), store);
                Assert(result.Outcome == SaveOutcome.Confirmed && result.Confirmed == 2 && store.Values["A"] && store.Values["B"] && !store.Values["P"], "unlock");
            });
            Test("Bulk lock does not reset protected achievements or game statistics", () =>
            {
                var request = Request(); request.Action = BatchAction.Lock;
                var store = Store(); store.Values["A"] = store.Values["B"] = store.Values["P"] = true;
                var result = BatchEngine.Apply(request, Live(), store);
                Assert(result.Confirmed == 2 && !store.Values["A"] && store.Values["P"], "lock scope");
            });
            Test("Already matching states do not trigger a needless save", () =>
            {
                var store = Store(); store.Values["A"] = store.Values["B"] = true;
                var result = BatchEngine.Apply(Request(), Live(), store);
                Assert(result.Outcome == SaveOutcome.NoChanges && store.Written.Count == 0 && store.Saves == 0, "unnecessary save");
            });
            Test("A rejected achievement does not block other approved entries", () =>
            {
                var store = Store(); store.Reject.Add("A"); var result = BatchEngine.Apply(Request(), Live(), store);
                Assert(result.Outcome == SaveOutcome.PartlyConfirmed && result.Confirmed == 1 && result.Rejected == 1, "partial failure");
            });
            Test("Missing save confirmation is never called success", () =>
            {
                foreach (int code in new[] { -1, -2, 8 })
                { var store = Store(); store.SaveCode = code; var result = BatchEngine.Apply(Request(), Live(), store); Assert(result.Outcome == SaveOutcome.Unconfirmed && result.Confirmed == 0 && result.Staged == 2, "false success"); }
            });
            Test("A protected-only game produces no native writes", () =>
            {
                var info = Live(); foreach (var a in info.Achievements) a.Protected = true; info.Availability = Availability.Protected;
                var store = Store(); BatchEngine.Apply(Request(), info, store); Assert(store.Written.Count == 0 && store.Saves == 0, "protected-only write");
            });
            Test("Batch IPC round-trips plans and rejects external XML entities", () =>
            {
                string file = Path.GetTempFileName();
                try
                {
                    Protocol.Write(file, Request()); var request = Protocol.Read<BatchRequest>(file);
                    Assert(request.Approved && request.GameId == 100 && request.AchievementIds.Count == 3, "protocol");
                    File.WriteAllText(file, "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///nonexistent'>]><BatchRequest>&e;</BatchRequest>");
                    bool rejected = false; try { Protocol.Read<BatchRequest>(file); } catch { rejected = true; }
                    Assert(rejected, "external entity accepted");
                }
                finally { File.Delete(file); }
            });
            Test("Results round-trip with live inspection and save counts", () =>
            {
                string file = Path.GetTempFileName();
                try { Protocol.Write(file, new BatchResult { RequestId = "result", Inspection = Live(), Outcome = SaveOutcome.PartlyConfirmed, Confirmed = 1 });
                    var result = Protocol.Read<BatchResult>(file); Assert(result.Inspection.Live && result.Inspection.ProtectedCount == 1 && result.Confirmed == 1, "result protocol"); }
                finally { File.Delete(file); }
            });
            Test("A missing disk cache remains unknown", () =>
            {
                var info = SchemaInspector.ReadCached(Path.Combine(Path.GetTempPath(), "sam-missing-" + Guid.NewGuid().ToString("N")), 100);
                Assert(info.Availability == Availability.Unknown, "missing cache classification");
            });
            Console.WriteLine("RESULT: " + passed + " tests passed. No real Steam account changes.");
            if (args.Length == 2 && args[0] == "--render") Render(args[1]);
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
    }

    private static int LiveProbe(string worker)
    {
        using var client = new SAM.API.Client(); client.Initialize(0);
        uint game = new uint[] { 440, 480, 570, 730 }.FirstOrDefault(id => client.SteamApps008.IsSubscribedApp(id));
        if (game == 0) { Console.WriteLine("LIVE: no sample license; no writes performed."); return 2; }
        string folder = Path.Combine(Path.GetTempPath(), "sam-readonly-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string requestFile = Path.Combine(folder, "request.xml"), resultFile = Path.Combine(folder, "result.xml");
        using var parent = Process.GetCurrentProcess();
        var request = new BatchRequest { RequestId = "readonly", GameId = game, ExpectedSteamId = client.SteamUser.GetSteamId(), Action = BatchAction.Check, Approved = false,
            ParentId = parent.Id, ParentStartTicks = parent.StartTime.ToUniversalTime().Ticks, ResultPath = resultFile };
        try
        {
            Protocol.Write(requestFile, request);
            using var process = Process.Start(new ProcessStartInfo(Path.GetFullPath(worker), "--probe \"" + requestFile + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(worker)) });
            if (!process.WaitForExit(90000)) { process.Kill(); Console.WriteLine("LIVE: probe timeout. No writes performed."); return 2; }
            if (!File.Exists(resultFile)) { Console.WriteLine("LIVE: worker exited " + process.ExitCode + " without result. No writes performed."); return 2; }
            var result = Protocol.Read<BatchResult>(resultFile);
            Console.WriteLine("LIVE: " + result.Inspection.StatusText + "; " + result.Detail + " No writes performed.");
            return result.Inspection.Live ? 0 : 2;
        }
        finally { if (File.Exists(requestFile)) File.Delete(requestFile); if (File.Exists(resultFile)) File.Delete(resultFile); Directory.Delete(folder); }
    }

    private static void Render(string folder)
    {
        Directory.CreateDirectory(folder); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using (var form = new SAM.Picker.EnhancedPicker(null, true))
        {
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-32000, -32000); form.Show(); Application.DoEvents(); form.PerformLayout();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(folder, "sam-enhanced-preview.png"));
        }
        var info = Live(); var sample = new List<BatchResult> { new() { Inspection = info, Detail = "Synthetic example: two editable changes, one protected entry." } };
        using (var form = new SAM.Picker.BatchPreviewDialog(BatchAction.Unlock, new() { [100] = "Example game (synthetic data)" }, sample))
        {
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-32000, -32000); form.Show(); Application.DoEvents(); form.PerformLayout();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(folder, "sam-batch-preview.png"));
        }
        Console.WriteLine("Rendered read-only UI previews using synthetic data.");
    }
}
