using CallsignLookup.Services;
using CallsignLookup.Services.Hrd;

namespace CallsignLookup
{
    // The fill report for one QSO: every field the fill would change, plus the
    // main fields it leaves alone, with "Will become" editable. Untick a row to
    // leave that field as it is. A value that can't be stored stays red with
    // the reason, and HasErrors keeps Apply off until it's fixed.
    public sealed class QsoEditGrid : UserControl
    {
        private readonly DataGridView _grid = new();
        private readonly DataGridViewCheckBoxColumn _tick = new() { HeaderText = "Write", Width = 46 };
        private readonly DataGridViewTextBoxColumn _field = new() { HeaderText = "Field", Width = 150, ReadOnly = true };
        private readonly DataGridViewTextBoxColumn _was = new() { HeaderText = "Was", Width = 260, ReadOnly = true };
        private readonly DataGridViewTextBoxColumn _now = new()
            { HeaderText = "Will become (click to change)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };

        // Typed values that couldn't be stored, per QSO and column, kept while
        // the user looks at other QSOs.
        private readonly Dictionary<HrdBatchItem, Dictionary<string, (string Text, string Error)>> _errors = [];
        private readonly Func<int, string?> _continentFor;
        private HrdBatchItem? _item;
        private bool _editable;
        private bool _filling;

        // A value or tick changed (the QSO's changes are different now).
        public event EventHandler? Changed;

        public QsoEditGrid(Func<int, string?> continentFor)
        {
            _continentFor = continentFor;
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.MultiSelect = false;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.ShowCellToolTips = true;
            _grid.Columns.AddRange(_tick, _field, _was, _now);
            foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;

            _grid.CellEndEdit += Grid_CellEndEdit;
            _grid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                // A tick takes effect at once, not when the cell is left.
                if (_grid.CurrentCell?.OwningColumn == _tick && _grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += Grid_CellValueChanged;
            _grid.EditingControlShowing += Grid_EditingControlShowing;
            _grid.DataError += (_, e) => e.ThrowException = false;
            Controls.Add(_grid);
        }

        public bool HasErrors(HrdBatchItem item) => _errors.TryGetValue(item, out var e) && e.Count > 0;

        // Shows this QSO (null: nothing). editable false: read only (done, or
        // a skipped QSO).
        public void Show(HrdBatchItem? item, bool editable)
        {
            _item = item;
            _editable = editable && item is { Skipped: null, Row: not null };
            Fill();
        }

        // Back to what the fill worked out: the user's edits and unticks go.
        public void Reset(HrdBatchItem item)
        {
            item.ClearEdits();
            _errors.Remove(item);
            if (item == _item) Fill();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Fill()
        {
            _filling = true;
            try
            {
                _grid.Rows.Clear();
                if (_item is not { Skipped: null } item) return;
                _errors.TryGetValue(item, out var errors);
                foreach (var row in HrdQsoEdits.Rows(item))
                {
                    string now = HrdQsoEdits.EditText(row.Column, row.Now, CurrentRow(item));
                    int at = _grid.Rows.Add(row.Ticked, row.Label, Was(item, row), now);
                    var gridRow = _grid.Rows[at];
                    gridRow.Tag = row;
                    gridRow.Cells[0].ReadOnly = !_editable || !row.Changes;
                    gridRow.Cells[3].ReadOnly = !_editable;
                    if (!row.Changes)
                    {
                        gridRow.Cells[0].Value = false;
                        gridRow.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                    }
                    else if (!row.Ticked)
                    {
                        gridRow.Cells[3].Style.Font = new Font(_grid.Font, FontStyle.Strikeout);
                    }
                    else
                    {
                        gridRow.Cells[3].Style.Font = new Font(_grid.Font, FontStyle.Bold);
                    }

                    if (errors != null && errors.TryGetValue(row.Column, out var bad))
                    {
                        gridRow.Cells[3].Value = bad.Text;
                        gridRow.Cells[3].ToolTipText = bad.Error;
                        gridRow.Cells[3].Style.BackColor = Color.MistyRose;
                        gridRow.Cells[3].Style.ForeColor = Color.DarkRed;
                        gridRow.Cells[1].ToolTipText = bad.Error;
                    }
                }
            }
            finally
            {
                _filling = false;
            }
        }

        // The QSO's columns as they'd be written (for an island's name, which
        // depends on the IOTA reference that may itself be changing).
        private static Dictionary<string, object?> CurrentRow(HrdBatchItem item) =>
            new() { ["COL_IOTA"] = item.Current("COL_IOTA") };

        private static string Was(HrdBatchItem item, HrdQsoEdits.ReportRow row)
        {
            string text = HrdQsoEdits.EditText(row.Column, row.Was, item.Row);
            return text.Length == 0 ? "(blank)" : text;
        }

        private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (_filling || e.RowIndex < 0 || e.ColumnIndex != 0 || _item == null) return;
            if (_grid.Rows[e.RowIndex].Tag is not HrdQsoEdits.ReportRow row) return;
            bool ticked = _grid.Rows[e.RowIndex].Cells[0].Value is true;
            if (ticked) _item.Excluded.Remove(row.Column);
            else _item.Excluded.Add(row.Column);
            BeginInvoke(Refill);
        }

        private void Grid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
        {
            if (_filling || e.RowIndex < 0 || e.ColumnIndex != 3 || _item is not { } item) return;
            if (_grid.Rows[e.RowIndex].Tag is not HrdQsoEdits.ReportRow row) return;
            string text = Convert.ToString(_grid.Rows[e.RowIndex].Cells[3].Value) ?? "";
            string shown = HrdQsoEdits.EditText(row.Column, row.Now, CurrentRow(item));

            if (!_errors.TryGetValue(item, out var errors)) _errors[item] = errors = [];
            if (text.Trim() == shown.Trim())
            {
                // Typed back what it was (or left it): no edit, and no error any more.
                if (errors.Remove(row.Column)) BeginInvoke(Refill);
                return;
            }

            string? error = HrdQsoEdits.Edit(item, row.Label, row.Column, text, _continentFor);
            if (error != null) errors[row.Column] = (text, error);
            else errors.Remove(row.Column);
            BeginInvoke(Refill);
        }

        private void Refill()
        {
            if (IsDisposed) return;
            int column = _grid.CurrentCell?.ColumnIndex ?? 3;
            string? at = (_grid.CurrentRow?.Tag as HrdQsoEdits.ReportRow)?.Column;
            Fill();
            if (at != null)
                foreach (DataGridViewRow r in _grid.Rows)
                    if (r.Tag is HrdQsoEdits.ReportRow { } row && row.Column == at)
                    {
                        _grid.CurrentCell = r.Cells[column];
                        break;
                    }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        // Suggestions while typing, for the fields with a list of values.
        private void Grid_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (e.Control is not TextBox box) return;
            string column = (_grid.CurrentRow?.Tag as HrdQsoEdits.ReportRow)?.Column ?? "";
            IEnumerable<string>? choices = column switch
            {
                "COL_COUNTRY" or "COL_MY_COUNTRY" => HrdCountries.All(),
                "COL_ARRL_SECT" => ArrlSectionService.AllSections(),
                "COL_IOTA_ISLAND_ID" when _item != null =>
                    IotaService.IslandsOf(HrdQsoEdits.Text(_item.Current("COL_IOTA"))).Select(i => $"{i.Id} - {i.Name}"),
                _ => null,
            };
            if (choices == null)
            {
                box.AutoCompleteMode = AutoCompleteMode.None;
                return;
            }
            var source = new AutoCompleteStringCollection();
            source.AddRange(choices.ToArray());
            box.AutoCompleteCustomSource = source;
            box.AutoCompleteSource = AutoCompleteSource.CustomSource;
            box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        }
    }
}
