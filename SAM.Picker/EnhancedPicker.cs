// SAM Enhanced modifications, 2026. Distributed under the included zlib license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SAM.Enhanced;

namespace SAM.Picker
{
    public sealed class EnhancedPicker : Form
    {
        private sealed class Entry { public uint Id; public string Name; public GameInspection Inspection; public string Last = "Not changed"; }
        private readonly API.Client steam;
        private readonly bool demo;
        private readonly ulong account;
        private readonly Dictionary<uint, Entry> entries = new();
        private readonly HashSet<uint> checkedIds = new();
        private readonly Dictionary<uint, Process> editors = new();
        private readonly ListView games = new();
        private readonly TextBox search = new();
        private readonly TextBox manualIds = new();
        private readonly ComboBox filter = new();
        private readonly Label summary = new();
        private readonly ToolStripStatusLabel status = new();
        private readonly ToolStripProgressBar progress = new();
        private readonly List<Button> actionButtons = new();
        private readonly Button cancel;
        private readonly System.Windows.Forms.Timer callbacks = new() { Interval = 500 };
        private readonly BatchRunner runner;
        private CancellationTokenSource cancellation;
        private bool busy, loading, rebuilding, closeWhenDone;

        public EnhancedPicker(API.Client client, bool preview = false)
        {
            this.steam = client; this.demo = preview;
            this.account = client == null ? 0 : client.SteamUser.GetSteamId();
            this.runner = preview ? null : new BatchRunner();
            this.Text = "Steam Achievement Manager — Enhanced edition";
            this.BackColor = Color.FromArgb(15, 23, 42); this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5f); this.ClientSize = new Size(1180, 700);
            this.MinimumSize = new Size(960, 560); this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            var header = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(18, 10, 18, 0) };
            header.Controls.Add(new Label { Text = "ACHIEVEMENT AVAILABILITY  /  BULK ACTIONS", ForeColor = Color.FromArgb(125, 211, 252), Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true, Location = new Point(20, 44) });
            header.Controls.Add(new Label { Text = preview ? "SAM Enhanced  •  read-only preview" : "SAM Enhanced", Font = new Font("Segoe UI", 21, FontStyle.Bold), AutoSize = true, Location = new Point(18, 4) });
            var commands = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(14, 3, 0, 0), WrapContents = false };
            commands.Controls.Add(this.MakeButton("Refresh library", async (_, _) => await this.LoadLibrary()));
            commands.Controls.Add(this.MakeButton("Check selected", async (_, _) => await this.RunBatch(BatchAction.Check)));
            commands.Controls.Add(this.MakeButton("Unlock editable…", async (_, _) => await this.RunBatch(BatchAction.Unlock), Color.FromArgb(22, 101, 52)));
            commands.Controls.Add(this.MakeButton("Lock editable…", async (_, _) => await this.RunBatch(BatchAction.Lock), Color.FromArgb(133, 77, 14)));
            commands.Controls.Add(this.MakeButton("Open editor(s)", (_, _) => this.OpenEditors()));
            this.cancel = this.MakeButton("Cancel queued", (_, _) => this.cancellation?.Cancel());
            this.actionButtons.Remove(this.cancel); this.cancel.Enabled = false; commands.Controls.Add(this.cancel);

            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(18, 5, 0, 0), WrapContents = false };
            filters.Controls.Add(new Label { Text = "Search", AutoSize = true, Margin = new Padding(0, 5, 6, 0) });
            this.search.Width = 210; this.search.TextChanged += (_, _) => this.Rebuild(); filters.Controls.Add(this.search);
            this.filter.DropDownStyle = ComboBoxStyle.DropDownList; this.filter.Width = 190;
            this.filter.Items.AddRange(new object[] { "All availability states", "Client-editable", "Partly protected", "Protected", "No achievements", "Unknown / unavailable" });
            this.filter.SelectedIndex = 0; this.filter.SelectedIndexChanged += (_, _) => this.Rebuild(); filters.Controls.Add(this.filter);
            filters.Controls.Add(this.MakeButton("Select visible", (_, _) => { foreach (ListViewItem item in this.games.Items) item.Checked = true; }, width: 118));
            filters.Controls.Add(this.MakeButton("Clear selection", (_, _) => { this.checkedIds.Clear(); this.Rebuild(); }, width: 126));
            this.summary.AutoSize = true; this.summary.Margin = new Padding(14, 5, 0, 0); filters.Controls.Add(this.summary);
            var add = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 43, Padding = new Padding(18, 3, 0, 0), WrapContents = false };
            add.Controls.Add(new Label { Text = "Add App IDs / Store links", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
            this.manualIds.Width = 220; add.Controls.Add(this.manualIds);
            add.Controls.Add(this.MakeButton("Add games", (_, _) => this.AddManual(), width: 105));
            add.Controls.Add(new Label { Text = "Green: candidates   •   Amber: mixed   •   Red: protected   •   Gray: unknown", AutoSize = true, Margin = new Padding(16, 6, 0, 0), ForeColor = Color.Silver });

            this.games.Dock = DockStyle.Fill; this.games.View = View.Details; this.games.FullRowSelect = true;
            this.games.MultiSelect = true; this.games.CheckBoxes = true; this.games.HideSelection = false;
            this.games.ShowItemToolTips = true; this.games.BackColor = Color.FromArgb(17, 24, 39); this.games.ForeColor = Color.White;
            this.games.Columns.Add("Game", 280); this.games.Columns.Add("App ID", 90);
            this.games.Columns.Add("Achievement availability", 230); this.games.Columns.Add("Editable / Total", 120);
            this.games.Columns.Add("Protected", 90); this.games.Columns.Add("Last result", 330);
            this.games.ItemChecked += (_, e) => { if (!this.rebuilding) { var entry = (Entry)e.Item.Tag; if (e.Item.Checked) this.checkedIds.Add(entry.Id); else this.checkedIds.Remove(entry.Id); this.UpdateSummary(); } };
            this.games.DoubleClick += (_, _) => this.OpenEditors();
            var footer = new StatusStrip { BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White };
            this.status.Spring = true; this.status.TextAlign = ContentAlignment.MiddleLeft; this.status.Text = "Cached schema inspection never changes achievements.";
            this.progress.Minimum = 0; this.progress.Maximum = 100; this.progress.Visible = false;
            footer.Items.Add(this.status); footer.Items.Add(this.progress);
            this.Controls.Add(this.games); this.Controls.Add(add); this.Controls.Add(filters); this.Controls.Add(commands); this.Controls.Add(header); this.Controls.Add(footer);
            this.callbacks.Tick += (_, _) => { try { this.steam?.RunCallbacks(false); } catch (Exception e) { this.status.Text = "Steam callback error: " + e.Message; } };
            this.Shown += async (_, _) => { if (!this.demo) await this.LoadLibrary(); };
            this.FormClosing += (_, e) =>
            {
                if (this.busy) { e.Cancel = true; this.closeWhenDone = true; this.cancellation?.Cancel(); this.status.Text = "Stopping queued games; waiting for active saves/checks to finish…"; }
                else this.callbacks.Stop();
            };
            if (preview) this.LoadDemo(); else this.callbacks.Start();
        }

        private Button MakeButton(string text, EventHandler click, Color? color = null, int width = 142)
        {
            var button = new Button { Text = text, Width = width, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = color ?? Color.FromArgb(51, 65, 85), ForeColor = Color.White, Margin = new Padding(3, 0, 3, 0) };
            button.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105); button.Click += click; this.actionButtons.Add(button); return button;
        }

        private async Task LoadLibrary()
        {
            if (this.busy || this.loading || this.demo) return;
            this.loading = true; this.EnableActions(false); this.status.Text = "Reading the local Steam library cache…";
            try
            {
                string steamPath = API.Steam.GetInstallPath();
                var candidates = await Task.Run(() => LibraryIndex.Read(steamPath));
                if (this.IsDisposed) return;
                this.entries.Clear(); this.checkedIds.Clear();
                int count = 0;
                foreach (var candidate in candidates.OrderBy(p => p.Value, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (this.IsDisposed) return;
                    if (this.steam.SteamApps008.IsSubscribedApp(candidate.Key))
                        this.entries[candidate.Key] = new Entry { Id = candidate.Key, Name = candidate.Value, Inspection = SchemaInspector.ReadCached(steamPath, candidate.Key) };
                    if (++count % 30 == 0) { this.status.Text = "Checking ownership: " + count + " / " + candidates.Count; this.Rebuild(); await Task.Delay(1); }
                }
                this.Rebuild(); this.status.Text = "Loaded " + this.entries.Count + " games. Availability is based on cached permissions until you choose Check selected.";
            }
            catch (Exception e) { this.status.Text = "Library load failed: " + e.Message; }
            finally { this.loading = false; if (!this.IsDisposed) this.EnableActions(true); }
        }

        private void Rebuild()
        {
            if (this.IsDisposed) return;
            this.rebuilding = true; this.games.BeginUpdate();
            try
            {
                this.games.Items.Clear();
                foreach (var entry in this.entries.Values.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (this.search.Text.Length > 0 && entry.Name.IndexOf(this.search.Text, StringComparison.OrdinalIgnoreCase) < 0 && !entry.Id.ToString().Contains(this.search.Text)) continue;
                    Availability availability = entry.Inspection.Availability;
                    bool shown = this.filter.SelectedIndex switch
                    {
                        1 => availability == Availability.ClientEditable,
                        2 => availability == Availability.PartlyProtected,
                        3 => availability == Availability.Protected,
                        4 => availability == Availability.NoAchievements,
                        5 => availability == Availability.Unknown || availability == Availability.Unavailable || availability == Availability.NoLicense,
                        _ => true,
                    };
                    if (!shown) continue;
                    var item = new ListViewItem(entry.Name) { Tag = entry, Checked = this.checkedIds.Contains(entry.Id), ForeColor = StatusColor(availability), ToolTipText = entry.Inspection.Detail + "\n" + entry.Last };
                    item.SubItems.Add(entry.Id.ToString(CultureInfo.InvariantCulture)); item.SubItems.Add(entry.Inspection.StatusText);
                    item.SubItems.Add(availability == Availability.Unknown || availability == Availability.Unavailable ? "?" : entry.Inspection.EditableCount + " / " + entry.Inspection.Total);
                    item.SubItems.Add(availability == Availability.Unknown ? "?" : entry.Inspection.ProtectedCount.ToString()); item.SubItems.Add(entry.Last);
                    this.games.Items.Add(item);
                }
            }
            finally { this.games.EndUpdate(); this.rebuilding = false; this.UpdateSummary(); }
        }

        private static Color StatusColor(Availability state) => state switch
        {
            Availability.ClientEditable => Color.FromArgb(134, 239, 172),
            Availability.PartlyProtected => Color.FromArgb(253, 230, 138),
            Availability.Protected => Color.FromArgb(252, 165, 165),
            Availability.NoAchievements => Color.FromArgb(148, 163, 184),
            _ => Color.FromArgb(203, 213, 225),
        };

        private void UpdateSummary() => this.summary.Text = this.checkedIds.Count + " checked / " + this.games.Items.Count + " visible";
        private List<Entry> SelectedEntries() => this.checkedIds.Count > 0 ? this.checkedIds.Where(this.entries.ContainsKey).Select(id => this.entries[id]).ToList() : this.games.SelectedItems.Cast<ListViewItem>().Select(item => (Entry)item.Tag).ToList();
        private void EnableActions(bool enable) { foreach (var button in this.actionButtons) button.Enabled = enable && !this.demo; this.cancel.Enabled = this.busy; }

        private void AddManual()
        {
            if (this.demo || this.busy) return;
            string text = Regex.Replace(this.manualIds.Text, @"https?://store\.steampowered\.com/app/(\d+)[^\s,]*", "$1", RegexOptions.IgnoreCase);
            int added = 0;
            foreach (string token in Regex.Split(text, @"[\s,;]+"))
            {
                if (!uint.TryParse(token, out uint id) || id == 0 || !this.steam.SteamApps008.IsSubscribedApp(id)) continue;
                if (!this.entries.ContainsKey(id)) this.entries[id] = new Entry { Id = id, Name = this.steam.SteamApps001.GetAppData(id, "name") ?? "App " + id, Inspection = SchemaInspector.ReadCached(API.Steam.GetInstallPath(), id) };
                this.checkedIds.Add(id); added++;
            }
            this.Rebuild(); this.status.Text = "Added/selected " + added + " valid licensed games."; this.manualIds.Clear();
        }

        private async Task RunBatch(BatchAction action)
        {
            if (this.busy || this.demo) return;
            var selected = this.SelectedEntries();
            if (selected.Count == 0) { MessageBox.Show(this, "Select or check one or more games first.", "Select games"); return; }
            if (action != BatchAction.Check && selected.Any(e => this.editors.TryGetValue(e.Id, out var editor) && !editor.HasExited))
            { MessageBox.Show(this, "Close the open achievement editors for these games before a bulk action, so changes cannot conflict.", "Editors still open"); return; }
            this.busy = true; this.cancellation = new CancellationTokenSource(); this.EnableActions(false);
            this.progress.Visible = true; this.progress.Maximum = selected.Count; this.progress.Value = 0;
            int done = 0;
            try
            {
                this.status.Text = "Read-only check for " + selected.Count + " games (up to two workers at once)…";
                var requests = selected.Select(e => new BatchRequest { GameId = e.Id, ExpectedSteamId = this.account, Action = action }).ToArray();
                var inspections = await this.runner.RunAsync(requests, false, result =>
                {
                    this.Record(result, false); this.progress.Value = Math.Min(++done, this.progress.Maximum); this.status.Text = "Read-only check: " + done + " / " + selected.Count;
                }, this.cancellation.Token);
                foreach (var result in inspections) this.Record(result, false);
                if (this.cancellation.IsCancellationRequested) { this.status.Text = "Check cancelled; no achievement changes made."; return; }
                if (action == BatchAction.Check) { this.status.Text = "Read-only check complete for " + selected.Count + " games. See availability and Last result."; return; }
                using var review = new BatchPreviewDialog(action, selected.ToDictionary(e => e.Id, e => e.Name), inspections);
                if (review.ShowDialog(this) != DialogResult.OK) { this.status.Text = "Preview closed. No achievement changes made."; return; }
                var approved = inspections.Where(r => r.Inspection.Live && r.Inspection.ChangesFor(action) > 0 && r.Inspection.Availability != Availability.Unknown)
                    .Select(r => new BatchRequest { GameId = r.Inspection.GameId, ExpectedSteamId = this.account, Action = action, Approved = true,
                        AchievementIds = r.Inspection.Achievements.Where(a => !a.Protected && a.Readable && a.Achieved != (action == BatchAction.Unlock)).Select(a => a.Id).ToList() }).ToArray();
                this.progress.Maximum = Math.Max(1, approved.Length); this.progress.Value = 0; done = 0;
                this.status.Text = "Applying the confirmed plan. Cancel stops queued games; active saves finish.";
                var applied = await this.runner.RunAsync(approved, true, result =>
                {
                    this.Record(result, true); this.progress.Value = Math.Min(++done, this.progress.Maximum); this.status.Text = "Applying: " + done + " / " + approved.Length;
                }, this.cancellation.Token);
                foreach (var result in applied) this.Record(result, true);
                MessageBox.Show(this, applied.Sum(r => r.Confirmed) + " achievement changes confirmed.\n" + applied.Count(r => r.Outcome == SaveOutcome.Unconfirmed) + " games with unconfirmed saves.\n" + applied.Count(r => r.Outcome == SaveOutcome.Failed || r.Outcome == SaveOutcome.NotApplied) + " games failed or not applied.\n" + applied.Count(r => r.Outcome == SaveOutcome.Cancelled) + " queued games cancelled.\n\nSee each game's Last result column for details.", "Batch result", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.status.Text = "Batch finished. Protected entries were skipped; account statistics were not reset.";
            }
            catch (Exception e) { this.status.Text = "Batch error: " + e.Message; }
            finally
            {
                this.busy = false; this.progress.Visible = false; this.cancellation.Dispose(); this.cancellation = null; this.EnableActions(true);
                if (this.closeWhenDone) this.Close();
            }
        }

        private void Record(BatchResult result, bool apply)
        {
            if (!this.entries.TryGetValue(result.Inspection.GameId, out var entry)) return;
            if (result.Outcome != SaveOutcome.Cancelled) entry.Inspection = result.Inspection;
            entry.Last = apply ? result.Outcome + ": " + result.Detail : result.Detail;
            this.Rebuild();
        }

        private void OpenEditors()
        {
            if (this.demo || this.busy) return;
            foreach (var entry in this.SelectedEntries())
            {
                try
                {
                    if (this.editors.TryGetValue(entry.Id, out var current) && !current.HasExited) continue;
                    this.editors[entry.Id] = Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SAM.Game.exe"), entry.Id.ToString(CultureInfo.InvariantCulture)) { UseShellExecute = false, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory });
                }
                catch (Exception e) { entry.Last = "Editor failed: " + e.Message; }
            }
            this.Rebuild();
        }

        private void LoadDemo()
        {
            this.entries[101] = new Entry { Id = 101, Name = "Example: editable game", Inspection = new GameInspection { GameId = 101, Availability = Availability.ClientEditable, Achievements = Enumerable.Range(1, 32).Select(i => new AchievementRule { Id = "A" + i }).ToList(), Detail = "Demo data only." } };
            this.entries[102] = new Entry { Id = 102, Name = "Example: mixed permissions", Inspection = new GameInspection { GameId = 102, Availability = Availability.PartlyProtected, Achievements = Enumerable.Range(1, 20).Select(i => new AchievementRule { Id = "A" + i, Protected = i > 15 }).ToList(), Detail = "Demo data only." } };
            this.entries[103] = new Entry { Id = 103, Name = "Example: protected game", Inspection = new GameInspection { GameId = 103, Availability = Availability.Protected, Achievements = new() { new AchievementRule { Id = "SERVER", Protected = true } }, Detail = "Demo data only." } };
            this.entries[104] = new Entry { Id = 104, Name = "Example: no achievements", Inspection = new GameInspection { GameId = 104, Availability = Availability.NoAchievements, Detail = "Demo data only." } };
            this.entries[105] = new Entry { Id = 105, Name = "Example: uncached / unknown", Inspection = new GameInspection { GameId = 105, Availability = Availability.Unknown, Detail = "Demo data only. Missing cache does not mean protected." } };
            this.checkedIds.Add(101); this.checkedIds.Add(102); this.Rebuild(); this.EnableActions(false);
            this.status.Text = "Read-only UI preview with synthetic examples. No Steam connection or achievement changes.";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { this.callbacks.Dispose(); foreach (var editor in this.editors.Values) editor.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
