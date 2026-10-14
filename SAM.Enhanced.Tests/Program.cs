// SAM Enhanced tests. Uses synthetic data and never writes to a real Steam account.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
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

internal sealed class RawStatsCallback : SAM.API.Callback
{
    public override int Id => 1101;
    public override bool IsServer => false;
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
            if (args.Length == 1 && args[0] == "--callback-diagnostic")
            {
                using var client = new SAM.API.Client(); client.Initialize(440);
                ulong account = client.SteamUser.GetSteamId();
                var callback = client.CreateAndRegisterCallback<RawStatsCallback>();
                bool done = false;
                callback.OnRun += pointer => { Console.WriteLine("CALLBACK game-match=" + ((ulong)Marshal.ReadInt64(pointer) == 440) + " result=" + Marshal.ReadInt32(pointer,8) + " packed-account-match=" + ((ulong)Marshal.ReadInt64(pointer,12) == account) + " aligned-account-match=" + ((ulong)Marshal.ReadInt64(pointer,16) == account)); done=true; };
                Console.WriteLine("REQUEST local=" + client.RequestCurrentStats());
                Console.WriteLine("REQUEST user-accepted=" + (client.SteamUserStats.RequestUserStats(account) != SAM.API.CallHandle.Invalid));
                var timer = Stopwatch.StartNew();
                while (!done && timer.Elapsed.TotalSeconds < 15) { client.RunCallbacks(false); Thread.Sleep(30); }
                Console.WriteLine("CALLBACK received=" + done + " native-achievements=" + client.SteamUserStats.GetNumAchievements()); return 0;
            }
            if (args.Length == 1 && args[0] == "--cache-diagnostic")
            {
                string steam = SAM.API.Steam.GetInstallPath();
                using var stream = new FileStream(Path.Combine(steam, "appcache", "appinfo.vdf"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var copy = new MemoryStream(); stream.CopyTo(copy);
                Console.WriteLine("CACHE bytes=" + copy.Length + " magic=" + BitConverter.ToUInt32(copy.ToArray(), 0).ToString("X8"));
                var data = MetadataReader.ReadAppInfo(copy.ToArray());
                Console.WriteLine("CACHE parsed=" + data.Count + " named=" + data.Count(p => !p.Value.Name.StartsWith("App ")));
                using var client = new SAM.API.Client(); client.Initialize(0);
                var owned = AccountLibrary.ReadOwnedAppIds(steam, client.SteamUser.GetSteamId());
                var library = LibraryIndex.Read(steam, client.SteamUser.GetSteamId());
                Console.WriteLine("ACCOUNT owned-apps=" + owned.Count + " games=" + library.Count + " named=" + library.Count(p => !p.Value.StartsWith("App ")));
                Console.WriteLine("OLD broad-cache matches=" + data.Count(p => p.Value.Type == "game" && client.SteamApps008.IsSubscribedApp(p.Key)));
                return 0;
            }
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
            Test("License caches cannot merge another account or the global free package", () =>
            {
                var encrypted = LicenseFixture(42, (10u,42u,0u), (20u,43u,0u), (0u,42u,0u), (30u,42u,8u));
                Assert(AccountLibrary.ReadLicensePackages(encrypted,42).SetEquals(new uint[] {10}), "account/expired license leak");
                bool rejected = false; try { AccountLibrary.ReadLicensePackages(encrypted,43); } catch { rejected=true; }
                Assert(rejected, "wrong account accepted");
            });
            Test("Per-account discovery reads only the active userdata directory", () =>
            {
                string folder = Path.Combine(Path.GetTempPath(), "sam-accounts-" + Guid.NewGuid().ToString("N"));
                try
                {
                    foreach (uint account in new uint[] {42,43})
                    {
                        string config = Path.Combine(folder,"userdata",account.ToString(),"config"); Directory.CreateDirectory(config);
                        File.WriteAllBytes(Path.Combine(config,"licensecache"), LicenseFixture(account,(account,account,0u)));
                    }
                    Directory.CreateDirectory(Path.Combine(folder,"appcache"));
                    File.WriteAllBytes(Path.Combine(folder,"appcache","packageinfo.vdf"), PackageFixture(40,(42u,100u),(43u,200u)));
                    Assert(AccountLibrary.ReadOwnedAppIds(folder,76561197960265728UL+42).SetEquals(new uint[]{100}), "other userdata merged");
                    foreach (string file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories)) File.Delete(file);
                    foreach (string directory in Directory.GetDirectories(folder,"*",SearchOption.AllDirectories).OrderByDescending(d=>d.Length)) Directory.Delete(directory);
                }
                finally { if (Directory.Exists(folder)) Directory.Delete(folder); }
            });
            Test("Package versions 39/40 map only approved license packages", () =>
            {
                foreach (int version in new[]{39,40})
                    Assert(AccountLibrary.ReadPackageApps(PackageFixture(version,(10u,100u),(20u,200u)),new HashSet<uint>{10}).SetEquals(new uint[]{100}), "wrong package mapping");
            });
            Test("Truncated package caches are rejected", () =>
            {
                var bytes = PackageFixture(40,(10u,100u)); Array.Resize(ref bytes,bytes.Length-4);
                bool rejected=false; try { AccountLibrary.ReadPackageApps(bytes,new HashSet<uint>{10}); } catch { rejected=true; }
                Assert(rejected,"truncated package accepted");
            });
            Test("Game names survive inline and indexed metadata formats", () =>
            {
                foreach (int version in new[]{39,40,41})
                { var games = MetadataReader.ReadAppInfo(AppFixture(version)); Assert(games[100].Name == "Test Game" && games[100].Type == "game", "name or type lost"); }
            });
            Test("One damaged app entry does not discard other game names", () =>
            {
                byte[] bad=AppFixture(39),good=AppFixture(39);
                bad[56]=255; Array.Copy(BitConverter.GetBytes(200u),0,good,8,4);
                using var stream=new MemoryStream();stream.Write(bad,0,bad.Length-4);stream.Write(good,8,good.Length-8);
                var games=MetadataReader.ReadAppInfo(stream.ToArray());
                Assert(games.ContainsKey(200) && games[200].Name=="Test Game", "one damaged entry removed all names");
            });
            Test("Live achievements with missing schema have useful counts but no write permission", () =>
            {
                var live = LiveInspector.Merge(new GameInspection { GameId=100, SteamId=42 },new[]{new AchievementRule{Id="A",Readable=true}});
                Assert(live.Total==1 && live.EditableCount==0 && live.Availability==Availability.PermissionsUnknown,"missing schema falsely editable");
                var store=Store(); BatchEngine.Apply(Request(),live,store); Assert(store.Written.Count==0,"unknown permission wrote data");
            });
            Test("Stale schema matches per ID without discarding known protected entries", () =>
            {
                var live=LiveInspector.Merge(Live(),new[]{new AchievementRule{Id="P",Readable=true},new AchievementRule{Id="NEW",Readable=true},new AchievementRule{Id="A",Readable=true}});
                Assert(live.Total==3 && live.ProtectedCount==1 && live.EditableCount==1,"bad merge");
                var request=Request(); request.AchievementIds.Add("NEW"); var store=Store(); store.Values["NEW"]=false;
                BatchEngine.Apply(request,live,store); Assert(store.Written.SequenceEqual(new[]{"A"}),"unmapped/protected write");
            });
            Test("Not-ready local stats are not confused with games without achievements", () =>
            {
                Assert(LiveInspector.Merge(Live(),Array.Empty<AchievementRule>()).Availability==Availability.Unavailable,"false no-achievements");
                Assert(LiveInspector.Merge(new GameInspection{GameId=100,Availability=Availability.NoAchievements},Array.Empty<AchievementRule>()).Availability==Availability.NoAchievements,"empty game");
            });
            Test("Duplicate native achievement IDs are rejected", () =>
            {
                bool rejected=false; try { LiveInspector.Merge(Live(),new[]{new AchievementRule{Id="A"},new AchievementRule{Id="A"}}); } catch { rejected=true; }
                Assert(rejected,"duplicate live IDs");
            });
            Console.WriteLine("RESULT: " + passed + " tests passed. No real Steam account changes.");
            if (args.Length == 2 && args[0] == "--render") Render(args[1]);
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
    }

