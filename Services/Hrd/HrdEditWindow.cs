using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace CallsignLookup.Services.Hrd
{
    // HRD Logbook's "Edit:" / "Add:" log entry window, driven through UI
    // Automation (HRD Logbook is a Qt app with accessibility on). Fields are
    // found by the last part of their AutomationId - "edtLAT", "cbxARRLSECT".
    //
    // What was found by testing against HRD 6.9 (2026-10-04):
    //   * Only the controls on the tab that's showing are exposed, so the
    //     right tab is selected before a field on it is read or written.
    //   * Text boxes take ValuePattern.SetValue, and HRD saves those values
    //     on Update. Its validated boxes silently refuse what they don't
    //     accept (Distance won't take an empty value - it's cleared by
    //     keyboard instead).
    //   * Drop-downs ignore SetValue and only expose the rows on screen, so
    //     they're set by opening them and typing the entry, then Enter.
    //   * The My Station profile list is a tree; selecting an item applies it.
    // Every call blocks on HRD, so use it off the UI thread.
    public sealed class HrdEditWindow
    {
        private const string WindowId = "QApplication.HRDLBEdit";

        private readonly AutomationElement _window;
        private Dictionary<string, AutomationElement>? _controls;

        private HrdEditWindow(AutomationElement window) => _window = window;

        public string Title => _window.Current.Name.Trim();

        // The open Edit/Add window, or null if HRD Logbook isn't running or
        // has none open. With several, the first one found.
        public static HrdEditWindow? Find()
        {
            foreach (var process in Process.GetProcessesByName("HRDLogBook"))
            {
                using (process)
                {
                    var byProcess = new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id);
                    var isEditWindow = new PropertyCondition(AutomationElement.AutomationIdProperty, WindowId);
                    foreach (AutomationElement top in AutomationElement.RootElement.FindAll(TreeScope.Children, byProcess))
                    {
                        // Qt reports the Edit window as a child of the main
                        // logbook window rather than as a window of its own.
                        var window = top.FindFirst(TreeScope.Subtree, isEditWindow);
                        if (window != null) return new HrdEditWindow(window);
                    }
                }
            }
            return null;
        }

        // Every open Edit/Add window (HRD can have several).
        public static List<HrdEditWindow> FindAll()
        {
            var windows = new List<HrdEditWindow>();
            foreach (var process in Process.GetProcessesByName("HRDLogBook"))
            {
                using (process)
                {
                    var byProcess = new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id);
                    var isEditWindow = new PropertyCondition(AutomationElement.AutomationIdProperty, WindowId);
                    foreach (AutomationElement top in AutomationElement.RootElement.FindAll(TreeScope.Children, byProcess))
                        foreach (AutomationElement window in top.FindAll(TreeScope.Subtree, isEditWindow))
                            windows.Add(new HrdEditWindow(window));
                }
            }
            return windows;
        }

        // Presses the window's Cancel button, which throws away what's in it
        // and closes it. False if it's still open afterwards - HRD asking
        // something, say - or has no Cancel button to press.
        public bool CloseWithoutSaving()
        {
            AutomationElement? cancel = null;
            foreach (AutomationElement e in _window.FindAll(TreeScope.Descendants,
                         new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
            {
                if (e.Current.AutomationId.EndsWith(".btnCancel", StringComparison.Ordinal) ||
                    e.Current.Name.Replace("&", "").Trim().Equals("Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    cancel = e;
                    break;
                }
            }
            if (cancel == null) return false;

            int before = FindAll().Count;
            ((InvokePattern)cancel.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            // Closed when one fewer window is found. Not "this element can't be
            // read any more": HRD's Qt accessibility leaves a closed window
            // readable for a while (tested 2026-10-12 - the window had gone but
            // IsOpen stayed true for the whole wait).
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(300);
                if (!IsOpen || FindAll().Count < before) return true;
            }
            return false;
        }

        public bool IsOpen
        {
            get
            {
                try { _ = _window.Current.Name; return true; }
                catch (ElementNotAvailableException) { return false; }
            }
        }

        // ---- tabs ---------------------------------------------------------

        public string SelectedTab()
        {
            foreach (var tab in Tabs())
                if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object p) &&
                    ((SelectionItemPattern)p).Current.IsSelected)
                    return tab.Current.Name;
            return "";
        }

        public void SelectTab(string name)
        {
            var tab = Tabs().FirstOrDefault(t => t.Current.Name == name)
                ?? throw new HrdException($"HRD's log entry window has no \"{name}\" tab.");
            ((SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            Thread.Sleep(500);
            _controls = null;
        }

        private IEnumerable<AutomationElement> Tabs() =>
            _window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>();

        // ---- fields -------------------------------------------------------

        private AutomationElement? Control(string id)
        {
            _controls ??= LoadControls();
            return _controls.TryGetValue(id, out var element) ? element : null;
        }

        private Dictionary<string, AutomationElement> LoadControls()
        {
            var controls = new Dictionary<string, AutomationElement>();
            foreach (AutomationElement e in _window.FindAll(TreeScope.Descendants, Condition.TrueCondition))
            {
                string id = e.Current.AutomationId;
                if (id.Length == 0) continue;
                controls.TryAdd(id[(id.LastIndexOf('.') + 1)..], e);
            }
            return controls;
        }

        private void Refresh() => _controls = null;

        // Drops the controls found so far, so the next read finds them again: HRD
        // sometimes rebuilds a control (an island list after the IOTA reference
        // changes), leaving the one found earlier reading stale.
        public void ForgetControls() => Refresh();

        public bool Has(string id) => Control(id) != null;

        // Null when the field isn't on the tab that's showing.
        public string? Read(string id)
        {
            if (Control(id) is not AutomationElement e) return null;
            try
            {
                return e.TryGetCurrentPattern(ValuePattern.Pattern, out object p)
                    ? ((ValuePattern)p).Current.Value.Trim()
                    : null;
            }
            catch (ElementNotAvailableException)
            {
                Refresh();
                return null;
            }
        }

        // A check box: null when it isn't on the tab that's showing.
        public bool? ReadChecked(string id)
        {
            if (Control(id) is not AutomationElement e) return null;
            try
            {
                return e.TryGetCurrentPattern(TogglePattern.Pattern, out object p)
                    ? ((TogglePattern)p).Current.ToggleState == ToggleState.On
                    : null;
            }
            catch (ElementNotAvailableException)
            {
                Refresh();
                return null;
            }
        }

        // Ticks or unticks a check box (only if it isn't already so); returns
        // how it's left.
        public bool? SetChecked(string id, bool on)
        {
            var e = Control(id) ?? throw new HrdException($"Can't find HRD's {id} box.");
            var toggle = (TogglePattern)e.GetCurrentPattern(TogglePattern.Pattern);
            if ((toggle.Current.ToggleState == ToggleState.On) != on)
            {
                toggle.Toggle();
                Thread.Sleep(250);
            }
            return ReadChecked(id);
        }

        // A field in the top half of the window (always showing), found
        // straight from its full id - much quicker than Read's look through
        // every control, for checking every second which QSO is open.
        public string? ReadTopField(string id)
        {
            var e = _window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, $"{WindowId}.{id}"));
            if (e == null) return Read(id);
            return e.TryGetCurrentPattern(ValuePattern.Pattern, out object p)
                ? ((ValuePattern)p).Current.Value.Trim()
                : null;
        }

        // Returns what the field holds afterwards, so the caller can check it took.
        public string? SetText(string id, string value)
        {
            var e = Control(id) ?? throw new HrdException($"Can't find HRD's {id} box.");
            ((ValuePattern)e.GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
            Thread.Sleep(250);
            return Read(id);
        }

        // Empties a box by keyboard - for boxes that won't take an empty SetValue.
        // Leaves it as it is if the box can't be given the focus: Ctrl+A, Delete
        // would empty whatever box has it instead.
        public string? ClearByKeyboard(string id)
        {
            var e = Control(id) ?? throw new HrdException($"Can't find HRD's {id} box.");
            if (!FocusForTyping(e)) return Read(id);
            Keyboard.Press(Keyboard.Control, 'A');
            Keyboard.Press(Keyboard.Delete);
            Thread.Sleep(250);
            return Read(id);
        }

        // Opens a drop-down, types the entry, Enter. HRD's lists find the
        // first entry that starts with what's typed.
        public string? ChooseInDropDown(string id, string text, bool openFirst = true)
        {
            var e = Control(id) ?? throw new HrdException($"Can't find HRD's {id} list.");
            if (!FocusForTyping(e))
                throw new HrdException($"Couldn't put the cursor in HRD's {id} list, so nothing was typed - set it by hand.");
            if (openFirst && e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object p))
            {
                ((ExpandCollapsePattern)p).Expand();
                Thread.Sleep(500);
            }
            Keyboard.Type(text);
            Thread.Sleep(250);
            Keyboard.Press(Keyboard.Enter);
            Thread.Sleep(500);
            Refresh();
            return Read(id);
        }

        // Keys go wherever the focus really is, and UI Automation's SetFocus
        // doesn't always get it to HRD's Qt lists: on CT9/DF7EE's IOTA tab it
        // stayed on the island ID box (and in a test went to the Date box), so
        // "AF-014", Enter was typed into the wrong box. So check the focus got
        // there - if not, click the control as the user would - and only type
        // once it has. False when it still hasn't: nothing must be typed then.
        private static bool FocusForTyping(AutomationElement e)
        {
            e.SetFocus();
            Thread.Sleep(200);
            if (HasFocus(e)) return true;

            var r = e.Current.BoundingRectangle;
            if (r.IsEmpty || e.Current.IsOffscreen) return false;
            int x = (int)(r.Left + r.Width / 2), y = (int)(r.Top + r.Height / 2);
            // Only click HRD itself - not a window on top of it (Callsign Lookup
            // with Stay on top, say).
            if (Mouse.ProcessAt(x, y) != e.Current.ProcessId) return false;
            Mouse.Click(x, y);
            Thread.Sleep(300);

            // Clicking a closed list opens it; close it again, keeping its choice.
            try
            {
                if (e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object p) &&
                    ((ExpandCollapsePattern)p).Current.ExpandCollapseState == ExpandCollapseState.Expanded)
                {
                    ((ExpandCollapsePattern)p).Collapse();
                    Thread.Sleep(300);
                }
            }
            catch (InvalidOperationException)
            {
            }
            return HasFocus(e);
        }

        // Whether the keyboard focus is on the control or inside it (an editable
        // list's own text box).
        private static bool HasFocus(AutomationElement e)
        {
            try
            {
                var focused = AutomationElement.FocusedElement;
                for (int depth = 0; focused != null && depth < 4; depth++)
                {
                    if (Automation.Compare(focused, e)) return true;
                    focused = TreeWalker.ControlViewWalker.GetParent(focused);
                }
            }
            catch (ElementNotAvailableException)
            {
            }
            return false;
        }

        public void Press(string id)
        {
            var e = Control(id) ?? throw new HrdException($"Can't find HRD's {id} button.");
            ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Thread.Sleep(700);
            Refresh();
        }

        // Presses Update (F7) - "Add (F7)" in an Add window - which saves the
        // QSO and closes the window. True once the window has gone; false if
        // it's still open after a few seconds (HRD asking something, say).
        public bool PressUpdateAndWait()
        {
            Press("btnUpdate");
            for (int i = 0; i < 10; i++)
            {
                if (!IsOpen || Find() == null) return true;
                Thread.Sleep(300);
            }
            return false;
        }

        // ---- My Station profiles -------------------------------------------

        // Selects a profile in the My Station tab's list, which fills the My
        // Station fields from it. The My Station tab must be showing.
        public bool SelectProfile(string displayName)
        {
            var items = _window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem));
            foreach (AutomationElement item in items)
            {
                if (!item.Current.Name.Trim().Equals(displayName, StringComparison.OrdinalIgnoreCase)) continue;
                ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                Thread.Sleep(600);
                Refresh();
                return true;
            }
            return false;
        }

        // Every text box and drop-down on the tab that's showing, by id.
        public Dictionary<string, string> ReadAll()
        {
            var values = new Dictionary<string, string>();
            _controls ??= LoadControls();
            foreach (var (id, e) in _controls)
            {
                try
                {
                    var type = e.Current.ControlType;
                    if (type != ControlType.Edit && type != ControlType.ComboBox) continue;
                    if (e.TryGetCurrentPattern(ValuePattern.Pattern, out object p))
                        values[id] = ((ValuePattern)p).Current.Value.Trim();
                }
                catch (ElementNotAvailableException)
                {
                }
            }
            return values;
        }

        // Clicking an HRD control when SetFocus can't reach it. The pointer is
        // put back where it was afterwards.
        private static class Mouse
        {
            private const uint LeftDown = 0x0002, LeftUp = 0x0004;

            public static void Click(int x, int y)
            {
                GetCursorPos(out POINT before);
                SetCursorPos(x, y);
                Thread.Sleep(50);
                mouse_event(LeftDown, 0, 0, 0, IntPtr.Zero);
                mouse_event(LeftUp, 0, 0, 0, IntPtr.Zero);
                Thread.Sleep(50);
                SetCursorPos(before.X, before.Y);
            }

            // The process whose window is at that point on the screen.
            public static int ProcessAt(int x, int y)
            {
                IntPtr window = WindowFromPoint(new POINT { X = x, Y = y });
                if (window == IntPtr.Zero) return 0;
                GetWindowThreadProcessId(window, out uint pid);
                return (int)pid;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct POINT
            {
                public int X;
                public int Y;
            }

            [DllImport("user32.dll")]
            private static extern bool GetCursorPos(out POINT point);

            [DllImport("user32.dll")]
            private static extern bool SetCursorPos(int x, int y);

            [DllImport("user32.dll")]
            private static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extraInfo);

            [DllImport("user32.dll")]
            private static extern IntPtr WindowFromPoint(POINT point);

            [DllImport("user32.dll")]
            private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        }

        // Typing into HRD: SendInput to whatever has the keyboard focus, which
        // FocusForTyping has just checked is the HRD control.
        private static class Keyboard
        {
            public const ushort Control = 0x11, Delete = 0x2E, Enter = 0x0D;

            public static void Type(string text)
            {
                var inputs = new List<INPUT>();
                foreach (char c in text)
                {
                    inputs.Add(Unicode(c, up: false));
                    inputs.Add(Unicode(c, up: true));
                }
                Send(inputs);
            }

            public static void Press(ushort key) => Send([Key(key, false), Key(key, true)]);

            // A key with a modifier held - e.g. Ctrl+A.
            public static void Press(ushort modifier, char key) =>
                Send([Key(modifier, false), Key(key, false), Key(key, true), Key(modifier, true)]);

            private static INPUT Key(ushort vk, bool up) => new()
            {
                type = 1,
                ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u },
            };

            private static INPUT Unicode(char c, bool up) => new()
            {
                type = 1,
                ki = new KEYBDINPUT { wScan = c, dwFlags = 4u | (up ? 2u : 0u) },
            };

            private static void Send(List<INPUT> inputs)
            {
                var array = inputs.ToArray();
                SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct KEYBDINPUT
            {
                public ushort wVk;
                public ushort wScan;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            // The union's other members (mouse input) are bigger than the
            // keyboard one; the padding keeps INPUT the size Windows expects.
            [StructLayout(LayoutKind.Sequential)]
            private struct INPUT
            {
                public uint type;
                public KEYBDINPUT ki;
                private readonly long _padding;
            }

            [DllImport("user32.dll", SetLastError = true)]
            private static extern uint SendInput(uint count, INPUT[] inputs, int size);
        }
    }

    public sealed class HrdException(string message) : Exception(message);
}
