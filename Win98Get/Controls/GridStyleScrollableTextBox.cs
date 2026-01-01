using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Win98Get.Controls;

public sealed class GridStyleScrollableTextBox : UserControl
{
    private readonly Panel _border;
    private readonly ScrollAwareTextBox _text = new();
    private readonly VScrollBar _scroll = new();

    private static readonly object _filterGate = new();
    private static MouseWheelMessageFilter? _wheelFilter;

    private bool _syncing;

    public GridStyleScrollableTextBox()
    {
        EnsureWheelFilterInstalled();

        _border = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.Fixed3D,
            BackColor = SystemColors.Window,
            Padding = new Padding(2),
        };

        _text.BorderStyle = BorderStyle.None;
        _text.Multiline = true;
        _text.ScrollBars = ScrollBars.None;
        _text.Dock = DockStyle.Fill;
        _text.BackColor = SystemColors.Window;
        _text.RequestWheelScroll += HandleWheelDelta;
        _text.TextChanged += (_, _) => SyncFromText();
        _text.Scrolled += (_, _) => SyncFromText();
        _text.Resize += (_, _) => SyncFromText();
        _text.FontChanged += (_, _) => SyncFromText();

        _scroll.Dock = DockStyle.Right;
        _scroll.Width = SystemInformation.VerticalScrollBarWidth;
        _scroll.Minimum = 0;
        _scroll.ValueChanged += (_, _) => SyncToText();

        _border.Controls.Add(_text);
        _border.Controls.Add(_scroll);

        Controls.Add(_border);

        BackColor = SystemColors.Control;
        Resize += (_, _) => SyncFromText();

