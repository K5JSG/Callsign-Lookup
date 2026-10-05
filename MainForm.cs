using System.Text;
using CallsignLookup.Services;
using CallsignLookup.Services.Hrd;

namespace CallsignLookup
{
    public partial class MainForm : Form
    {
        private readonly AppSettings _settings;
        private QrzService? _qrz;

        // The last lookup, so a grid square typed into the Grid Square box can
        // be worked out against the same QRZ record.
        private LookupResult? _lastResult;

        // HRD Logbook's open Edit/Add window, watched so its callsign is
        // looked up as soon as a QSO is opened there.
        private readonly System.Windows.Forms.Timer _hrdTimer = new() { Interval = 1000 };
        private HrdEditWindow? _hrdWindow;
        private string _hrdQsoKey = "";     // callsign + date + time of the QSO last shown
        private string _hrdSeenKey = "";    // ... and of the one seen on the last check
        private bool _hrdChecking, _hrdFilling;
        private int _hrdTicks;

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

            LoadStationProfiles();
            _hrdTimer.Tick += HrdTimer_Tick;
            _hrdTimer.Start();
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

            await LookupAsync(callsign);
        }

        // True when the lookup found the callsign.
        private async Task<bool> LookupAsync(string callsign)
        {
            if (!_settings.HasQrzLogin && !PromptForQrzLogin()) return false;

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
                return true;
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
            return false;
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

        // Empties the window - callsign and results. HRD isn't touched, and
        // the QSO open there (if any) isn't looked up again until another
        // one is opened or its callsign changes.
        private void BtnClear_Click(object? sender, EventArgs e)
        {
            txtCallsign.Clear();
            ClearResults();
            UpdateStatus("Cleared.");
            txtCallsign.Focus();
        }

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
            btnFillHrd.Enabled = !busy && _hrdWindow != null;
            UseWaitCursor = busy;
        }

        // ---- HRD Logbook ----------------------------------------------------

        private void LoadStationProfiles()
        {
            var profiles = HrdStationProfiles.Load();
            cbxStationProfile.Items.Clear();
            foreach (var profile in profiles) cbxStationProfile.Items.Add(profile);
            cbxStationProfile.SelectedItem = profiles.FirstOrDefault(p => p.DisplayName == _settings.HrdStationProfile)
                                             ?? profiles.FirstOrDefault();
            cbxStationProfile.Enabled = profiles.Count > 0;
        }

