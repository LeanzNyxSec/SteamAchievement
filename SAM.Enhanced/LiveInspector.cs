// SAM Enhanced modifications, 2026. Distributed under the included zlib license.
using System;
using System.Collections.Generic;
using System.Linq;
namespace SAM.Enhanced
{
    public static class LiveInspector
    {
        public static GameInspection Merge(GameInspection schema, IEnumerable<AchievementRule> live)
        {
            var values = live.ToArray();
            if (values.Any(a => string.IsNullOrEmpty(a.Id)) || values.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new InvalidOperationException("Steam returned invalid or duplicate achievement IDs. Retry the check.");
            var rules = schema.Achievements.ToDictionary(a => a.Id, StringComparer.Ordinal);
            bool usableSchema = schema.Availability != Availability.Unknown;
            var result = new GameInspection { GameId = schema.GameId, GameName = schema.GameName, SteamId = schema.SteamId, Live = true };
            foreach (var item in values)
            {
                bool known = usableSchema && rules.TryGetValue(item.Id, out _);
                var rule = known ? rules[item.Id] : null;
                result.Achievements.Add(new AchievementRule { Id = item.Id, Readable = item.Readable, Achieved = item.Achieved,
                    PermissionKnown = known, Permission = rule?.Permission ?? 0, Protected = rule?.Protected ?? false });
            }
            if (values.Length == 0 && schema.Total > 0)
            {
                result.Availability = Availability.Unavailable;
                result.Detail = "Steam's local achievement data is not ready yet; cached schema has " + schema.Total + " entries. Retry check.";
                return result;
            }
            if (values.Length == 0 && schema.Availability == Availability.Unknown)
            {
                result.Availability = Availability.Unknown;
                result.Detail = "Steam returned no achievement list and no schema is available yet. Retry check; this is not proof that the game has no achievements.";
                return result;
            }
            SchemaInspector.Classify(result);
            int missing = result.Achievements.Count(a => !a.PermissionKnown);
            int unreadable = result.Achievements.Count(a => !a.Readable);
            if (missing > 0 && result.EditableCount == 0) result.Availability = Availability.PermissionsUnknown;
            else if (missing > 0) result.Availability = Availability.PartlyProtected;
            result.Detail = "Live check: " + result.Total + " achievements, " + result.EditableCount + " editable candidates, " + result.ProtectedCount +
                " protected, " + missing + " with unknown permissions, " + unreadable + " unreadable. Steam can still reject a save.";
            return result;
        }
    }
}