    private static void WriteVarint(Stream stream, ulong value)
    { while(value>=128) { stream.WriteByte((byte)((value&127)|128)); value>>=7; } stream.WriteByte((byte)value); }
    private static byte[] LicenseFixture(uint account, params (uint Package,uint Owner,uint Flags)[] licenses)
    {
        using var stream=new MemoryStream(); stream.WriteByte(8);stream.WriteByte(1);
        foreach(var item in licenses)
        {
            using var nested=new MemoryStream(); nested.WriteByte(8);WriteVarint(nested,item.Package);nested.WriteByte(96);WriteVarint(nested,item.Owner);nested.WriteByte(56);WriteVarint(nested,item.Flags);
            stream.WriteByte(18);WriteVarint(stream,(ulong)nested.Length);byte[] data=nested.ToArray();stream.Write(data,0,data.Length);
        }
        stream.Write(new byte[4],0,4);return AccountLibrary.Transform(stream.ToArray(),account);
    }
    private static void Z(BinaryWriter writer,string text) { writer.Write(System.Text.Encoding.UTF8.GetBytes(text)); writer.Write((byte)0); }
    private static byte[] PackageFixture(int version,params (uint Package,uint App)[] packages)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);writer.Write((uint)(0x06565500+version));writer.Write(1u);
        foreach(var item in packages)
        {
            writer.Write(item.Package);writer.Write(new byte[version==40?32:24]);
            writer.Write((byte)0);Z(writer,item.Package.ToString());writer.Write((byte)0);Z(writer,"appids");
            writer.Write((byte)2);Z(writer,"0");writer.Write(item.App);writer.Write((byte)8);writer.Write((byte)8);writer.Write((byte)8);
        }
        writer.Write(uint.MaxValue);return stream.ToArray();
    }
    private static byte[] AppFixture(int version)
    {
        using var entry=new MemoryStream();using var e=new BinaryWriter(entry);
        e.Write(new byte[version>=40?60:40]);
        string[] keys={"appinfo","common","name","type"};
        Action<string> key=text=> { if(version==41)e.Write((uint)Array.IndexOf(keys,text));else Z(e,text); };
        e.Write((byte)0);key("appinfo");e.Write((byte)0);key("common");e.Write((byte)1);key("name");Z(e,"Test Game");e.Write((byte)1);key("type");Z(e,"game");
        e.Write((byte)8);e.Write((byte)8);e.Write((byte)8);
        using var result=new MemoryStream();using var writer=new BinaryWriter(result);writer.Write((uint)(0x07564400+version));writer.Write(1u);
        if(version==41)writer.Write((long)(16+8+entry.Length+4));
        writer.Write(100u);writer.Write((uint)entry.Length);writer.Write(entry.ToArray());writer.Write(0u);
        if(version==41){writer.Write((uint)keys.Length);foreach(string text in keys)Z(writer,text);}
        return result.ToArray();
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
