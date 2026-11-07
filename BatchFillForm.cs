using CallsignLookup.Services;
using CallsignLookup.Services.Hrd;
using CallsignLookup.Services.Upload;

namespace CallsignLookup
{
    // The batch fill: looks up the QSOs picked in HRD's log, shows what would
    // change in each (tick the ones to write, correct any value), then writes
    // the ticked ones into HRD's log, sends them to LoTW and QRZ if wanted, and
    // shows a summary. Nothing is written or sent until Apply.
    // With a snapshot it's the single fill (Fill HRD QSO): one QSO HRD has
    // just looked up and saved, no list, and what HRD's Lookup wiped of the
    // snapshot's POTA, IOTA and SK note goes back.
    public sealed class BatchFillForm : Form
    {
        private enum Stage { Preparing, Ready, Applying, Done }

        private readonly HrdLogDatabase _log;
        private readonly HrdBatchFiller _filler;
        private readonly IReadOnlyList<long> _pks;
        private HrdStationProfile? _profile;
        private readonly bool _clearQslVia;
        private readonly CancellationTokenSource _cancel = new();

        private readonly Label _intro = new();
        private readonly ProgressBar _progress = new();
        private readonly ListView _qsos = new();
        private readonly QsoEditGrid _edit;
        private readonly Button _resetEdits = new();
        private readonly IReadOnlyDictionary<string, object?>? _snapshot;
        private bool Single => _snapshot != null;

        // The send box.
        private readonly Panel _sendPanel = new();
        private readonly ComboBox _stationBox = new();
        private bool _replanning;
        private readonly CheckBox _sendLotw = new();
        private readonly CheckBox _sendQrz = new();
        private readonly Label _lotwNote = new();
        private readonly Label _qrzNote = new();
        private readonly FlowLayoutPanel _lotwLocations = new();
        private readonly Dictionary<string, ComboBox> _locationBoxes = [];
        private List<TqslLocation> _tqslLocations = [];
        private string? _tqslPath;
        private bool _sendDefaultsSet;
        private readonly TextBox _notes = new();
        private readonly TextBox _summary = new();
        private readonly Button _apply = new();
        private readonly Button _close = new();
        private readonly Button _tickAll = new();
        private readonly Button _untickAll = new();
        private readonly Button _copy = new();
        private readonly Button _openBackup = new();
        private readonly Button _undo = new();
        private readonly Button _allAddress = new();
        private readonly Button _allQrz = new();
        private readonly Panel _choicePanel = new();
        private readonly CheckBox _choiceQrz = new();
        private readonly CheckBox _choiceAddress = new();
        private readonly CheckBox _stationHrd = new();
        private readonly CheckBox _stationSelected = new();
        private readonly Button _allSelectedStation = new();
        private readonly Button _allHrdStation = new();
        private bool _settingChoice;

        private List<HrdBatchItem> _items = [];
        private HrdBatchResult? _result;
        private Stage _stage = Stage.Preparing;
        private bool _populating;
        private bool _undone;

        public BatchFillForm(HrdLogDatabase log, CallsignLookupService lookup, IGeocoder geocoder, IReadOnlyList<long> pks,
            HrdStationProfile? profile, bool clearQslVia, IReadOnlyDictionary<string, object?>? snapshot = null)
        {
            _log = log;
            _snapshot = snapshot;
            var continents = new Dictionary<int, string?>();
            _edit = new QsoEditGrid(dxcc =>
            {
                if (!continents.TryGetValue(dxcc, out string? continent)) continents[dxcc] = continent = log.ContinentFor(dxcc);
                return continent;
            }) { Dock = DockStyle.Fill };
            _edit.Changed += (_, _) =>
            {
                if (SelectedRow is { Tag: HrdBatchItem item } row) RefreshRow(row, item);
                UpdateApplyButton();
            };
            _filler = new HrdBatchFiller(log, lookup, geocoder);
            _pks = pks;
            _profile = profile;
            _clearQslVia = clearQslVia;

            Text = Single ? "Fill HRD QSO" : "Fill selected QSOs in HRD";
            Icon = AppLogo.Icon ?? Icon;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 600);
            MinimumSize = new Size(640, 400);
            // In the taskbar so it can be found again if minimized: without a
            // taskbar button a minimized dialog shrinks to a small title bar in
            // the screen's bottom-left corner, which looks like it closed.
            ShowInTaskbar = true;

            _intro.Dock = DockStyle.Top;
            _intro.AutoSize = true;
            _intro.Padding = new Padding(10, 10, 10, 6);
            _intro.MaximumSize = new Size(880, 0);

            _progress.Dock = DockStyle.Top;
            _progress.Height = 8;
            _progress.Maximum = Math.Max(1, pks.Count);

            ConfigureList(_qsos, checkBoxes: true);
            _qsos.Columns.Add("QSO", 110);
            _qsos.Columns.Add("Date / time (UTC)", 130);
            _qsos.Columns.Add("Changes", 70);
            _qsos.Columns.Add("Location", 70);
            _qsos.Columns.Add("Station", 70);
            _qsos.Columns.Add("Notes", 420);
            _qsos.ItemCheck += Qsos_ItemCheck;
            _qsos.ItemChecked += (_, _) => UpdateApplyButton();
            _qsos.SelectedIndexChanged += (_, _) => ShowDetails();

