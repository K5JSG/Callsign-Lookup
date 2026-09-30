namespace CallsignLookup
{
    partial class QrzLoginForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            lblInfo = new Label();
            lblUsername = new Label();
            txtUsername = new TextBox();
            lblPassword = new Label();
            txtPassword = new TextBox();
            btnOk = new Button();
            btnCancel = new Button();
            SuspendLayout();
            //
            // lblInfo
            //
            lblInfo.Location = new Point(12, 9);
            lblInfo.Name = "lblInfo";
            lblInfo.Size = new Size(316, 48);
            lblInfo.TabIndex = 0;
            lblInfo.Text = "Your QRZ.com login. Lat/long come back only with a QRZ XML Logbook Data subscription; without one, the grid square is used instead.";
            //
            // lblUsername
            //
            lblUsername.AutoSize = true;
            lblUsername.Location = new Point(12, 66);
            lblUsername.Name = "lblUsername";
            lblUsername.Size = new Size(63, 15);
            lblUsername.TabIndex = 1;
            lblUsername.Text = "&Username:";
            //
            // txtUsername
            //
            txtUsername.CharacterCasing = CharacterCasing.Upper;
            txtUsername.Location = new Point(88, 63);
            txtUsername.Name = "txtUsername";
            txtUsername.Size = new Size(240, 23);
            txtUsername.TabIndex = 2;
            //
            // lblPassword
            //
            lblPassword.AutoSize = true;
            lblPassword.Location = new Point(12, 95);
            lblPassword.Name = "lblPassword";
            lblPassword.Size = new Size(60, 15);
            lblPassword.TabIndex = 3;
            lblPassword.Text = "&Password:";
            //
            // txtPassword
            //
            txtPassword.Location = new Point(88, 92);
            txtPassword.Name = "txtPassword";
            txtPassword.Size = new Size(240, 23);
            txtPassword.TabIndex = 4;
            txtPassword.UseSystemPasswordChar = true;
            //
            // btnOk
            //
            btnOk.Location = new Point(172, 128);
            btnOk.Name = "btnOk";
            btnOk.Size = new Size(75, 27);
            btnOk.TabIndex = 5;
            btnOk.Text = "OK";
            btnOk.UseVisualStyleBackColor = true;
            btnOk.Click += BtnOk_Click;
            //
            // btnCancel
            //
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(253, 128);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(75, 27);
            btnCancel.TabIndex = 6;
            btnCancel.Text = "Cancel";
            btnCancel.UseVisualStyleBackColor = true;
            //
            // QrzLoginForm
            //
            AcceptButton = btnOk;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = btnCancel;
            ClientSize = new Size(340, 167);
            Controls.Add(lblInfo);
            Controls.Add(lblUsername);
            Controls.Add(txtUsername);
            Controls.Add(lblPassword);
            Controls.Add(txtPassword);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "QrzLoginForm";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "QRZ Login";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label lblInfo;
        private Label lblUsername;
        private TextBox txtUsername;
        private Label lblPassword;
        private TextBox txtPassword;
        private Button btnOk;
        private Button btnCancel;
    }
}