        Disposed += (_, _) => _wheelFilter?.Unregister(this);
        _wheelFilter?.Register(this);
    }

    public void SetBorder(BorderStyle borderStyle, Padding padding)
    {
        _border.BorderStyle = borderStyle;
        _border.Padding = padding;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ReadOnly
    {
        get => _text.ReadOnly;
        set => _text.ReadOnly = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool WordWrap
    {
        get => _text.WordWrap;
        set
        {
            _text.WordWrap = value;
            SyncFromText();
        }
    }

    [AllowNull]
    public override string Text
    {
        get => _text.Text;
        set
        {
            _text.Text = value ?? string.Empty;
            SyncFromText();
        }
    }

    public void AppendText(string text)
    {
        _text.AppendText(text);
        SyncFromText();
    }

    public void Clear()
    {
        _text.Clear();
        SyncFromText();
    }

    [AllowNull]
    public override Font Font
    {
        get => _text.Font;
        set
        {
            _text.Font = value ?? Control.DefaultFont;
            SyncFromText();
        }
    }

    public override Color BackColor
    {
        get => _text.BackColor;
        set
        {
            base.BackColor = value;
            _text.BackColor = value;
        }
    }

    private void SyncFromText()
    {
        if (_syncing || !_text.IsHandleCreated)
        {
            return;
        }

        _syncing = true;
        try
        {
            var lineCount = NativeMethods.GetLineCount(_text);
            var firstVisible = NativeMethods.GetFirstVisibleLine(_text);
            var visibleLines = Math.Max(1, _text.ClientSize.Height / Math.Max(1, _text.Font.Height));

            _scroll.LargeChange = visibleLines;
            _scroll.SmallChange = 1;

            // Windows scrollbars use Maximum as the top end plus LargeChange-1.
            // Keep it simple: clamp Value into [0..maxLine].
            var maxLine = Math.Max(0, lineCount - 1);
            _scroll.Maximum = maxLine;

            var v = Math.Max(0, Math.Min(firstVisible, maxLine));
            if (_scroll.Value != v)
            {
                _scroll.Value = v;
            }

            _scroll.Enabled = lineCount > visibleLines;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncToText()
    {
        if (_syncing || !_text.IsHandleCreated)
        {
            return;
        }

        _syncing = true;
        try
        {
            var current = NativeMethods.GetFirstVisibleLine(_text);
            var delta = _scroll.Value - current;
            if (delta != 0)
            {
                NativeMethods.LineScroll(_text, delta);
            }
        }
        finally
        {
            _syncing = false;
            SyncFromText();
        }
    }

    private void HandleWheelDelta(int wheelDelta)
    {
        if (!_text.IsHandleCreated)
        {
            return;
        }

        // Positive delta => wheel up => scroll up.
        var notches = wheelDelta / SystemInformation.MouseWheelScrollDelta;
        if (notches == 0)
        {
            return;
        }

        var scrollLines = SystemInformation.MouseWheelScrollLines;
        var visibleLines = Math.Max(1, _text.ClientSize.Height / Math.Max(1, _text.Font.Height));
        var lines = scrollLines == -1
            ? notches * visibleLines
            : notches * scrollLines;

        if (lines != 0)
        {
            NativeMethods.LineScroll(_text, -lines);
            SyncFromText();
        }
    }

    private sealed class ScrollAwareTextBox : TextBox
    {
        public event EventHandler? Scrolled;

        public event Action<int>? RequestWheelScroll;

        protected override void WndProc(ref Message m)
        {
            const int WM_VSCROLL = 0x0115;
            const int WM_MOUSEWHEEL = 0x020A;
            const int WM_KEYDOWN = 0x0100;

            if (m.Msg == WM_MOUSEWHEEL)
            {
                // Multiline TextBox without WS_VSCROLL won't scroll on wheel by itself.
                // Ask the parent control to scroll and swallow the message.
                var delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                RequestWheelScroll?.Invoke(delta);
                Scrolled?.Invoke(this, EventArgs.Empty);
                m.Result = 0;
                return;
            }

            base.WndProc(ref m);

            if (m.Msg is WM_VSCROLL or WM_MOUSEWHEEL or WM_KEYDOWN)
            {
                Scrolled?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static void EnsureWheelFilterInstalled()
    {
        if (_wheelFilter is not null)
        {
            return;
        }

        lock (_filterGate)
        {
            if (_wheelFilter is not null)
            {
                return;
            }

            _wheelFilter = new MouseWheelMessageFilter();
            Application.AddMessageFilter(_wheelFilter);
        }
    }

    private sealed class MouseWheelMessageFilter : IMessageFilter
    {
        private readonly List<WeakReference<GridStyleScrollableTextBox>> _owners = new();

        public void Register(GridStyleScrollableTextBox owner)
        {
            lock (_owners)
            {
                _owners.Add(new WeakReference<GridStyleScrollableTextBox>(owner));
            }
        }

        public void Unregister(GridStyleScrollableTextBox owner)
        {
            lock (_owners)
            {
                _owners.RemoveAll(wr => !wr.TryGetTarget(out var t) || ReferenceEquals(t, owner));
            }
        }

        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg != WM_MOUSEWHEEL)
            {
                return false;
            }

            // Redirect wheel to the GridStyleScrollableTextBox under the cursor.
            if (!NativeMethods.TryGetControlUnderCursor(out var ctrl) || ctrl is null)
            {
                return false;
            }

            var owner = FindOwner(ctrl);
            if (owner is null || owner.IsDisposed)
            {
                return false;
            }

            // Ensure this owner is one we registered (avoid global side effects).
            if (!IsRegistered(owner))
            {
                return false;
            }

            var delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            owner.HandleWheelDelta(delta);
            return true;
        }

        private bool IsRegistered(GridStyleScrollableTextBox owner)
        {
            lock (_owners)
            {
                _owners.RemoveAll(wr => !wr.TryGetTarget(out _));
                foreach (var wr in _owners)
                {
                    if (wr.TryGetTarget(out var t) && ReferenceEquals(t, owner))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static GridStyleScrollableTextBox? FindOwner(Control ctrl)
        {
            for (Control? c = ctrl; c is not null; c = c.Parent)
            {
                if (c is GridStyleScrollableTextBox gstb)
                {
                    return gstb;
                }
            }

            return null;
        }
    }

    private static class NativeMethods
    {
        private const int GA_ROOT = 2;
        private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        private const int EM_LINESCROLL = 0x00B6;
        private const int EM_GETLINECOUNT = 0x00BA;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern nint WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern nint GetAncestor(nint hwnd, int gaFlags);

        public static bool TryGetControlUnderCursor(out Control? control)
        {
            control = null;
            if (!GetCursorPos(out var p))
            {
                return false;
            }

            var hwnd = WindowFromPoint(p);
            if (hwnd == 0)
            {
                return false;
            }

            // Normalize child handles back to a WinForms control handle.
            var root = GetAncestor(hwnd, GA_ROOT);
            var c = Control.FromHandle(hwnd) ?? Control.FromHandle(root);
            if (c is null)
            {
                return false;
            }

            control = c;
            return true;
        }

        public static int GetFirstVisibleLine(Control c)
            => (int)SendMessage(c.Handle, EM_GETFIRSTVISIBLELINE, 0, 0);

        public static int GetLineCount(Control c)
            => (int)SendMessage(c.Handle, EM_GETLINECOUNT, 0, 0);

        public static void LineScroll(Control c, int deltaLines)
            => _ = SendMessage(c.Handle, EM_LINESCROLL, 0, deltaLines);
    }
}
