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
        public static Dictionary<uint, string> Read(string steamPath)
        {
            var games = new Dictionary<uint, string>();
            string appinfo = Path.Combine(steamPath, "appcache", "appinfo.vdf");
            if (File.Exists(appinfo))
            {
                try
                {
                    using var stream = new FileStream(appinfo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var memory = new MemoryStream(); stream.CopyTo(memory);
                    foreach (var pair in MetadataReader.ReadAppInfo(memory.ToArray()))
                        if (pair.Key >= 10 && (pair.Value.Type.Length == 0 || pair.Value.Type.Equals("game", StringComparison.OrdinalIgnoreCase)))
                            games[pair.Key] = pair.Value.Name;
                }
                catch { } // Fall back to local artwork/manifests; an unreadable cache is not fatal.
            }
            string cache = Path.Combine(steamPath, "appcache", "librarycache");
            if (Directory.Exists(cache))
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(cache))
                {
                    Match match = Regex.Match(Path.GetFileName(entry), @"^(\d+)(?:_|$)");
                    if (match.Success && uint.TryParse(match.Groups[1].Value, out uint id) && id >= 10 && !games.ContainsKey(id)) games[id] = "App " + id;
                }
            }
            return games;
        }
    }
}
