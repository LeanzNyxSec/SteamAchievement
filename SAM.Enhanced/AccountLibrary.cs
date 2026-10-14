// SAM Enhanced modifications, 2026. Distributed under the included zlib license.
// Format references (independent implementation):
// github.com/wynick27/steam-missing-covers-downloader/blob/master/license_parser.py
// github.com/SteamDatabase/Protobufs/blob/master/steam/steammessages_clientserver.proto
// github.com/xPaw/SteamAppInfo — account license packages mapped to app IDs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAM.Game;

namespace SAM.Enhanced
{
    public static class AccountLibrary
    {
        public static byte[] ReadShared(string path)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > 256 * 1024 * 1024) throw new InvalidDataException("Steam cache exceeds the size limit.");
            using var copy = new MemoryStream(); file.CopyTo(copy); return copy.ToArray();
        }

        // Valve's shuffled Park-Miller stream obfuscates the account-local cache.
        // No credentials, ownership tokens or private fields are returned or persisted.
        public static byte[] Transform(byte[] bytes, uint accountId)
        {
            long state = Math.Abs((long)unchecked((int)accountId));
            if (state == 0) state = 1;
            var shuffle = new long[32];
            for (int warm = 39; warm >= 0; warm--)
            {
                state = state * 16807 % 2147483647;
                if (warm < 32) shuffle[warm] = state;
            }
            long previous = shuffle[0];
            var result = new byte[bytes.Length];
            for (int offset = 0; offset < bytes.Length; offset++)
            {
                long random;
                do
                {
                    state = state * 16807 % 2147483647;
                    int index = (int)(previous / 67108864);
                    random = shuffle[index]; shuffle[index] = state; previous = random;
                } while (random > 2147483647L - (2147483648L % 95));
                result[offset] = (byte)(bytes[offset] ^ (32 + random % 95));
            }
            return result;
        }

        public static HashSet<uint> ReadLicensePackages(byte[] encrypted, uint accountId)
        {
            if (accountId == 0 || encrypted.Length < 6 || encrypted.Length > 16 * 1024 * 1024) throw new InvalidDataException("Invalid account license cache.");
            byte[] bytes = Transform(encrypted, accountId);
            int position = 0, end = bytes.Length - 4, result = 2;
            var packages = new HashSet<uint>();
            while (position < end)
            {
                ulong tag = Varint(bytes, ref position, end);
                if (tag == 8) result = checked((int)Varint(bytes, ref position, end));
                else if (tag == 18)
                {
                    int length = checked((int)Varint(bytes, ref position, end));
                    if (length < 0 || length > end - position) throw new InvalidDataException("Truncated license.");
                    int limit = position + length; uint package = uint.MaxValue, owner = 0, flags = 0;
                    while (position < limit)
                    {
                        ulong item = Varint(bytes, ref position, limit);
                        if (item == 8) package = checked((uint)Varint(bytes, ref position, limit));
                        else if (item == 56) flags = checked((uint)Varint(bytes, ref position, limit));
                        else if (item == 96) owner = checked((uint)Varint(bytes, ref position, limit));
                        else Skip(bytes, ref position, limit, item);
                    }
                    // Package 0 is the global free-to-play base, not this account's library.
                    if (package != 0 && package != uint.MaxValue && (owner == 0 || owner == accountId) && (flags & 0xC3E) == 0) packages.Add(package);
                }
                else Skip(bytes, ref position, end, tag);
            }
            if (result != 1) throw new InvalidDataException("Steam license cache is not a successful account snapshot. Open Steam's Library and refresh.");
            return packages;
        }

        private static ulong Varint(byte[] data, ref int position, int end)
        {
            ulong value = 0;
            for (int shift = 0; shift < 70; shift += 7)
            {
                if (position >= end) throw new InvalidDataException("Truncated protobuf field.");
                byte next = data[position++];
                if (shift == 63 && next > 1) throw new InvalidDataException("Oversized protobuf field.");
                value |= (ulong)(next & 127) << shift;
                if (next < 128) return value;
            }
            throw new InvalidDataException("Oversized protobuf field.");
        }
        private static void Skip(byte[] data, ref int position, int end, ulong tag)
        {
            if (tag == 0) throw new InvalidDataException("Invalid protobuf tag.");
            int size;
            switch (tag & 7)
            {
                case 0: Varint(data, ref position, end); return;
                case 1: size = 8; break;
                case 2: size = checked((int)Varint(data, ref position, end)); break;
                case 5: size = 4; break;
                default: throw new InvalidDataException("Unsupported protobuf field.");
            }
            if (size > end - position) throw new InvalidDataException("Truncated protobuf field.");
            position += size;
        }

        public static HashSet<uint> ReadPackageApps(byte[] bytes, HashSet<uint> selectedPackages)
        {
            var ids = new HashSet<uint>();
            using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream);
            uint magic = reader.ReadUInt32();
            if (magic != 0x06565527 && magic != 0x06565528) throw new InvalidDataException("Unsupported packageinfo format.");
            reader.ReadUInt32();
            while (stream.Position <= stream.Length - 4)
            {
                uint package = reader.ReadUInt32();
                if (package == uint.MaxValue) return ids;
                int header = magic == 0x06565528 ? 32 : 24;
                if (header > stream.Length - stream.Position) throw new InvalidDataException("Truncated package header.");
                stream.Position += header;
                var root = new KeyValue();
                if (!root.ReadAsBinary(stream)) throw new InvalidDataException("Unreadable package entry.");
                if (!selectedPackages.Contains(package)) continue;
                var node = root[package.ToString(System.Globalization.CultureInfo.InvariantCulture)]["appids"];
                if (!node.Valid) node = root["appids"];
                if (node.Children == null) continue;
                foreach (var app in node.Children)
                    if (uint.TryParse(app.AsString(""), out uint id) && id >= 10) ids.Add(id);
            }
            throw new InvalidDataException("Package cache has no end marker.");
        }
        public static HashSet<uint> ReadOwnedAppIds(string steamPath, ulong steamId)
        {
            uint account = unchecked((uint)steamId);
            if ((steamId >> 32) == 0 || account == 0) throw new InvalidDataException("No active Steam account.");
            string licenses = Path.Combine(steamPath, "userdata", account.ToString(System.Globalization.CultureInfo.InvariantCulture), "config", "licensecache");
            if (!File.Exists(licenses)) throw new FileNotFoundException("No license cache for the active account. Open Steam's Library, then Refresh library.");
            var packages = ReadLicensePackages(ReadShared(licenses), account);
            return ReadPackageApps(ReadShared(Path.Combine(steamPath, "appcache", "packageinfo.vdf")), packages);
        }
    }
}