            _resetEdits.AutoSize = true;
            _resetEdits.Text = "&Reset my changes";
            _resetEdits.Click += (_, _) =>
            {
                if (SelectedRow?.Tag is HrdBatchItem item) _edit.Reset(item);
            };
            new ToolTip().SetToolTip(_resetEdits, "Puts this QSO's fields back to what the fill worked out (your typed values and unticks go).");

            BuildSendBox();

            _notes.Dock = DockStyle.Bottom;
            _notes.Multiline = true;
            _notes.ReadOnly = true;
            _notes.Height = 74;
            _notes.ScrollBars = ScrollBars.Vertical;

            _summary.Dock = DockStyle.Fill;
            _summary.Multiline = true;
            _summary.ReadOnly = true;
            _summary.ScrollBars = ScrollBars.Both;
            _summary.WordWrap = false;
            _summary.Font = new Font(FontFamily.GenericMonospace, 9f);
            _summary.Visible = false;

            var lower = new Panel { Dock = DockStyle.Fill };
            lower.Controls.Add(_summary);
            lower.Controls.Add(_edit);
            lower.Controls.Add(_notes);
            lower.Controls.Add(_choicePanel);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 250 };
            split.Panel1.Controls.Add(_qsos);
            split.Panel2.Controls.Add(lower);
            if (Single) split.Panel1Collapsed = true;

