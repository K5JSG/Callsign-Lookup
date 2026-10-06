namespace CallsignLookup
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            lblIotaUpdate = new Label();
            topPanel = new TableLayoutPanel();
            lblCallsign = new Label();
            txtCallsign = new TextBox();
            btnLookup = new Button();
            btnQrzLogin = new Button();
            resultsPanel = new TableLayoutPanel();
            lblGridSquare = new Label();
            txtGridSquare = new TextBox();
            lblCounty = new Label();
            txtCounty = new TextBox();
            lblState = new Label();
            txtState = new TextBox();
            lblCqZone = new Label();
            txtCqZone = new TextBox();
            lblItuZone = new Label();
            txtItuZone = new TextBox();
            lblArrlSection = new Label();
            txtArrlSection = new TextBox();
            lblIota = new Label();
            txtIota = new TextBox();
            lblIsland = new Label();
            txtIsland = new TextBox();
            pictureBoxLogo = new PictureBox();
            hrdPanel = new FlowLayoutPanel();
            lblHrd = new Label();
            lblHrdQso = new Label();
            lblWorkingFrom = new Label();
            cbxStationProfile = new ComboBox();
            btnFillHrd = new Button();
            bottomPanel = new FlowLayoutPanel();
            btnCopy = new Button();
            btnClear = new Button();
            chkStayOnTop = new CheckBox();
            statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel();
            topPanel.SuspendLayout();
            resultsPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBoxLogo).BeginInit();
            hrdPanel.SuspendLayout();
            bottomPanel.SuspendLayout();
            statusStrip.SuspendLayout();
            SuspendLayout();
            //
            // topPanel
            //
            topPanel.AutoSize = true;
            topPanel.ColumnCount = 4;
            topPanel.ColumnStyles.Add(new ColumnStyle());
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            topPanel.ColumnStyles.Add(new ColumnStyle());
            topPanel.ColumnStyles.Add(new ColumnStyle());
            topPanel.Controls.Add(lblCallsign, 0, 0);
            topPanel.Controls.Add(txtCallsign, 1, 0);
            topPanel.Controls.Add(btnLookup, 2, 0);
            topPanel.Controls.Add(btnQrzLogin, 3, 0);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Padding = new Padding(8);
            topPanel.RowCount = 1;
            topPanel.RowStyles.Add(new RowStyle());
            topPanel.Size = new Size(740, 51);
            topPanel.TabIndex = 0;
            //
            // lblCallsign
            //
            lblCallsign.Anchor = AnchorStyles.Left;
            lblCallsign.AutoSize = true;
            lblCallsign.Location = new Point(11, 17);
            lblCallsign.Name = "lblCallsign";
            lblCallsign.Size = new Size(52, 15);
            lblCallsign.TabIndex = 0;
            lblCallsign.Text = "Callsign:";
            //
            // txtCallsign
            //
            txtCallsign.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtCallsign.CharacterCasing = CharacterCasing.Upper;
            txtCallsign.Font = new Font("Segoe UI", 11F);
            txtCallsign.Location = new Point(69, 12);
            txtCallsign.MaxLength = 20;
            txtCallsign.Name = "txtCallsign";
            txtCallsign.Size = new Size(268, 27);
            txtCallsign.TabIndex = 1;
            //
            // btnLookup
            //
            btnLookup.Anchor = AnchorStyles.Left;
            btnLookup.AutoSize = true;
            btnLookup.Location = new Point(343, 12);
            btnLookup.Name = "btnLookup";
            btnLookup.Size = new Size(90, 27);
            btnLookup.TabIndex = 2;
            btnLookup.Text = "&Look Up";
            btnLookup.UseVisualStyleBackColor = true;
            btnLookup.Click += BtnLookup_Click;
            //
            // btnQrzLogin
            //
            btnQrzLogin.Anchor = AnchorStyles.Left;
            btnQrzLogin.AutoSize = true;
            btnQrzLogin.Location = new Point(439, 12);
            btnQrzLogin.Name = "btnQrzLogin";
            btnQrzLogin.Size = new Size(110, 27);
            btnQrzLogin.TabIndex = 3;
            btnQrzLogin.Text = "&QRZ Login...";
            btnQrzLogin.UseVisualStyleBackColor = true;
            btnQrzLogin.Click += BtnQrzLogin_Click;
            //
            // lblIotaUpdate
            //
            // Shown by MainForm when IOTA has added islands this version's
            // island data doesn't have.
            lblIotaUpdate.AutoSize = true;
            lblIotaUpdate.BackColor = Color.FromArgb(255, 243, 205);
            lblIotaUpdate.Dock = DockStyle.Top;
            lblIotaUpdate.ForeColor = Color.FromArgb(102, 77, 3);
            lblIotaUpdate.MaximumSize = new Size(740, 0);
            lblIotaUpdate.Name = "lblIotaUpdate";
            lblIotaUpdate.Padding = new Padding(8, 6, 8, 6);
            lblIotaUpdate.TabIndex = 4;
            lblIotaUpdate.Visible = false;
            //
            // resultsPanel
            //
            resultsPanel.ColumnCount = 3;
            resultsPanel.ColumnStyles.Add(new ColumnStyle());
            resultsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            resultsPanel.ColumnStyles.Add(new ColumnStyle());
            resultsPanel.Controls.Add(lblGridSquare, 0, 0);
            resultsPanel.Controls.Add(txtGridSquare, 1, 0);
            resultsPanel.Controls.Add(lblCqZone, 0, 1);
            resultsPanel.Controls.Add(txtCqZone, 1, 1);
            resultsPanel.Controls.Add(lblItuZone, 0, 2);
            resultsPanel.Controls.Add(txtItuZone, 1, 2);
            resultsPanel.Controls.Add(lblCounty, 0, 3);
            resultsPanel.Controls.Add(txtCounty, 1, 3);
            resultsPanel.Controls.Add(lblState, 0, 4);
            resultsPanel.Controls.Add(txtState, 1, 4);
            resultsPanel.Controls.Add(lblArrlSection, 0, 5);
            resultsPanel.Controls.Add(txtArrlSection, 1, 5);
            resultsPanel.Controls.Add(lblIota, 0, 6);
            resultsPanel.Controls.Add(txtIota, 1, 6);
            resultsPanel.Controls.Add(lblIsland, 0, 7);
            resultsPanel.Controls.Add(txtIsland, 1, 7);
            resultsPanel.Controls.Add(pictureBoxLogo, 2, 0);
            resultsPanel.SetRowSpan(pictureBoxLogo, 8);
            resultsPanel.Dock = DockStyle.Fill;
            resultsPanel.Location = new Point(0, 51);
            resultsPanel.Name = "resultsPanel";
            resultsPanel.Padding = new Padding(8, 4, 8, 4);
            resultsPanel.RowCount = 9;
            resultsPanel.RowStyles.Add(new RowStyle());
            resultsPanel.RowStyles.Add(new RowStyle());
            resultsPanel.RowStyles.Add(new RowStyle());
            resultsPanel.RowStyles.Add(new RowStyle());
            resultsPanel.RowStyles.Add(new RowStyle());
            resultsPanel.RowStyles.Add(new RowStyle());
            resultsPanel.RowStyles.Add(new RowStyle());
            resultsPanel.RowStyles.Add(new RowStyle());
            resultsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            resultsPanel.Size = new Size(740, 306);
            resultsPanel.TabIndex = 1;
            //
            // lblGridSquare
            //
            lblGridSquare.Anchor = AnchorStyles.Left;
            lblGridSquare.AutoSize = true;
            lblGridSquare.Name = "lblGridSquare";
            lblGridSquare.TabIndex = 0;
            lblGridSquare.Text = "Grid Square:";
            //
            // txtGridSquare
            //
            txtGridSquare.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtGridSquare.Font = new Font("Segoe UI", 11F);
            txtGridSquare.MaxLength = 10;
            txtGridSquare.Name = "txtGridSquare";
            txtGridSquare.TabIndex = 1;
            txtGridSquare.TextChanged += TxtGridSquare_TextChanged;
            txtGridSquare.KeyDown += TxtGridSquare_KeyDown;
            txtGridSquare.Enter += TxtGridSquare_Enter;
            txtGridSquare.Leave += TxtGridSquare_Leave;
            //
            // lblCounty
            //
            lblCounty.Anchor = AnchorStyles.Left;
            lblCounty.AutoSize = true;
            lblCounty.Name = "lblCounty";
            lblCounty.TabIndex = 6;
            lblCounty.Text = "County:";
            //
            // txtCounty
            //
            txtCounty.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtCounty.Font = new Font("Segoe UI", 11F);
            txtCounty.Name = "txtCounty";
            txtCounty.ReadOnly = true;
            txtCounty.TabIndex = 7;
            //
            // lblState
            //
            lblState.Anchor = AnchorStyles.Left;
            lblState.AutoSize = true;
            lblState.Name = "lblState";
            lblState.TabIndex = 8;
            lblState.Text = "State:";
            //
            // txtState
            //
            txtState.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtState.Font = new Font("Segoe UI", 11F);
            txtState.Name = "txtState";
            txtState.ReadOnly = true;
            txtState.TabIndex = 9;
            //
            // lblCqZone
            //
            lblCqZone.Anchor = AnchorStyles.Left;
            lblCqZone.AutoSize = true;
            lblCqZone.Name = "lblCqZone";
            lblCqZone.TabIndex = 2;
            lblCqZone.Text = "CQ Zone:";
            //
            // txtCqZone
            //
            txtCqZone.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtCqZone.Font = new Font("Segoe UI", 11F);
            txtCqZone.Name = "txtCqZone";
            txtCqZone.ReadOnly = true;
            txtCqZone.TabIndex = 3;
            //
            // lblItuZone
            //
            lblItuZone.Anchor = AnchorStyles.Left;
            lblItuZone.AutoSize = true;
            lblItuZone.Name = "lblItuZone";
            lblItuZone.TabIndex = 4;
            lblItuZone.Text = "ITU Zone:";
            //
            // txtItuZone
            //
            txtItuZone.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtItuZone.Font = new Font("Segoe UI", 11F);
            txtItuZone.Name = "txtItuZone";
            txtItuZone.ReadOnly = true;
            txtItuZone.TabIndex = 5;
            //
            // lblArrlSection
            //
            lblArrlSection.Anchor = AnchorStyles.Left;
            lblArrlSection.AutoSize = true;
            lblArrlSection.Name = "lblArrlSection";
            lblArrlSection.TabIndex = 10;
            lblArrlSection.Text = "ARRL Section:";
            //
            // txtArrlSection
            //
            txtArrlSection.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtArrlSection.Font = new Font("Segoe UI", 11F);
            txtArrlSection.Name = "txtArrlSection";
            txtArrlSection.ReadOnly = true;
            txtArrlSection.TabIndex = 11;
            //
            // lblIota
            //
            lblIota.Anchor = AnchorStyles.Left;
            lblIota.AutoSize = true;
            lblIota.Name = "lblIota";
            lblIota.TabIndex = 13;
            lblIota.Text = "IOTA:";
            //
            // txtIota
            //
            txtIota.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtIota.Font = new Font("Segoe UI", 11F);
            txtIota.Name = "txtIota";
            txtIota.ReadOnly = true;
            txtIota.TabIndex = 14;
            //
            // lblIsland
            //
            lblIsland.Anchor = AnchorStyles.Left;
            lblIsland.AutoSize = true;
            lblIsland.Name = "lblIsland";
            lblIsland.TabIndex = 15;
            lblIsland.Text = "Island:";
            //
            // txtIsland
            //
            txtIsland.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            txtIsland.Font = new Font("Segoe UI", 11F);
            txtIsland.Name = "txtIsland";
            txtIsland.ReadOnly = true;
            txtIsland.TabIndex = 16;
            //
            // pictureBoxLogo
            //
            // Image is set in MainForm's constructor from AppLogo (built into the exe).
            pictureBoxLogo.Anchor = AnchorStyles.Top;
            pictureBoxLogo.BackColor = Color.Transparent;
            pictureBoxLogo.Margin = new Padding(16, 3, 3, 3);
            pictureBoxLogo.Name = "pictureBoxLogo";
            pictureBoxLogo.Size = new Size(180, 180);
            pictureBoxLogo.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBoxLogo.TabIndex = 12;
            pictureBoxLogo.TabStop = false;
            //
            // hrdPanel
            //
            hrdPanel.AutoSize = true;
            hrdPanel.Controls.Add(lblHrd);
            hrdPanel.Controls.Add(lblHrdQso);
            hrdPanel.Controls.Add(lblWorkingFrom);
            hrdPanel.Controls.Add(cbxStationProfile);
            hrdPanel.Controls.Add(btnFillHrd);
            hrdPanel.Dock = DockStyle.Bottom;
            hrdPanel.Location = new Point(0, 316);
            hrdPanel.Name = "hrdPanel";
            hrdPanel.Padding = new Padding(8, 4, 8, 0);
            hrdPanel.Size = new Size(740, 41);
            hrdPanel.TabIndex = 5;
            hrdPanel.WrapContents = false;
            //
            // lblHrd
            //
            lblHrd.Anchor = AnchorStyles.Left;
            lblHrd.AutoSize = true;
            lblHrd.Font = new Font(Font, FontStyle.Bold);
            lblHrd.Location = new Point(11, 14);
            lblHrd.Name = "lblHrd";
            lblHrd.Size = new Size(83, 15);
            lblHrd.TabIndex = 0;
            lblHrd.Text = "HRD Logbook:";
            //
            // lblHrdQso
            //
            lblHrdQso.Anchor = AnchorStyles.Left;
            lblHrdQso.AutoSize = true;
            lblHrdQso.Location = new Point(100, 14);
            lblHrdQso.MinimumSize = new Size(190, 0);
            lblHrdQso.Name = "lblHrdQso";
            lblHrdQso.Size = new Size(190, 15);
            lblHrdQso.TabIndex = 1;
            lblHrdQso.Text = "no QSO open";
            //
            // lblWorkingFrom
            //
            lblWorkingFrom.Anchor = AnchorStyles.Left;
            lblWorkingFrom.AutoSize = true;
            lblWorkingFrom.Location = new Point(296, 14);
            lblWorkingFrom.Name = "lblWorkingFrom";
            lblWorkingFrom.Size = new Size(85, 15);
            lblWorkingFrom.TabIndex = 2;
            lblWorkingFrom.Text = "Working from:";
            //
            // cbxStationProfile
            //
            cbxStationProfile.Anchor = AnchorStyles.Left;
            cbxStationProfile.DropDownStyle = ComboBoxStyle.DropDownList;
            cbxStationProfile.Location = new Point(387, 10);
            cbxStationProfile.Name = "cbxStationProfile";
            cbxStationProfile.Size = new Size(200, 23);
            cbxStationProfile.TabIndex = 3;
            cbxStationProfile.SelectedIndexChanged += CbxStationProfile_SelectedIndexChanged;
            //
            // btnFillHrd
            //
            btnFillHrd.Anchor = AnchorStyles.Left;
            btnFillHrd.AutoSize = true;
            btnFillHrd.Enabled = false;
            btnFillHrd.Location = new Point(593, 7);
            btnFillHrd.Name = "btnFillHrd";
            btnFillHrd.Size = new Size(110, 27);
            btnFillHrd.TabIndex = 4;
            btnFillHrd.Text = "&Fill HRD QSO";
            btnFillHrd.UseVisualStyleBackColor = true;
            btnFillHrd.Click += BtnFillHrd_Click;
            //
            // bottomPanel
            //
            bottomPanel.AutoSize = true;
            bottomPanel.Controls.Add(btnCopy);
            bottomPanel.Controls.Add(btnClear);
            bottomPanel.Controls.Add(chkStayOnTop);
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.FlowDirection = FlowDirection.RightToLeft;
            bottomPanel.Location = new Point(0, 357);
            bottomPanel.Name = "bottomPanel";
            bottomPanel.Padding = new Padding(8, 4, 8, 4);
            bottomPanel.Size = new Size(740, 41);
            bottomPanel.TabIndex = 2;
            //
            // btnCopy
            //
            btnCopy.AutoSize = true;
            btnCopy.Enabled = false;
            btnCopy.Location = new Point(439, 7);
            btnCopy.Name = "btnCopy";
            btnCopy.Size = new Size(110, 27);
            btnCopy.TabIndex = 0;
            btnCopy.Text = "&Copy All";
            btnCopy.UseVisualStyleBackColor = true;
            btnCopy.Click += BtnCopy_Click;
            //
            // btnClear
            //
            btnClear.AutoSize = true;
            btnClear.Location = new Point(323, 7);
            btnClear.Name = "btnClear";
            btnClear.Size = new Size(110, 27);
            btnClear.TabIndex = 1;
            btnClear.Text = "C&lear";
            btnClear.UseVisualStyleBackColor = true;
            btnClear.Click += BtnClear_Click;
            //
            // chkStayOnTop
            //
            chkStayOnTop.Anchor = AnchorStyles.Left;
            chkStayOnTop.AutoSize = true;
            chkStayOnTop.Margin = new Padding(3, 3, 12, 3);
            chkStayOnTop.Name = "chkStayOnTop";
            chkStayOnTop.TabIndex = 2;
            chkStayOnTop.Text = "Stay on &top";
            chkStayOnTop.UseVisualStyleBackColor = true;
            chkStayOnTop.CheckedChanged += ChkStayOnTop_CheckedChanged;
            //
            // statusStrip
            //
            statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus });
            statusStrip.Location = new Point(0, 398);
            statusStrip.Name = "statusStrip";
            statusStrip.Size = new Size(740, 22);
            statusStrip.TabIndex = 3;
            //
            // lblStatus
            //
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(545, 17);
            lblStatus.Spring = true;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            //
            // MainForm
            //
            AcceptButton = btnLookup;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(740, 429);
            Controls.Add(resultsPanel);
            Controls.Add(hrdPanel);
            Controls.Add(bottomPanel);
            Controls.Add(topPanel);
            Controls.Add(lblIotaUpdate);
            Controls.Add(statusStrip);
            MinimumSize = new Size(740, 468);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Callsign Lookup";
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBoxLogo).EndInit();
            resultsPanel.ResumeLayout(false);
            resultsPanel.PerformLayout();
            hrdPanel.ResumeLayout(false);
            hrdPanel.PerformLayout();
            bottomPanel.ResumeLayout(false);
            bottomPanel.PerformLayout();
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label lblIotaUpdate;
        private TableLayoutPanel topPanel;
        private Label lblCallsign;
        private TextBox txtCallsign;
        private Button btnLookup;
        private Button btnQrzLogin;
        private TableLayoutPanel resultsPanel;
        private Label lblGridSquare;
        private TextBox txtGridSquare;
        private Label lblCounty;
        private TextBox txtCounty;
        private Label lblState;
        private TextBox txtState;
        private Label lblCqZone;
        private TextBox txtCqZone;
        private Label lblItuZone;
        private TextBox txtItuZone;
        private Label lblArrlSection;
        private TextBox txtArrlSection;
        private Label lblIota;
        private TextBox txtIota;
        private Label lblIsland;
        private TextBox txtIsland;
        private PictureBox pictureBoxLogo;
        private FlowLayoutPanel hrdPanel;
        private Label lblHrd;
        private Label lblHrdQso;
        private Label lblWorkingFrom;
        private ComboBox cbxStationProfile;
        private Button btnFillHrd;
        private FlowLayoutPanel bottomPanel;
        private Button btnCopy;
        private Button btnClear;
        private CheckBox chkStayOnTop;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;
    }
}
