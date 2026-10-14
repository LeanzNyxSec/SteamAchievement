// SAM Enhanced modifications, 2026. Distributed under the included zlib license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace SAM.Enhanced
{
    public enum Availability { Unknown, ClientEditable, PartlyProtected, Protected, NoAchievements, Unavailable, NoLicense, PermissionsUnknown }
    public enum BatchAction { Check, Unlock, Lock }
    public enum SaveOutcome { NotApplied, NoChanges, Confirmed, PartlyConfirmed, Unconfirmed, Failed, Cancelled }

    public sealed class AchievementRule
    {
        public string Id;
        public int Permission;
        public bool Protected;
        public bool PermissionKnown = true;
        public bool Readable;
        public bool Achieved;
    }

    public sealed class GameInspection
    {
        public uint GameId;
        public string GameName;
        public ulong SteamId;
        public bool Live;
        public Availability Availability;
        public string Detail = "Not checked yet.";
        public List<AchievementRule> Achievements = new();
        [XmlIgnore] public int Total => this.Achievements.Count;
        [XmlIgnore] public int ProtectedCount => this.Achievements.Count(a => a.Protected);
        [XmlIgnore] public int EditableCount => this.Achievements.Count(a => a.PermissionKnown && !a.Protected && (!this.Live || a.Readable));
        public int ChangesFor(BatchAction action) => this.Achievements.Count(a => a.PermissionKnown && !a.Protected && a.Readable && a.Achieved != (action == BatchAction.Unlock));
        public string StatusText => this.Availability switch
        {
            Availability.ClientEditable => this.Live ? "Client-editable" : "Client-editable (cache)",
            Availability.PartlyProtected => this.Live ? "Partly protected" : "Partly protected (cache)",
            Availability.Protected => "Protected / not editable",
            Availability.NoAchievements => "No achievements",
            Availability.NoLicense => "No usable game license",
            Availability.PermissionsUnknown => "Achievements loaded / permissions unknown",
            Availability.Unavailable => "Unavailable / retry check",
            _ => "Unknown / check needed",
        };
    }

    public sealed class BatchRequest
    {
        public string RequestId;
        public uint GameId;
        public ulong ExpectedSteamId;
        public BatchAction Action;
        public bool Approved;
        public string ResultPath;
        public int ParentId;
        public long ParentStartTicks;
        public List<string> AchievementIds = new();
    }

    public sealed class BatchResult
    {
        public string RequestId;
        public GameInspection Inspection = new();
        public SaveOutcome Outcome;
        public int Staged;
        public int Confirmed;
        public int Rejected;
        public int Unchanged;
        public string Detail;
    }

    public static class Protocol
    {
        public static T Read<T>(string path)
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
            return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
        }
        public static void Write<T>(string path, T value)
        {
            string temp = path + ".tmp";
            using (var writer = XmlWriter.Create(temp, new XmlWriterSettings { Indent = true }))
                new XmlSerializer(typeof(T)).Serialize(writer, value);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
    }
}
