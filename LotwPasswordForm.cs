namespace CallsignLookup
{
    // Asks for the password of the TQSL certificate, when TQSL wants one to
    // sign the QSOs. Only held for this send: never saved, logged or shown.
    public sealed class LotwPasswordForm : Form
    {
        private readonly TextBox _password = new();

        public string Password => _password.Text;

        public LotwPasswordForm(string locationNames)
        {
            Text = "LoTW certificate password";
            Icon = AppLogo.Icon ?? Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            var text = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Text = $"TQSL needs the password of your LoTW certificate to sign the QSOs for {locationNames}.\n\n" +
                       "It's used for this send only and isn't saved. Skip leaves these QSOs unsent to LoTW " +
                       "(HRD's own upload can send them later).",
            };
            _password.UseSystemPasswordChar = true;
            _password.Width = 260;

            var ok = new Button { Text = "Send", DialogResult = DialogResult.OK, AutoSize = true };
            var skip = new Button { Text = "Skip LoTW", DialogResult = DialogResult.Cancel, AutoSize = true };
            AcceptButton = ok;
            CancelButton = skip;

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            buttons.Controls.Add(skip);
            buttons.Controls.Add(ok);

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            layout.Controls.Add(text);
            layout.Controls.Add(new Label { Text = "Password:", AutoSize = true, Margin = new Padding(3, 12, 3, 0) });
            layout.Controls.Add(_password);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            FormClosed += (_, _) => _password.Clear();
        }
    }
}
