// SAM Enhanced modifications, 2026. Distributed under the included zlib license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SAM.Enhanced
{
    public static class LibraryIndex
    {
        public static Dictionary<uint, string> Read(string steamPath, ulong steamId)
        {
            // Global metadata supplies names only. The current account's licenses supply IDs.
            var owned = AccountLibrary.ReadOwnedAppIds(steamPath, steamId);
            Dictionary<uint, CachedGame> metadata;
            try { metadata = MetadataReader.ReadAppInfo(AccountLibrary.ReadShared(Path.Combine(steamPath, "appcache", "appinfo.vdf"))); }
            catch (IOException e) { throw new IOException("Game metadata could not be read. Open Steam’s Library, let it load, and Refresh library.", e); }
            var games = new Dictionary<uint, string>();
            foreach (uint id in owned)
            {
                if (metadata.TryGetValue(id, out var game))
                {
                    if (!game.Type.Equals("game", StringComparison.OrdinalIgnoreCase)) continue;
                    games[id] = game.Name;
                }
                else games[id] = "App " + id;
            }
            return games;
        }
    }
}