        private void CbxStationProfile_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cbxStationProfile.SelectedItem is not HrdStationProfile profile ||
                profile.DisplayName == _settings.HrdStationProfile) return;
            _settings.HrdStationProfile = profile.DisplayName;
            _settings.Save();
        }

        // Every second: which QSO is open in HRD? When it changes - another
        // QSO opened, or the callsign edited or deleted - and has stayed the
        // same for a second (so typing a callsign doesn't look up every
        // letter), it's looked up, ready for when the user has done HRD's own
        // Lookup. A blank callsign clears the results.
        private async void HrdTimer_Tick(object? sender, EventArgs e)
        {
            if (_hrdChecking || _hrdFilling) return;
            _hrdChecking = true;
            try
            {
                // Looking for the window means searching all of HRD's main
                // window (log grid and all), so between full searches every
                // 5 seconds the one already found is just re-read.
                var known = _hrdTicks++ % 5 == 0 ? null : _hrdWindow;
                var (window, call, key) = await Task.Run(() => ReadOpenQso(known));
                _hrdWindow = window;
                btnFillHrd.Enabled = window != null && btnLookup.Enabled;
                if (window == null)
                {
                    lblHrdQso.Text = "no QSO open";
                    _hrdQsoKey = _hrdSeenKey = "";
                    return;
                }

                lblHrdQso.Text = call.Length > 0 ? $"{call} open" : "QSO open, no callsign";
                bool settled = key == _hrdSeenKey;
                _hrdSeenKey = key;
                if (!settled || key == _hrdQsoKey) return;
                if (!btnLookup.Enabled) return; // a lookup is running - try again next time
                _hrdQsoKey = key;

                txtCallsign.Text = call;
                if (call.Length == 0)
                {
                    ClearResults();
                    UpdateStatus("The QSO open in HRD has no callsign.");
                    return;
                }
                await LookupAsync(call);
            }
            finally
            {
                _hrdChecking = false;
            }
        }

        // The fields are read straight from the window each time (not from
        // cached controls), so another QSO shown in it is picked up.
        private static (HrdEditWindow? Window, string Call, string Key) ReadOpenQso(HrdEditWindow? known)
        {
            try
            {
                var window = known is { IsOpen: true } ? known : HrdEditWindow.Find();
                if (window == null) return (null, "", "");
                string call = (window.ReadTopField("edtCALL") ?? "").ToUpperInvariant();
                string key = $"{call}|{window.ReadTopField("edtQSO_DATE")}|{window.ReadTopField("edtTIME_ON")}";
                return (window, call, key);
            }
            catch (Exception ex) when (ex is System.Windows.Automation.ElementNotAvailableException
                                           or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return (null, "", "");
            }
        }

        private async void BtnFillHrd_Click(object? sender, EventArgs e)
        {
            if (_hrdWindow is not HrdEditWindow window || !window.IsOpen)
            {
                UpdateStatus("No QSO is open in HRD Logbook - open it there first.");
                return;
            }
            string call = (await Task.Run(() => window.Read("edtCALL")) ?? "").ToUpperInvariant();
            if (call.Length == 0)
            {
                UpdateStatus("The QSO open in HRD has no callsign.");
                return;
            }

            // Always a fresh lookup of the callsign that's open, in case it
            // was changed in HRD since it was opened.
            txtCallsign.Text = call;
            if (!await LookupAsync(call) || _lastResult is not LookupResult lookup) return;

            if (!lookup.Qrz.Call.Equals(call, StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(this,
                    $"QRZ's page for {call} is the record for {lookup.Qrz.Call}.\n\n" +
                    "That's normal for a portable or changed callsign, but it can also mean it's the wrong station. " +
                    $"Fill in the QSO from {lookup.Qrz.Call}'s QRZ record anyway?",
                    "QRZ record doesn't match", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            var profile = cbxStationProfile.SelectedItem as HrdStationProfile;
            _hrdFilling = true;
            SetBusy(true);
            UpdateStatus($"Filling in {call} in HRD - leave HRD alone until it's done...");
            try
            {
                var report = await Task.Run(() =>
                {
                    var qso = HrdQsoFiller.ReadQso(window);
                    var plan = HrdQsoFiller.Plan(qso, lookup, HrdPotaParks.Find, profile);
                    return HrdQsoFiller.Apply(window, plan);
                });
                int changed = report.Applied.Count(a => a.Took);
                UpdateStatus($"{call}: {changed} field{(changed == 1 ? "" : "s")} filled in HRD" +
                             (report.Problems.Count > 0 ? $", {report.Problems.Count} to do by hand" : "") +
                             " - check it, then press Update in HRD.");
                using var dialog = new HrdFillReportForm(report);
                switch (dialog.ShowDialog(this))
                {
                    case DialogResult.Yes:
                        UpdateStatus($"Saving {call} in HRD...");
                        bool saved = await Task.Run(window.PressUpdateAndWait);
                        UpdateStatus(saved
                            ? $"{call} saved in HRD."
                            : $"Pressed Update for {call}, but HRD's window is still open - check HRD.");
                        break;
                }
            }
            catch (Exception ex) when (ex is HrdException or System.Windows.Automation.ElementNotAvailableException
                                           or InvalidOperationException)
            {
                UpdateStatus($"Couldn't finish filling in HRD: {ex.Message}");
            }
            finally
            {
                _hrdFilling = false;
                SetBusy(false);
            }
        }

        private void UpdateStatus(string text) => lblStatus.Text = text;
    }
}
