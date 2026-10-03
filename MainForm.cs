using System.Text;
using CallsignLookup.Services;

namespace CallsignLookup
{
    public partial class MainForm : Form
    {
        private readonly AppSettings _settings;
        private QrzService? _qrz;

        // The last lookup, so a grid square typed into the Grid Square box can
        // be worked out against the same QRZ record.
        private LookupResult? _lastResult;

        public MainForm()
        {
            InitializeComponent();
            Icon = AppLogo.Icon ?? Icon;
            pictureBoxLogo.Image = AppLogo.Image;
            // The warning bar wraps to the window's width.
            Resize += (_, _) => lblIotaUpdate.MaximumSize = new Size(ClientSize.Width, 0);
            _settings = AppSettings.Load();
            UpdateStatus(_settings.HasQrzLogin
                ? $"QRZ login: {_settings.QrzUsername}"
                : "Click \"QRZ Login...\" to enter your QRZ.com username and password.");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            txtCallsign.Focus();
            if (!_settings.HasQrzLogin) PromptForQrzLogin();

            // Weekly refresh of the IOTA list in the background - lookups use
            // the saved (or shipped) copy until it's done, and if it fails.
            ShowIotaUpdateWarning();
            _ = RefreshIotaListAsync();
        }

        private async Task RefreshIotaListAsync()
        {
            if (await IotaListUpdater.RefreshIfStaleAsync()) ShowIotaUpdateWarning();
        }

        // IOTA adds islands now and then. The new list arrives by itself, but
        // their outlines only come with a new version of the island data, so
        // until then a station on one can't be placed on it - say so.
        private void ShowIotaUpdateWarning()
        {
            var missing = IotaService.IslandsMissingOutlines();
            if (missing.Count > 0)
            {
                string examples = string.Join(", ", missing.Take(3)) + (missing.Count > 3 ? ", ..." : "");
                lblIotaUpdate.Text =
                    $"IOTA has added {missing.Count} new island{(missing.Count == 1 ? "" : "s")} ({examples}) " +
                    "since this version's island data was built - the island data needs updating " +
                    "before stations on them can be found. Check for a newer Callsign Lookup.";
            }

            bool show = missing.Count > 0;
            if (show == lblIotaUpdate.Visible) return;

            // Grow (or shrink) the window by the bar's height rather than
            // squeezing the results out of view.
            lblIotaUpdate.MaximumSize = new Size(ClientSize.Width, 0);
            int change = lblIotaUpdate.GetPreferredSize(new Size(ClientSize.Width, 0)).Height * (show ? 1 : -1);
            lblIotaUpdate.Visible = show;
            MinimumSize = new Size(MinimumSize.Width, MinimumSize.Height + change);
            Height += change;
        }

        private void BtnQrzLogin_Click(object? sender, EventArgs e) => PromptForQrzLogin();

        private bool PromptForQrzLogin()
        {
            using var dialog = new QrzLoginForm(_settings.QrzUsername, _settings.GetQrzPassword());
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;

            _settings.QrzUsername = dialog.Username;
            _settings.SetQrzPassword(dialog.Password);
            _settings.Save();
            _qrz = null; // log in again with the new credentials on the next lookup
            UpdateStatus($"QRZ login: {_settings.QrzUsername}");
            return true;
        }

