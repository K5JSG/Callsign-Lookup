using CallsignLookup.Services.Hrd;

namespace CallsignLookup
{
    // What "Fill HRD QSO" did: fields it changed that already had something in
    // them (to check), fields it filled in, the My Station switch, warnings,
    // and anything HRD wouldn't take. Built in code - it's just a list.
    //
    // Closes with DialogResult.Yes for Approve (press Update in HRD), or Cancel.
    public sealed class HrdFillReportForm : Form
    {
        public HrdFillReportForm(HrdFillReport report)
        {
            Text = $"Filled in HRD - {report.Plan.Callsign}";
            Icon = AppLogo.Icon ?? Icon;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 480);
            MinimumSize = new Size(520, 320);
            ShowInTaskbar = false;

            var intro = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(740, 0),
                Padding = new Padding(10, 10, 10, 6),
                Text = "Check the QSO in HRD. Approve presses Update (F7) in HRD to save it; nothing is saved until " +
                       "then. Close leaves it open in HRD for you to finish there.",
            };

            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
            };
            list.Columns.Add("Field", 210);
            list.Columns.Add("Was", 250);
            list.Columns.Add("Now", 250);

            // Sections as bold heading rows rather than ListView groups, whose
            // headings stay dark blue in dark mode.
            bool dark = Application.IsDarkModeEnabled;
            Color problemColor = dark ? Color.Salmon : Color.Firebrick;
            Color noteColor = dark ? Color.Khaki : Color.DarkGoldenrod;

            var changed = new List<string[]>();
            var filled = new List<string[]>();
            var station = new List<string[]>();

            foreach (var applied in report.Applied.Where(a => a.Took && !HrdQsoFiller.SameText(a.Change.Old, a.Actual)))
            {
                var c = applied.Change;
                string now = applied.Actual.Length > 0 ? applied.Actual : "(blank)";
                (c.WasBlank ? filled : changed).Add([c.Label, c.WasBlank ? "" : c.Old, now]);
            }

            if (report.DistanceNew.Length > 0 && report.DistanceNew != report.DistanceOld)
                (report.DistanceOld.Length > 0 ? changed : filled).Add(["Distance (Recalc)", report.DistanceOld, report.DistanceNew]);

            if (report.ProfileSelected)
            {
                if (report.StationChanges.Count == 0)
                    station.Add(["Profile", "", "re-selected - no My Station fields changed"]);
                foreach (var (field, old, now) in report.StationChanges) station.Add([field, old, now]);
            }

            var bold = new Font(list.Font, FontStyle.Bold);
            void Section(string title, List<string[]> rows)
            {
                if (rows.Count == 0) return;
                if (list.Items.Count > 0) list.Items.Add(new ListViewItem(""));
                list.Items.Add(new ListViewItem(title) { Font = bold });
                foreach (var row in rows) list.Items.Add(new ListViewItem(row));
            }

            Section("Changed (please verify)", changed);
            Section("Filled in (was blank)", filled);
            Section(report.Plan.Profile is HrdStationProfile p ? $"My Station: {p.DisplayName}" : "My Station", station);
            if (list.Items.Count == 0) list.Items.Add(new ListViewItem(["", "Everything already matched - nothing changed.", ""]));

            // Problems and notes in full above the list - they're sentences,
            // too long for its columns.
            Label Lines(IEnumerable<string> lines, string heading, Color color) => new()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(ClientSize.Width, 0),
                Padding = new Padding(10, 0, 10, 6),
                ForeColor = color,
                Text = heading + Environment.NewLine + string.Join(Environment.NewLine, lines.Select(l => "• " + l)),
            };
            var messages = new List<Label>();
            if (report.Problems.Count > 0)
                messages.Add(Lines(report.Problems, "Couldn't set - please do these by hand in HRD:", problemColor));
            if (report.Plan.Warnings.Count > 0)
                messages.Add(Lines(report.Plan.Warnings, "Check:", noteColor));

            var approve = new Button { Text = "&Approve - Update in HRD", AutoSize = true, DialogResult = DialogResult.Yes };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            var tips = new ToolTip();
            tips.SetToolTip(approve, "Presses Update (F7) in HRD's window, saving the QSO as it is now.");
            tips.SetToolTip(close, "Closes this report. The QSO stays open in HRD, not saved.");
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 4, 8, 4),
            };
            buttons.Controls.Add(close);
            buttons.Controls.Add(approve);
            CancelButton = close;
            // Nothing pre-selected: saving the QSO should be a deliberate click.
            Shown += (_, _) => list.Focus();

            // Docked to the top in reverse: the last added sits highest.
            Controls.Add(list);
            Controls.Add(buttons);
            for (int i = messages.Count - 1; i >= 0; i--) Controls.Add(messages[i]);
            Controls.Add(intro);
            Resize += (_, _) =>
            {
                foreach (var label in messages.Append(intro)) label.MaximumSize = new Size(ClientSize.Width, 0);
            };
        }
    }
}
