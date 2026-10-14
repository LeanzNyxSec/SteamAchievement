// SAM Enhanced modifications, 2026. Distributed under the included zlib license.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SAM.Game;
using SAM.API.Types;

namespace SAM.Enhanced
{
    public static class SchemaInspector
    {
        public static GameInspection ReadCached(string steamPath, uint id)
        {
            var unknown = new GameInspection { GameId = id };
            string path = Path.Combine(steamPath, "appcache", "stats", "UserGameStatsSchema_" + id.ToString(CultureInfo.InvariantCulture) + ".bin");
            if (!File.Exists(path)) { unknown.Detail = "No local schema. A live check is needed; this does not mean the game is protected."; return unknown; }
            try
            {
                if (new FileInfo(path).Length > 16 * 1024 * 1024) { unknown.Detail = "Schema is too large to inspect."; return unknown; }
                var root = KeyValue.LoadAsBinary(path);
                if (root == null) { unknown.Detail = "Schema could not be parsed. Retry the live check."; return unknown; }
                return Inspect(root, id);
            }
            catch (Exception e) { unknown.Detail = "Schema could not be read: " + e.GetType().Name; return unknown; }
        }

        public static GameInspection Inspect(KeyValue root, uint id)
        {
            var result = new GameInspection { GameId = id };
            var stats = root[id.ToString(CultureInfo.InvariantCulture)]["stats"];
            if (!stats.Valid || stats.Children == null) { result.Detail = "No usable stats schema. A live check is needed."; return result; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var stat in stats.Children)
            {
                if (!stat.Valid) continue;
                UserStatType type;
                var typeNode = stat["type"];
                if (typeNode.Type != KeyValueType.String || !Enum.TryParse(typeNode.AsString(""), true, out type))
                    type = (UserStatType)stat["type_int"].AsInteger(typeNode.AsInteger(0));
                if (type != UserStatType.Achievements && type != UserStatType.GroupAchievements) continue;
                if (stat.Children == null) continue;
                foreach (var bits in stat.Children.Where(c => c.Name.Equals("bits", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!bits.Valid || bits.Children == null) continue;
                    foreach (var bit in bits.Children)
                    {
                        string name = bit["name"].AsString("");
                        if (!bit.Valid || name.Length == 0) continue;
                        if (!seen.Add(name)) throw new InvalidDataException("Duplicate achievement ID in the schema.");
                        int permission = bit["permission"].AsInteger(0) | stat["permission"].AsInteger(0);
                        bool trusted = IsTrusted(stat["bSetByTrustedGS"]) || IsTrusted(bit["bSetByTrustedGS"]);
                        result.Achievements.Add(new AchievementRule { Id = name, Permission = permission, Protected = (permission & 3) != 0 || trusted });
                    }
                }
            }
            Classify(result);
            result.Detail = "Cached schema: " + result.EditableCount + " client-editable candidates, " + result.ProtectedCount + " protected. Steam must accept an actual save.";
            return result;
        }

        public static void Classify(GameInspection result)
        {
            result.Availability = result.Total == 0 ? Availability.NoAchievements :
                result.ProtectedCount == result.Total ? Availability.Protected :
                result.EditableCount == 0 ? Availability.Unavailable :
                result.ProtectedCount > 0 || result.EditableCount < result.Total ? Availability.PartlyProtected : Availability.ClientEditable;
        }

        private static bool IsTrusted(KeyValue key) => key.AsBoolean(false) || key.AsString("").Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
