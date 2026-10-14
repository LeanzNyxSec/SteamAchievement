// SAM Enhanced modifications, 2026. Distributed under the included zlib license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace SAM.Enhanced
{
    public sealed class CachedGame { public uint Id; public string Name = ""; public string Type = ""; }
    public static class MetadataReader
    {
        public static Dictionary<uint, CachedGame> ReadAppInfo(byte[] bytes)
        {
            var games = new Dictionary<uint, CachedGame>();
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                uint magic = reader.ReadUInt32();
                if (magic != 0x07564427 && magic != 0x07564428 && magic != 0x07564429)
                    throw new InvalidDataException("Unsupported appinfo format: " + magic.ToString("X8"));
                reader.ReadUInt32();
                string[] table = null;
                long entriesEnd = stream.Length;
                if (magic == 0x07564429)
                {
                    long offset = reader.ReadInt64();
                    if (offset < 16 || offset > stream.Length - 4) throw new InvalidDataException("Invalid string table.");
                    entriesEnd = offset;
                    stream.Position = offset;
                    uint count = reader.ReadUInt32();
                    if (count > 1000000) throw new InvalidDataException("String table is too large.");
                    table = new string[count];
                    for (int i = 0; i < count; i++) table[i] = ReadZ(reader, stream.Length);
                    stream.Position = 16;
                }
                while (stream.Position <= entriesEnd - 4)
                {
                    uint id = reader.ReadUInt32();
                    if (id == 0) break;
                    uint size = reader.ReadUInt32();
                    long end = stream.Position + size;
                    int header = magic >= 0x07564428 ? 60 : 40;
                    if (size < header || end > entriesEnd) throw new InvalidDataException("Incomplete appinfo entry.");
                    stream.Position += header;
                    var game = new CachedGame { Id = id, Name = "App " + id };
                    try
                    {
                        ReadObject(reader, table, end, "", game, 0);
                        games[id] = game;
                    }
                    catch (InvalidDataException) { } // A bad entry must not discard names from the rest of the cache.
                    finally { stream.Position = end; }
                }
            }
            return games;
        }

        private static string ReadZ(BinaryReader reader, long end)
        {
            var bytes = new List<byte>();
            while (reader.BaseStream.Position < end)
            {
                byte value = reader.ReadByte();
                if (value == 0) return Encoding.UTF8.GetString(bytes.ToArray());
                if (bytes.Count >= 1024 * 1024) throw new InvalidDataException("Cache string is too long.");
                bytes.Add(value);
            }
            throw new InvalidDataException("Incomplete cache string.");
        }

        private static void ReadObject(BinaryReader reader, string[] table, long end, string parent, CachedGame game, int depth)
        {
            if (depth > 64) throw new InvalidDataException("Cache nesting is too deep.");
            while (reader.BaseStream.Position < end)
            {
                byte type = reader.ReadByte();
                if (type == 8 || type == 11) return;
                string key;
                if (table == null) key = ReadZ(reader, end);
                else
                {
                    uint index = reader.ReadUInt32();
                    if (index >= table.Length) throw new InvalidDataException("Invalid cache key.");
                    key = table[index];
                }
                switch (type)
                {
                    case 0: ReadObject(reader, table, end, key, game, depth + 1); break;
                    case 1:
                        string value = ReadZ(reader, end);
                        if (parent.Equals("common", StringComparison.OrdinalIgnoreCase))
                        {
                            if (key.Equals("name", StringComparison.OrdinalIgnoreCase)) game.Name = value;
                            if (key.Equals("type", StringComparison.OrdinalIgnoreCase)) game.Type = value;
                        }
                        break;
                    case 2: case 3: case 4: case 6: reader.BaseStream.Position += 4; break;
                    case 7: case 10: reader.BaseStream.Position += 8; break;
                    case 5:
                        while (reader.BaseStream.Position <= end - 2 && reader.ReadUInt16() != 0) { }
                        break;
                    default: throw new InvalidDataException("Unsupported cache value type: " + type);
                }
                if (reader.BaseStream.Position > end) throw new InvalidDataException("Cache entry is too short.");
            }
        }
    }
}
