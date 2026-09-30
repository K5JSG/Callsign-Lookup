namespace CallsignLookup
{
    public partial class QrzLoginForm : Form
    {
        public QrzLoginForm(string username, string password)
        {
            InitializeComponent();
            txtUsername.Text = username;
            txtPassword.Text = password;
        }

        public string Username => txtUsername.Text.Trim();
        public string Password => txtPassword.Text;

        private void BtnOk_Click(object? sender, EventArgs e)
        {
            if (Username.Length == 0 || Password.Length == 0)
            {
                MessageBox.Show(this, "Enter both your QRZ username and password.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
