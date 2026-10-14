// SAM Enhanced modifications, 2026. Distributed under the included zlib license.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Enhanced
{
    public interface IAchievementStore
    {
        bool Read(string id, out bool achieved);
        bool Set(string id, bool achieved);
        int StoreAndWait();
    }

    public static class BatchEngine
    {
        // Only an explicitly approved plan is applied. Protected and unknown entries are excluded.
        public static BatchResult Apply(BatchRequest request, GameInspection current, IAchievementStore store)
        {
            var result = new BatchResult { RequestId = request.RequestId, Inspection = current };
            if (!request.Approved || request.Action == BatchAction.Check)
            { result.Detail = "Read-only request; no achievement changes made."; return result; }
            if (request.GameId != current.GameId || request.ExpectedSteamId != current.SteamId || !current.Live)
            { result.Outcome = SaveOutcome.Failed; result.Detail = "Game/account changed or no live check available. Nothing applied."; return result; }
            if (current.Availability == Availability.Unknown || current.Availability == Availability.NoLicense)
            { result.Outcome = SaveOutcome.Failed; result.Detail = "No reliable editable schema. Nothing applied."; return result; }

            bool target = request.Action == BatchAction.Unlock;
            var plan = new HashSet<string>(request.AchievementIds ?? new List<string>(), StringComparer.Ordinal);
            var permitted = current.Achievements.Where(a => !a.Protected && a.Readable && plan.Contains(a.Id)).ToArray();
            var staged = new List<string>();
            foreach (var achievement in permitted)
            {
                if (!store.Read(achievement.Id, out bool achieved)) { result.Rejected++; continue; }
                if (achieved == target) { result.Unchanged++; continue; }
                if (store.Set(achievement.Id, target)) { result.Staged++; staged.Add(achievement.Id); }
                else result.Rejected++;
            }
            if (staged.Count == 0)
            {
                result.Outcome = result.Rejected == 0 ? SaveOutcome.NoChanges : SaveOutcome.Failed;
                result.Detail = result.Rejected == 0 ? "Already in the requested state, or no editable entries." : "Steam rejected every requested change.";
                return result;
            }
            int stored = store.StoreAndWait();
            if (stored != 1)
            {
                result.Outcome = SaveOutcome.Unconfirmed;
                result.Detail = "Save not confirmed (result " + stored + "). Steam may still retry pending changes. Do not assume nothing changed.";
                return result;
            }
            foreach (string id in staged)
            {
                if (store.Read(id, out bool achieved) && achieved == target) result.Confirmed++;
                else result.Rejected++;
            }
            result.Outcome = result.Confirmed == staged.Count && result.Rejected == 0 ? SaveOutcome.Confirmed : SaveOutcome.PartlyConfirmed;
            result.Detail = result.Confirmed + " achievement changes confirmed by Steam; " + result.Rejected + " rejected/unverified; " + current.ProtectedCount + " protected skipped.";
            return result;
        }
    }
}