            _apply.AutoSize = true;
            _apply.Enabled = false;
            _apply.Click += async (_, _) => await ApplyAsync();
            _close.AutoSize = true;
            _close.Text = "Cancel";
            _close.Click += (_, _) => Close();
            _tickAll.AutoSize = true;
            _tickAll.Text = "Tick all";
            _tickAll.Click += (_, _) => SetAllTicks(true);
            _untickAll.AutoSize = true;
            _untickAll.Text = "Untick all";
            _untickAll.Click += (_, _) => SetAllTicks(false);
            _copy.AutoSize = true;
            _copy.Text = "Copy summary";
            _copy.Visible = false;
            _copy.Click += (_, _) => Clipboard.SetText(_summary.Text);
            _openBackup.AutoSize = true;
            _openBackup.Text = "Show backup";
            _openBackup.Visible = false;
            _openBackup.Click += (_, _) =>
            {
                if (_result is { BackupPath.Length: > 0 })
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_result.BackupPath}\"");
            };

            _undo.AutoSize = true;
            _undo.Text = "&Undo this batch";
            _undo.Visible = false;
            _undo.Click += async (_, _) => await UndoAsync();
            new ToolTip().SetToolTip(_undo, "Puts the QSOs back to what they were before this batch (a backup is made first).");

            // For every QSO whose QRZ location disagrees with its state: the same choice for all.
            _allAddress.AutoSize = true;
            _allAddress.Text = "Address location for &all";
            _allAddress.Visible = false;
            _allAddress.Click += (_, _) => SetAllLocations(useAddress: true);
            _allQrz.AutoSize = true;
            _allQrz.Text = "QRZ location for a&ll";
            _allQrz.Visible = false;
            _allQrz.Click += (_, _) => SetAllLocations(useAddress: false);
            new ToolTip().SetToolTip(_allAddress, "For every QSO where QRZ's location is in another state than its record lists, use the location the address gives.");
            new ToolTip().SetToolTip(_allQrz, "For every QSO where QRZ's location is in another state than its record lists, use QRZ's location.");

            // For every QSO whose My Station is a different station than the selected one.
            _allSelectedStation.AutoSize = true;
            _allSelectedStation.Text = "Selected station for al&l";
            _allSelectedStation.Visible = false;
            _allSelectedStation.Click += (_, _) => SetAllStations(useSelected: true);
            _allHrdStation.AutoSize = true;
            _allHrdStation.Text = "HRD's station for all";
            _allHrdStation.Visible = false;
            _allHrdStation.Click += (_, _) => SetAllStations(useSelected: false);
            new ToolTip().SetToolTip(_allSelectedStation, "For every QSO whose My Station is a different station than the one selected under Working from, replace it with the selected station.");
            new ToolTip().SetToolTip(_allHrdStation, "For every QSO whose My Station is a different station than the one selected under Working from, keep what is in HRD.");

            // Two ticks for each choice (location, station), one of each pair always on.
            _choicePanel.Dock = DockStyle.Bottom;
            _choicePanel.Height = 52;
            _choicePanel.Visible = false;
            foreach (var box in new[] { _choiceQrz, _choiceAddress, _stationHrd, _stationSelected })
            {
                box.AutoSize = true;
                box.Left = 8;
                _choicePanel.Controls.Add(box);
            }
            _choiceQrz.CheckedChanged += (_, _) => ChoiceChanged(useAddress: false);
            _choiceAddress.CheckedChanged += (_, _) => ChoiceChanged(useAddress: true);
            _stationHrd.CheckedChanged += (_, _) => StationChoiceChanged(useSelected: false);
            _stationSelected.CheckedChanged += (_, _) => StationChoiceChanged(useSelected: true);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 4, 8, 4),
            };
            buttons.Controls.Add(_close);
            buttons.Controls.Add(_apply);
            buttons.Controls.Add(_undo);
            buttons.Controls.Add(_openBackup);
            buttons.Controls.Add(_copy);
            buttons.Controls.Add(_allHrdStation);
            buttons.Controls.Add(_allSelectedStation);
            buttons.Controls.Add(_allQrz);
            buttons.Controls.Add(_allAddress);
            buttons.Controls.Add(_untickAll);
            buttons.Controls.Add(_tickAll);
            buttons.Controls.Add(_resetEdits);
            CancelButton = _close;
            if (Single) _tickAll.Visible = _untickAll.Visible = false;

            Controls.Add(split);
            Controls.Add(_sendPanel);
            Controls.Add(buttons);
            Controls.Add(_progress);
            Controls.Add(_intro);
            Resize += (_, _) => _intro.MaximumSize = new Size(ClientSize.Width, 0);

            SetStage(Stage.Preparing);
            Shown += async (_, _) => await PrepareAsync();
            FormClosing += (_, e) =>
            {
                // Closing while it's working stops the lookups; while it's writing
                // the transaction has to finish, so wait.
                if (_stage == Stage.Applying) e.Cancel = true;
                else _cancel.Cancel();
            };
        }

        private static void ConfigureList(ListView list, bool checkBoxes)
        {
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.GridLines = true;
            list.HideSelection = false;
            list.CheckBoxes = checkBoxes;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        }

        // ---- stages -------------------------------------------------------------

        private void SetStage(Stage stage)
        {
            _stage = stage;
            switch (stage)
            {
                case Stage.Preparing:
                    _intro.Text = $"Looking up {_pks.Count} QSO{(_pks.Count == 1 ? "" : "s")} on QRZ and working out what each needs. " +
                                  "Nothing is written to HRD's log yet.";
                    _sendPanel.Visible = _resetEdits.Visible = false;
                    _progress.Visible = true;
                    _close.Text = "Cancel";
                    break;
                case Stage.Ready:
                    _intro.Text = Single
                        ? "HRD has looked this QSO up and saved it. Below is what this app would change on top. Click a value under " +
                          "\"Will become\" to correct it, untick a row to leave that field as it is. A backup of the log is made " +
                          "first. Nothing is written or sent until you click Approve."
                        : "Tick the QSOs to write into HRD's log, click one to see exactly what would change (click a value under " +
                          "\"Will become\" to correct it). A backup of the log is made first. Nothing is written until you click " +
                          "Apply. Ones with a QRZ record for another callsign start unticked - check them first.";
                    _progress.Visible = false;
                    _sendPanel.Visible = _resetEdits.Visible = true;
                    _close.Text = "Cancel";
                    break;
                case Stage.Applying:
                    _allAddress.Visible = _allQrz.Visible = _allSelectedStation.Visible = _allHrdStation.Visible = false;
                    _choicePanel.Enabled = false;
                    _intro.Text = "Writing to HRD's log...";
                    _apply.Enabled = false;
                    _close.Enabled = false;
                    _sendPanel.Enabled = _resetEdits.Enabled = false;
                    _edit.Show(SelectedRow?.Tag as HrdBatchItem, editable: false);
                    break;
                case Stage.Done:
                    _allAddress.Visible = _allQrz.Visible = _allSelectedStation.Visible = _allHrdStation.Visible = false;
                    _choicePanel.Visible = false;
                    _progress.Visible = false;
                    _close.Enabled = true;
                    _close.Text = "Close";
                    _apply.Visible = _tickAll.Visible = _untickAll.Visible = _resetEdits.Visible = _sendPanel.Visible = false;
                    _copy.Visible = true;
                    _openBackup.Visible = _result is { BackupPath.Length: > 0 };
                    _undo.Visible = _result is { Applied.Count: > 0 } && !_undone;
                    break;
            }
        }

        private async Task PrepareAsync()
        {
            try
            {
                _tqslPath = HrdUploadSettings.TqslPath();
                _tqslLocations = await Task.Run(() => TqslLocations.Load());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            await PlanAsync(stationPicked: false);
        }

        // Works out every QSO's changes for the current My Station (again, when
        // it's changed in the drop-down: the user's edits then go).
        private async Task PlanAsync(bool stationPicked)
        {
            var progress = new Progress<(int Done, int Total, string Call)>(p =>
            {
                _progress.Value = Math.Min(p.Done, _progress.Maximum);
                if (p.Call.Length > 0) _intro.Text = $"Looking up {p.Call} ({p.Done + 1} of {p.Total})... Nothing is written to HRD's log yet.";
            });
            if (stationPicked) SetStage(Stage.Preparing);
            try
            {
                _items = await _filler.PrepareAsync(_pks, _profile, _clearQslVia, progress, _cancel.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is QrzException or HttpRequestException)
            {
                MessageBox.Show(this, ex.Message, "Fill selected QSOs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }

            // The single fill: what HRD's own Lookup wiped goes back.
            if (_snapshot != null)
                foreach (var item in _items.Where(i => i.Row != null))
                    item.Restores.AddRange(HrdQsoEdits.Restores(_snapshot, item.Row!));

            // Picked in the drop-down: that station is wanted, even on a QSO that
            // has another one.
            if (stationPicked)
                foreach (var item in _items.Where(i => i.HasStationChoice)) item.UseSelectedStation = true;

            _populating = true;
            _qsos.BeginUpdate();
            _qsos.Items.Clear();
            foreach (var item in _items)
            {
                var row = new ListViewItem([
                    item.Call.Length > 0 ? item.Call : $"#{item.Pk}", item.When, RowCount(item), RowLocation(item), RowStation(item), RowNotes(item)])
                {
                    Tag = item,
                    Checked = item.TickedAtStart || (Single && item.Applicable),
                    ForeColor = item.Applicable ? _qsos.ForeColor : SystemColors.GrayText,
                };
                _qsos.Items.Add(row);
            }
            _qsos.EndUpdate();
            _populating = false;

            SetStage(Stage.Ready);
            _allAddress.Visible = _allQrz.Visible = _items.Any(i => i.HasLocationChoice);
            _allSelectedStation.Visible = _allHrdStation.Visible = _items.Any(i => i.HasStationChoice);
            UpdateApplyButton();
            if (_qsos.Items.Count > 0) _qsos.Items[0].Selected = true;
            if (Single) ShowDetails();
        }

        private async Task ApplyAsync()
        {
            var chosen = Chosen();
            if (chosen.Count == 0) return;
            var send = SendPlan(chosen);

            // An open Add/Edit window saves its whole QSO as it loaded it, undoing a
            // change made here - and the user may have opened one since starting.
            var open = HrdEditWindow.FindAll();
            if (open.Count > 0)
            {
                MessageBox.Show(this,
                    $"HRD has {open.Count} Add/Edit window{(open.Count == 1 ? "" : "s")} open. Close {(open.Count == 1 ? "it" : "them")} " +
                    "(without saving), then click Apply again - a QSO saved from an open window would undo the changes.",
                    "Fill selected QSOs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetStage(Stage.Applying);
            HrdBatchResult result;
            try
            {
                result = await Task.Run(() => _filler.Apply(chosen, BackupPurpose));
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or HrdException or IOException)
            {
                SetStage(Stage.Ready);
                UpdateApplyButton();
                MessageBox.Show(this, $"Nothing was written to HRD's log (it's all or nothing).\n\n{ex.Message}",
                    "Fill selected QSOs", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Send what's now in the log: QSOs written, or with nothing to write. Not
            // ones that changed in HRD meanwhile or didn't read back.
            var skip = result.NotApplied.Select(n => n.Item.Pk).Concat(result.Failed.Select(f => f.Item.Pk)).ToHashSet();
            var request = new UploadRequest(
                send.Lotw.Where(l => !skip.Contains(l.Key)).ToDictionary(),
                send.Qrz.Where(pk => !skip.Contains(pk)).ToList());
            string uploads = "";
            if (request.LotwLocations.Count + request.QrzPks.Count > 0)
            {
                try
                {
                    // The sent flags are a write too: covered by the fill's backup, or one of their own.
                    if (result.BackupPath.Length == 0) result.BackupPath = await Task.Run(() => _log.Backup(BackupPurpose));
                    uploads = await SendAsync(request);
                }
                catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or HrdException or IOException)
                {
                    uploads = $"Nothing was sent: {ex.Message}";
                }
            }

            if (result.Applied.Count > 0 || result.Failed.Count > 0 || uploads.Length > 0)
            {
                var refresh = await _log.RefreshAsync();
                result.Refreshed = refresh.Ok;
                result.RefreshMessage = refresh.Ok ? "" : refresh.Message;
            }

            _result = result;
            _summary.Text = HrdBatchFiller.Summary(result, _log.FilePath, title: Single ? "Fill HRD QSO" : "Batch fill") +
                            (uploads.Length > 0 ? Environment.NewLine + uploads : "") +
                            (Single ? Environment.NewLine + "Double-click the QSO in HRD to check it." + Environment.NewLine : "");
            _edit.Visible = _notes.Visible = false;
            _summary.Visible = true;
            _intro.Text = $"Done: {result.Applied.Count} QSO{(result.Applied.Count == 1 ? "" : "s")} written" +
                          (result.NotApplied.Count + result.Failed.Count > 0 ? $", {result.NotApplied.Count + result.Failed.Count} to check below" : "") +
                          (Single ? ". Double-click the QSO in HRD to check it." : ".");
            SetStage(Stage.Done);
        }

        // Puts the batch's QSOs back as they were.
        private async Task UndoAsync()
        {
            if (_result is not { Applied.Count: > 0 } batch) return;
            if (HrdEditWindow.FindAll().Count > 0)
            {
                MessageBox.Show(this, "HRD has an Add/Edit window open. Close it (without saving), then click Undo again.",
                    "Fill selected QSOs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this,
                    $"Put the {batch.Applied.Count} QSO{(batch.Applied.Count == 1 ? "" : "s")} back to what they were before this batch?\n\n" +
                    "A backup of the log is made first.",
                    "Fill selected QSOs", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            _undo.Enabled = _close.Enabled = false;
            HrdBatchResult undone;
            try
            {
                undone = await Task.Run(() => _filler.Undo(batch, BackupPurpose));
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or HrdException or IOException)
            {
                _undo.Enabled = _close.Enabled = true;
                MessageBox.Show(this, $"Nothing was changed (it's all or nothing).\n\n{ex.Message}",
                    "Fill selected QSOs", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (undone.Applied.Count > 0 || undone.Failed.Count > 0)
            {
                var refresh = await _log.RefreshAsync();
                undone.Refreshed = refresh.Ok;
                undone.RefreshMessage = refresh.Ok ? "" : refresh.Message;
            }

            _undone = true;
            _summary.Text = HrdBatchFiller.Summary(undone, _log.FilePath, undo: true, title: Single ? "Fill HRD QSO" : "Batch fill") +
                            Environment.NewLine + "----- the batch that was undone -----" + Environment.NewLine +
                            HrdBatchFiller.Summary(batch, _log.FilePath, title: Single ? "Fill HRD QSO" : "Batch fill");
            _intro.Text = $"Undone: {undone.Applied.Count} QSO{(undone.Applied.Count == 1 ? "" : "s")} put back" +
                          (undone.NotApplied.Count + undone.Failed.Count > 0 ? $", {undone.NotApplied.Count + undone.Failed.Count} to check below" : "") + ".";
            _undo.Visible = false;
            _close.Enabled = true;
            if (undone.BackupPath.Length > 0) _result = undone;
            _openBackup.Visible = _result is { BackupPath.Length: > 0 };
        }

        // ---- the list --------------------------------------------------------------

        // Only a QSO with something to write can be ticked.
        private void Qsos_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (_populating) return;
            if (_qsos.Items[e.Index].Tag is HrdBatchItem { Applicable: false } || _stage != Stage.Ready)
                e.NewValue = e.CurrentValue;
        }

        private void SetAllTicks(bool ticked)
        {
            foreach (ListViewItem row in _qsos.Items)
                if (((HrdBatchItem)row.Tag!).Applicable) row.Checked = ticked;
        }

        // The QSOs Apply works on: the ticked ones, or for the single fill its
        // one QSO (sent even with nothing to write).
        private List<HrdBatchItem> Chosen() => Single
            ? _items.Where(i => i.Skipped == null && i.Row != null).ToList()
            : _qsos.CheckedItems.Cast<ListViewItem>().Select(i => (HrdBatchItem)i.Tag!).ToList();

        private void UpdateApplyButton()
        {
            RefreshSendBox();
            var chosen = Chosen();
            var send = SendPlan(chosen);
            bool writes = chosen.Any(i => i.Applicable), sends = send.Lotw.Count + send.Qrz.Count > 0;
            int n = chosen.Count(i => i.Applicable);
            string action = writes && sends ? " and send" : "";
            _apply.Text = Single
                ? (writes ? $"&Approve: write{action}" : "&Approve: send")
                : (n == 1 ? $"&Apply to 1 QSO{action}" : $"&Apply to {n} QSOs{action}");

            var errors = chosen.Where(_edit.HasErrors).Select(i => i.Call).ToList();
            bool locationsMissing = _sendLotw.Checked && _sendLotw.Enabled && _locationBoxes.Values.Any(b => b.SelectedItem == null);
            _apply.Enabled = _stage == Stage.Ready && chosen.Count > 0 && (writes || (Single && sends)) &&
                             errors.Count == 0 && !locationsMissing;
            if (errors.Count > 0)
                _lotwNote.Text = $"Fix the red value{(errors.Count == 1 ? "" : "s")} first ({string.Join(", ", errors.Distinct())}).";
            else if (locationsMissing)
                _lotwNote.Text = "Pick the TQSL location to sign with (or untick LoTW).";
        }

        // ---- the send box ------------------------------------------------------------

        private void BuildSendBox()
        {
            _sendPanel.Dock = DockStyle.Bottom;
            _sendPanel.AutoSize = true;
            _sendPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _sendPanel.Padding = new Padding(8, 4, 8, 0);
            _sendPanel.Visible = false;

            _sendLotw.AutoSize = true;
            _sendLotw.Text = "Send to LoTW";
            _sendQrz.AutoSize = true;
            _sendQrz.Text = "Send to QRZ logbook";
            _sendLotw.CheckedChanged += (_, _) => UpdateApplyButton();
            _sendQrz.CheckedChanged += (_, _) => UpdateApplyButton();
            new ToolTip().SetToolTip(_sendLotw, "Signs the QSOs with TQSL (the one HRD uses, no window) and uploads them, then sets HRD's LoTW sent flags.");
            new ToolTip().SetToolTip(_sendQrz, "Sends the QSOs with HRD's QRZ logbook API key (replacing what QRZ has), then sets HRD's QRZ sent flags.");
            foreach (var note in new[] { _lotwNote, _qrzNote })
            {
                note.AutoSize = true;
                note.ForeColor = SystemColors.GrayText;
                note.Margin = new Padding(20, 0, 3, 3);
            }
            _lotwLocations.AutoSize = true;
            _lotwLocations.FlowDirection = FlowDirection.TopDown;
            _lotwLocations.WrapContents = false;
            _lotwLocations.Margin = new Padding(20, 0, 3, 3);

            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            };
            var profiles = HrdStationProfiles.Load();
            _stationBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _stationBox.Width = 420;
            _stationBox.Items.AddRange(profiles.ToArray<object>());
            _stationBox.SelectedItem = profiles.FirstOrDefault(p => p.DisplayName == _profile?.DisplayName);
            _stationBox.SelectedIndexChanged += async (_, _) =>
            {
                if (_replanning || _stage != Stage.Ready || _stationBox.SelectedItem is not HrdStationProfile picked ||
                    picked.DisplayName == _profile?.DisplayName) return;
                _profile = picked;
                _replanning = true;
                try { await PlanAsync(stationPicked: true); }
                finally { _replanning = false; }
            };
            new ToolTip().SetToolTip(_stationBox, "HRD's My Station profiles. Picking one works the QSO out again with that station " +
                                                  "(your typed changes go).");
            var stationRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 3, 6) };
            stationRow.Controls.Add(new Label { AutoSize = true, Text = "My Station (HRD):", Margin = new Padding(3, 6, 3, 0) });
            stationRow.Controls.Add(_stationBox);
            stationRow.Visible = profiles.Count > 0;

            layout.Controls.AddRange([stationRow, _sendLotw, _lotwLocations, _lotwNote, _sendQrz, _qrzNote]);
            _sendPanel.Controls.Add(layout);
        }

        // A My Station to sign for: the QSO's station callsign, grid and parks as
        // they'll be written.
        private static (string Key, string Call, string Grid, string Parks) StationOf(HrdBatchItem item)
        {
            string call = HrdQsoEdits.Text(item.Current("COL_STATION_CALLSIGN")).ToUpperInvariant();
            if (call.Length == 0) call = HrdQsoEdits.Text(item.Current("COL_OPERATOR")).ToUpperInvariant();
            string grid = HrdQsoEdits.Text(item.Current("COL_MY_GRIDSQUARE"));
            string parks = HrdQsoEdits.Text(item.Current("COL_MY_POTA_REF")).ToUpperInvariant();
            return ($"{call}|{grid.ToUpperInvariant()}|{parks}", call, grid, parks);
        }

        // One TQSL location drop-down per My Station among the chosen QSOs, and
        // the ticks' defaults the first time.
        private void RefreshSendBox()
        {
            if (_stage != Stage.Ready) return;
            var chosen = Chosen();
            var stations = chosen.Select(StationOf).DistinctBy(s => s.Key).ToList();
            int notOnLotw = chosen.Count(i => i.Row != null && !HrdUploadFlags.IsLotwSent(i.Row));

            _lotwLocations.SuspendLayout();
            var gone = _replanning ? _locationBoxes.Keys.ToList() : _locationBoxes.Keys.Except(stations.Select(s => s.Key)).ToList();
            foreach (var key in gone)
            {
                _lotwLocations.Controls.Remove(_locationBoxes[key].Parent);
                _locationBoxes.Remove(key);
            }
            foreach (var station in stations.Where(s => !_locationBoxes.ContainsKey(s.Key)))
            {
                var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
                box.Items.AddRange(_tqslLocations.ToArray<object>());
                var pick = TqslLocations.Pick(_tqslLocations, station.Call, station.Grid,
                    HrdUploadSettings.LotwStation(station.Call), station.Parks.Split(',', StringSplitOptions.RemoveEmptyEntries));
                box.SelectedItem = pick;
                box.SelectedIndexChanged += (_, _) => UpdateApplyButton();
                var label = new Label
                {
                    AutoSize = true, Margin = new Padding(3, 6, 3, 0),
                    Text = $"{station.Call} at {(station.Grid.Length > 0 ? station.Grid : "no grid")}" +
                           (station.Parks.Length > 0 ? $" ({station.Parks})" : "") + ":",
                };
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                row.Controls.Add(label);
                row.Controls.Add(box);
                _lotwLocations.Controls.Add(row);
                _locationBoxes[station.Key] = box;
            }
            _lotwLocations.ResumeLayout();

            bool qrzKeys = stations.Count > 0 && stations.All(s => HrdUploadSettings.QrzApiKey(s.Call).Length > 0);
            bool tqsl = _tqslPath != null && _tqslLocations.Count > 0;
            _sendLotw.Enabled = tqsl && notOnLotw > 0;
            _sendQrz.Enabled = qrzKeys;
            if (!_sendDefaultsSet && chosen.Count > 0)
            {
                _sendLotw.Checked = _sendLotw.Enabled;
                _sendQrz.Checked = _sendQrz.Enabled;
                _sendDefaultsSet = true;
            }
            if (!_sendLotw.Enabled) _sendLotw.Checked = false;
            if (!_sendQrz.Enabled) _sendQrz.Checked = false;
            _lotwLocations.Visible = _sendLotw.Checked;

            _sendLotw.Text = notOnLotw == chosen.Count ? "Send to LoTW" : $"Send to LoTW ({notOnLotw} not sent yet)";
            _lotwNote.Text = !tqsl ? (_tqslPath == null ? "TQSL isn't installed (HRD's TQSL setting points nowhere)." : "TQSL has no station locations.")
                : notOnLotw == 0 ? "Already on LoTW (HRD has it as sent): nothing to send there."
                : chosen.Count > notOnLotw ? "QSOs HRD has as sent to LoTW already aren't sent again." : "";
            _qrzNote.Text = qrzKeys ? "" : stations.Count == 0 ? ""
                : "HRD has no QRZ logbook API key for " +
                  string.Join(", ", stations.Where(s => HrdUploadSettings.QrzApiKey(s.Call).Length == 0).Select(s => s.Call).Distinct()) + ".";
        }

        // What Apply would send: the TQSL location per QSO for LoTW, the QSOs for QRZ.
        private (Dictionary<long, TqslLocation> Lotw, List<long> Qrz) SendPlan(List<HrdBatchItem> chosen)
        {
            var lotw = new Dictionary<long, TqslLocation>();
            var qrz = new List<long>();
            foreach (var item in chosen)
            {
                if (_sendLotw.Checked && _sendLotw.Enabled && item.Row != null && !HrdUploadFlags.IsLotwSent(item.Row) &&
                    _locationBoxes.TryGetValue(StationOf(item).Key, out var box) && box.SelectedItem is TqslLocation location)
                    lotw[item.Pk] = location;
                if (_sendQrz.Checked && _sendQrz.Enabled) qrz.Add(item.Pk);
            }
            return (lotw, qrz);
        }

        // Sends, asking for TQSL's certificate password if it wants one, and
        // returns the result as text for the summary.
        private async Task<string> SendAsync(UploadRequest request)
        {
            var uploader = new QsoUploader(_log);
            var progress = new Progress<string>(text => _intro.Text = text);
            var report = await uploader.SendAsync(request, progress);
            var lotw = report.Lotw.ToList();
            var qrz = report.Qrz.ToList();
            var flagErrors = new List<string>();
            if (report.FlagError.Length > 0) flagErrors.Add(report.FlagError);

            var needing = report.LotwNeedingPassword;
            while (needing.Count > 0)
            {
                string names = string.Join(", ", needing.Values.Select(l => l.Name).Distinct());
                using var ask = new LotwPasswordForm(names);
                if (ask.ShowDialog(this) != DialogResult.OK) break;
                var retry = await uploader.SendAsync(new UploadRequest(needing, [], ask.Password), progress);
                lotw.RemoveAll(l => needing.ContainsKey(l.Pk));
                lotw.AddRange(retry.Lotw);
                if (retry.FlagError.Length > 0) flagErrors.Add(retry.FlagError);
                needing = retry.LotwNeedingPassword;
            }

            var names2 = _items.ToDictionary(i => i.Pk, i => $"{i.Call} {i.When}");
            var text = new System.Text.StringBuilder();
            if (lotw.Count > 0)
            {
                text.AppendLine($"LoTW: {lotw.Count(l => l.Result.Sent)} of {lotw.Count} sent.");
                foreach (var group in lotw.GroupBy(l => (l.Location, l.Result.Message)))
                    text.AppendLine($"  {(group.First().Result.Sent ? "Sent" : "NOT sent")} as \"{group.Key.Location}\": " +
                                    $"{string.Join(", ", group.Select(g => names2.GetValueOrDefault(g.Pk, g.Pk.ToString())))}" +
                                    (group.Key.Message.Length > 0 ? $" - {group.Key.Message}" : ""));
            }
            if (qrz.Count > 0)
            {
                text.AppendLine($"QRZ logbook: {qrz.Count(q => q.Result.Sent)} of {qrz.Count} sent.");
                foreach (var (pk, result) in qrz)
                    text.AppendLine($"  {names2.GetValueOrDefault(pk, pk.ToString())}: {(result.Sent ? "sent" : "NOT sent")}" +
                                    (result.LogId.Length > 0 ? $" (logid {result.LogId})" : "") +
                                    (result.Message.Length > 0 ? $" - {result.Message}" : ""));
            }
            foreach (string error in flagErrors)
                text.AppendLine($"Sent, but HRD's sent flags couldn't be set: {error}");
            return text.ToString();
        }

        // Which location a QSO uses, when QRZ's and the address's disagree.
        private static string RowLocation(HrdBatchItem item) =>
            item.AlternativeChanges == null ? "" : item.UseAlternative ? "address" : "QRZ";

        // Which My Station a QSO ends up with, when it has a different one than selected.
        private static string RowStation(HrdBatchItem item) =>
            !item.HasStationChoice ? "" : item.UseSelectedStation ? "selected" : "HRD's";

        private static string RowCount(HrdBatchItem item) => item.Skipped != null ? "-" : item.Active!.Changes.Count.ToString();

        private static string RowNotes(HrdBatchItem item)
        {
            if (item.Skipped != null) return "skipped: " + item.Skipped;
            var changes = item.Active!;
            if (!changes.Any) return "nothing to change";
            return changes.Warnings.Count > 0
                ? changes.Warnings[0] + (changes.Warnings.Count > 1 ? $" (+{changes.Warnings.Count - 1} more)" : "")
                : "";
        }

        private string BackupPurpose => Single ? HrdLogDatabase.FillQsoPurpose : HrdLogDatabase.BatchFillPurpose;

        // The single fill hides the list (its handle never exists, so selecting
        // a row raises no event): its one QSO is always the one shown.
        private ListViewItem? SelectedRow =>
            Single ? (_qsos.Items.Count > 0 ? _qsos.Items[0] : null)
            : _qsos.SelectedItems.Count > 0 ? _qsos.SelectedItems[0] : null;

        // A tick in the Location choice: the selected QSO uses QRZ's location or the
        // address's. Always exactly one is ticked.
        private void ChoiceChanged(bool useAddress)
        {
            if (_settingChoice || SelectedRow is not { Tag: HrdBatchItem { AlternativeChanges: not null } item } row) return;
            var box = useAddress ? _choiceAddress : _choiceQrz;
            var other = useAddress ? _choiceQrz : _choiceAddress;

            _settingChoice = true;
            try
            {
                if (box.Checked)
                {
                    other.Checked = false;
                    item.UseAlternative = useAddress;
                }
                else
                {
                    box.Checked = true;   // unticking one just ticks it again: one is always chosen
                    return;
                }
            }
            finally
            {
                _settingChoice = false;
            }

            RefreshRow(row, item);
            ShowDetails();
            UpdateApplyButton();
        }

        private void RefreshRow(ListViewItem row, HrdBatchItem item)
        {
            row.SubItems[2].Text = RowCount(item);
            row.SubItems[3].Text = RowLocation(item);
            row.SubItems[4].Text = RowStation(item);
            row.SubItems[5].Text = RowNotes(item);
            row.ForeColor = item.Applicable ? _qsos.ForeColor : SystemColors.GrayText;

            // A QSO with something to write is ticked, one with nothing isn't. Set
            // under _populating so the "only a QSO with changes can be ticked" check
            // in Qsos_ItemCheck lets it through.
            _populating = true;
            try { row.Checked = item.Applicable; }
            finally { _populating = false; }
        }

        // A tick in the Station choice: the selected QSO keeps the My Station it has in
        // HRD, or gets the selected one. Always exactly one is ticked.
        private void StationChoiceChanged(bool useSelected)
        {
            if (_settingChoice || SelectedRow is not { Tag: HrdBatchItem { StationChanges: not null } item } row) return;
            var box = useSelected ? _stationSelected : _stationHrd;
            var other = useSelected ? _stationHrd : _stationSelected;

            _settingChoice = true;
            try
            {
                if (box.Checked)
                {
                    other.Checked = false;
                    item.UseSelectedStation = useSelected;
                }
                else
                {
                    box.Checked = true;
                    return;
                }
            }
            finally
            {
                _settingChoice = false;
            }

            RefreshRow(row, item);
            ShowDetails();
            UpdateApplyButton();
        }

        // The same station choice for every QSO that has one.
        private void SetAllStations(bool useSelected)
        {
            foreach (ListViewItem row in _qsos.Items)
            {
                if (row.Tag is not HrdBatchItem { StationChanges: not null } item) continue;
                item.UseSelectedStation = useSelected;
                RefreshRow(row, item);
            }
            ShowDetails();
            UpdateApplyButton();
        }

        // The same location choice for every QSO that has one.
        private void SetAllLocations(bool useAddress)
        {
            foreach (ListViewItem row in _qsos.Items)
            {
                if (row.Tag is not HrdBatchItem { AlternativeChanges: not null } item) continue;
                item.UseAlternative = useAddress;
                RefreshRow(row, item);
            }
            ShowDetails();
            UpdateApplyButton();
        }

        private void ShowDetails()
        {
            _notes.Clear();
            _choicePanel.Visible = false;
            var item = SelectedRow?.Tag as HrdBatchItem;
            _edit.Show(item, editable: _stage == Stage.Ready);
            if (item == null) return;

            if (item.Skipped != null)
            {
                _notes.Text = $"Skipped: {item.Skipped}";
                return;
            }
            var changes = item.Active!;

            var lines = new List<string>();
            if (!changes.Any) lines.Add("Everything already matched - nothing to change (any field can still be corrected below).");
            if (item.Restores.Count > 0)
                lines.Add("HRD's Lookup changed " + string.Join(", ", item.Restores.Select(r => r.Label)) + " - put back.");
            int top = 4;
            bool location = item.HasLocationChoice, station = item.HasStationChoice;
            _settingChoice = true;
            foreach (var box in new[] { _choiceQrz, _choiceAddress }) box.Visible = location;
            foreach (var box in new[] { _stationHrd, _stationSelected }) box.Visible = station;
            if (location)
            {
                lines.Add("QRZ's location is in another state than its record lists - tick which location to use.");
                _choiceQrz.Text = $"QRZ's location: {item.QrzLocationText}";
                _choiceAddress.Text = $"The address's location: {item.AlternativeLocationText}";
                _choiceQrz.Checked = !item.UseAlternative;
                _choiceAddress.Checked = item.UseAlternative;
                _choiceQrz.Top = top;
                _choiceAddress.Top = top + 23;
                top += 48;
            }
            if (station)
            {
                lines.Add($"This QSO's My Station ({item.StationHereText}) is a different station than the selected " +
                          $"\"{item.StationSelectedText}\" - tick which My Station to use.");
                _stationHrd.Text = $"Keep what's in HRD: {item.StationHereText}";
                _stationSelected.Text = $"Use the selected station: {item.StationSelectedText}";
                _stationHrd.Checked = !item.UseSelectedStation;
                _stationSelected.Checked = item.UseSelectedStation;
                _stationHrd.Top = top;
                _stationSelected.Top = top + 23;
                top += 48;
            }
            _settingChoice = false;
            _choicePanel.Height = Math.Max(top, 8);
            _choicePanel.Visible = (location || station) && _stage == Stage.Ready;
            lines.AddRange(changes.Warnings);
            _notes.Text = string.Join(Environment.NewLine, lines.Select(w => "• " + w));
        }
    }
}