        private async void BtnLookup_Click(object? sender, EventArgs e)
        {
            string callsign = txtCallsign.Text.Trim();
            if (callsign.Length == 0)
            {
                txtCallsign.Focus();
                return;
            }

            if (!_settings.HasQrzLogin && !PromptForQrzLogin()) return;

            SetBusy(true);
            UpdateStatus($"Looking up {callsign} on QRZ...");
            try
            {
                _qrz ??= new QrzService(_settings.QrzUsername, _settings.GetQrzPassword());
                var result = await new CallsignLookupService(_qrz).LookupAsync(callsign);
                _lastResult = result;
                ShowResult(result);
                string status = $"Found {result.Qrz.Call}.";
                // Worth knowing when the lat/long isn't QRZ's own.
                if (result.Qrz.Latitude == null || result.Qrz.Longitude == null)
                    status += $"  Location: {result.LocationSource}.";
                if (result.Iota?.Note.Length > 0) status += $"  IOTA: {result.Iota.Note}.";
                if (_qrz.LoginMessage.Length > 0) status += $"  QRZ: {_qrz.LoginMessage}";
                UpdateStatus(status);
            }
            catch (QrzException ex)
            {
                ClearResults();
                UpdateStatus($"QRZ: {ex.Message}");
                // A bad password gives a login error on every lookup - drop the
                // session so the next try logs in fresh.
                _qrz = null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
            {
                ClearResults();
                UpdateStatus($"Couldn't reach QRZ: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        // The Grid Square box is editable: paste or type the right grid (when
        // the QRZ record's is wrong) and everything is worked out again from
        // its center. A complete 6-character grid updates as soon as it's in;
        // Enter takes a 4-character one too.
        private void TxtGridSquare_TextChanged(object? sender, EventArgs e)
        {
            if (Maidenhead.Normalize(txtGridSquare.Text).Length == 6) ApplyEnteredGrid(quiet: true);
        }

        private void TxtGridSquare_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            ApplyEnteredGrid(quiet: false);
        }

        // Enter in the grid box means "use this grid", not "look up the callsign".
        private void TxtGridSquare_Enter(object? sender, EventArgs e) => AcceptButton = null;

        private void TxtGridSquare_Leave(object? sender, EventArgs e) => AcceptButton = btnLookup;

        private void ApplyEnteredGrid(bool quiet)
        {
            string grid = Maidenhead.Normalize(txtGridSquare.Text);
            if (grid.Length == 0) return;
            // Already showing this grid's results - including when ShowResult
            // has just put it in the box.
            if (_lastResult != null && grid.Equals(_lastResult.GridSquare, StringComparison.OrdinalIgnoreCase)) return;

            // With no callsign looked up there's no DXCC entity or state, so
            // no county or section - but the zones and island still work.
            var record = _lastResult?.Qrz ?? new QrzCallsignRecord();
            var result = CallsignLookupService.ResolveAtGrid(record, grid);
            if (result == null)
            {
                if (!quiet) UpdateStatus($"\"{txtGridSquare.Text.Trim()}\" isn't a valid 4- or 6-character grid square.");
                return;
            }

            _lastResult = result;
            int caret = txtGridSquare.SelectionStart;
            ShowResult(result);
            txtGridSquare.SelectionStart = Math.Min(caret, txtGridSquare.TextLength);

            string status = record.Call.Length > 0
                ? $"{record.Call} worked out from grid {grid} (entered), not QRZ's location."
                : $"Worked out from grid {grid} (entered) - no callsign looked up, so no county or section.";
            if (result.Iota?.Note.Length > 0) status += $"  IOTA: {result.Iota.Note}.";
            UpdateStatus(status);
        }

        private void ShowResult(LookupResult result)
        {
            txtGridSquare.Text = Dash(result.GridSquare);
            txtCqZone.Text = result.CqZone?.ToString() ?? "-";
            txtItuZone.Text = result.ItuZone?.ToString() ?? "-";
            txtCounty.Text = result.County?.County ?? "-";
            // US: the county's state. Canada: the province QRZ has (it's what
            // the section is based on). Anywhere else there's no state.
            txtState.Text = result.County?.StateAbbrev
                ?? (result.Qrz.IsCanadian ? Dash(result.Qrz.State) : "-");
            txtArrlSection.Text = result.ArrlSection?.Section ?? "-";
            txtIota.Text = result.Iota?.Reference ?? "-";
            txtIsland.Text = Dash(result.Iota?.Island ?? "");
            btnCopy.Enabled = true;
        }

        private void ClearResults()
        {
            _lastResult = null;
            foreach (var (_, box) in ResultFields) box.Clear();
            btnCopy.Enabled = false;
        }

        // Label/value pairs in display order, for clearing and "Copy All".
        private (Label Label, TextBox Box)[] ResultFields =>
        [
            (lblGridSquare, txtGridSquare),
            (lblCqZone, txtCqZone),
            (lblItuZone, txtItuZone),
            (lblCounty, txtCounty),
            (lblState, txtState),
            (lblArrlSection, txtArrlSection),
            (lblIota, txtIota),
            (lblIsland, txtIsland),
        ];

        private static string Dash(string value) => value.Length > 0 ? value : "-";

        private void BtnCopy_Click(object? sender, EventArgs e)
        {
            var sb = new StringBuilder();
            foreach (var (label, box) in ResultFields)
                sb.AppendLine($"{label.Text}	{box.Text}");
            Clipboard.SetText(sb.ToString());
            UpdateStatus("Results copied to the clipboard.");
        }

        private void SetBusy(bool busy)
        {
            btnLookup.Enabled = !busy;
            btnQrzLogin.Enabled = !busy;
            UseWaitCursor = busy;
        }

        private void UpdateStatus(string text) => lblStatus.Text = text;
    }
}
