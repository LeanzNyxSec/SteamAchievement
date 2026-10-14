// SAM Enhanced modifications, 2026. Distributed under the included zlib license.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SAM.Enhanced;

namespace SAM.Picker
{
    public sealed class BatchPreviewDialog : Form
    {
        public BatchPreviewDialog(BatchAction action, Dictionary<uint, string> names, List<BatchResult> results)
        {
            string verb = action == BatchAction.Unlock ? "Unlock" : "Lock";
            var eligible = results.Where(r => r.Inspection.Live && r.Inspection.Availability != Availability.Unknown && r.Inspection.ChangesFor(action) > 0).ToArray();
            int count = eligible.Sum(r => r.Inspection.ChangesFor(action));
            this.Text = "Review achievement changes — " + verb;
            this.StartPosition = FormStartPosition.CenterParent; this.ClientSize = new Size(1020, 500); this.MinimumSize = new Size(800, 400);
            this.BackColor = Color.FromArgb(15, 23, 42); this.ForeColor = Color.White; this.Font = new Font("Segoe UI", 10);
            var heading = new Label { Dock = DockStyle.Top, Height = 74, Padding = new Padding(16, 14, 0, 0), Text = verb + " " + count + " editable achievements across " + eligible.Length + " games?\nProtected, unreadable and unavailable entries will be skipped. Game statistics are not reset." };
            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BackColor = Color.FromArgb(17, 24, 39), ForeColor = Color.White, ShowItemToolTips = true };
            list.Columns.Add("Game", 260); list.Columns.Add("Availability", 205); list.Columns.Add("To change", 95); list.Columns.Add("Protected", 95); list.Columns.Add("Read-only result", 330);
            foreach (var result in results)
            {
                var inspection = result.Inspection;
                var item = new ListViewItem(names.TryGetValue(inspection.GameId, out string name) ? name : "App " + inspection.GameId) { ToolTipText = result.Detail };
                item.SubItems.Add(inspection.StatusText); item.SubItems.Add(inspection.Live ? inspection.ChangesFor(action).ToString() : "0");
                item.SubItems.Add(inspection.ProtectedCount.ToString()); item.SubItems.Add(result.Detail); list.Items.Add(item);
            }
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 0) };
            var apply = new Button { Text = "Confirm " + verb.ToLowerInvariant(), Width = 150, Height = 34, DialogResult = DialogResult.OK, Enabled = count > 0 };
            var cancel = new Button { Text = "Cancel — no changes", Width = 170, Height = 34, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(apply); buttons.Controls.Add(cancel); this.AcceptButton = apply; this.CancelButton = cancel;
            this.Controls.Add(list); this.Controls.Add(heading); this.Controls.Add(buttons);
        }
    }
}
