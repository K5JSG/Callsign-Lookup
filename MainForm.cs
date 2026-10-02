using System.Text;
using CallsignLookup.Services;

namespace CallsignLookup
{
    public partial class MainForm : Form
    {
        private readonly AppSettings _settings;
        private QrzService? _qrz;

        public MainForm()
        {
            InitializeComponent();
            Icon = AppLogo.Icon ?? Icon;
            pictureBoxLogo.Image = AppLogo.Image;
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
            _ = IotaListUpdater.RefreshIfStaleAsync();
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
