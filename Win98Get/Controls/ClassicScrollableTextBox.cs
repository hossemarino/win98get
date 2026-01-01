using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Win98Get.Controls;

public sealed class ClassicScrollableTextBox : UserControl
{
    private readonly ScrollAwareTextBox _text = new();
    private readonly ClassicVScrollBar _scroll = new();

    private bool _syncing;

    public ClassicScrollableTextBox()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        BackColor = SystemColors.Window;

        _text.BorderStyle = BorderStyle.None;
        _text.Multiline = true;
        _text.ReadOnly = true;
        _text.ScrollBars = ScrollBars.None;
        _text.WordWrap = false;
        _text.Dock = DockStyle.Fill;
        _text.BackColor = SystemColors.Window;
        _text.Scrolled += (_, _) => SyncFromText();
        _text.TextChanged += (_, _) => SyncFromText();
        _text.FontChanged += (_, _) => SyncFromText();
        _text.Resize += (_, _) => SyncFromText();

        _scroll.Dock = DockStyle.Right;
        _scroll.Width = SystemInformation.VerticalScrollBarWidth;
        _scroll.ValueChanged += (_, _) => SyncToText();

        Controls.Add(_text);
        Controls.Add(_scroll);

        Padding = new Padding(2);

        Resize += (_, _) => SyncFromText();
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
            Invalidate();
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Win98-ish 3D border.
        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        using var light = new Pen(SystemColors.ControlLightLight);
        using var dark = new Pen(SystemColors.ControlDark);

        e.Graphics.DrawLine(light, rect.Left, rect.Top, rect.Right, rect.Top);
        e.Graphics.DrawLine(light, rect.Left, rect.Top, rect.Left, rect.Bottom);
        e.Graphics.DrawLine(dark, rect.Left, rect.Bottom, rect.Right, rect.Bottom);
        e.Graphics.DrawLine(dark, rect.Right, rect.Top, rect.Right, rect.Bottom);
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
            _scroll.Maximum = Math.Max(0, lineCount - 1);
            _scroll.Value = Math.Max(0, Math.Min(firstVisible, _scroll.Maximum));
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

    private sealed class ScrollAwareTextBox : TextBox
    {
        public event EventHandler? Scrolled;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            const int WM_VSCROLL = 0x0115;
            const int WM_MOUSEWHEEL = 0x020A;
            const int WM_KEYDOWN = 0x0100;
            const int WM_KEYUP = 0x0101;

            if (m.Msg is WM_VSCROLL or WM_MOUSEWHEEL or WM_KEYDOWN or WM_KEYUP)
            {
                Scrolled?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static class NativeMethods
    {
        private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        private const int EM_LINESCROLL = 0x00B6;
        private const int EM_GETLINECOUNT = 0x00BA;

        [DllImport("user32.dll")]
        private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

        public static int GetFirstVisibleLine(Control c)
            => (int)SendMessage(c.Handle, EM_GETFIRSTVISIBLELINE, 0, 0);

        public static int GetLineCount(Control c)
            => (int)SendMessage(c.Handle, EM_GETLINECOUNT, 0, 0);

        public static void LineScroll(Control c, int deltaLines)
            => _ = SendMessage(c.Handle, EM_LINESCROLL, 0, deltaLines);
    }
}

internal sealed class ClassicVScrollBar : Control
{
    public event EventHandler? ValueChanged;

    private int _minimum;
    private int _maximum;
    private int _largeChange = 10;
    private int _value;

    private bool _dragging;
    private int _dragOffsetY;

    public ClassicVScrollBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Width = SystemInformation.VerticalScrollBarWidth;
        BackColor = SystemColors.Control;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < _minimum) _maximum = _minimum;
            Value = _value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum);
            Value = _value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LargeChange
    {
        get => _largeChange;
        set
        {
            _largeChange = Math.Max(1, value);
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Max(_minimum, Math.Min(value, _maximum));
            if (clamped == _value) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        var rect = ClientRectangle;

        using (var bg = new SolidBrush(SystemColors.Control))
        {
            g.FillRectangle(bg, rect);
        }

        Draw3DBorder(g, rect);

        var btnH = Math.Min(16, rect.Height / 2);
        var topBtn = new Rectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, btnH);
        var bottomBtn = new Rectangle(rect.Left + 1, rect.Bottom - 1 - btnH, rect.Width - 2, btnH);

        DrawButton(g, topBtn, ArrowDirection.Up);
        DrawButton(g, bottomBtn, ArrowDirection.Down);

        var track = Rectangle.FromLTRB(rect.Left + 1, topBtn.Bottom + 1, rect.Right - 1, bottomBtn.Top - 1);
        DrawTrackAndThumb(g, track);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!Enabled) return;

        var btnH = Math.Min(16, Height / 2);
        var topBtn = new Rectangle(1, 1, Width - 2, btnH);
        var bottomBtn = new Rectangle(1, Height - 1 - btnH, Width - 2, btnH);
        var track = Rectangle.FromLTRB(1, topBtn.Bottom + 1, Width - 1, bottomBtn.Top - 1);

        if (topBtn.Contains(e.Location))
        {
            Value -= 1;
            return;
        }

        if (bottomBtn.Contains(e.Location))
        {
            Value += 1;
            return;
        }

        var thumb = GetThumbRect(track);
        if (thumb.Contains(e.Location))
        {
            _dragging = true;
            _dragOffsetY = e.Y - thumb.Top;
            Capture = true;
            return;
        }

        if (track.Contains(e.Location))
        {
            if (e.Y < thumb.Top)
            {
                Value -= LargeChange;
            }
            else if (e.Y > thumb.Bottom)
            {
                Value += LargeChange;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_dragging || !Enabled) return;

        var btnH = Math.Min(16, Height / 2);
        var topBtn = new Rectangle(1, 1, Width - 2, btnH);
        var bottomBtn = new Rectangle(1, Height - 1 - btnH, Width - 2, btnH);
        var track = Rectangle.FromLTRB(1, topBtn.Bottom + 1, Width - 1, bottomBtn.Top - 1);

        var thumb = GetThumbRect(track);
        var newTop = e.Y - _dragOffsetY;
        var minTop = track.Top;
        var maxTop = track.Bottom - thumb.Height;
        newTop = Math.Max(minTop, Math.Min(newTop, maxTop));

        var range = Math.Max(1, Maximum - Minimum);
        var trackSpan = Math.Max(1, track.Height - thumb.Height);
        var t = (double)(newTop - track.Top) / trackSpan;
        Value = Minimum + (int)Math.Round(t * range);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
        }
    }

    private void DrawTrackAndThumb(Graphics g, Rectangle track)
    {
        using (var bg = new SolidBrush(SystemColors.Control))
        {
            g.FillRectangle(bg, track);
        }

        var thumb = GetThumbRect(track);
        DrawButton(g, thumb, null);
    }

    private Rectangle GetThumbRect(Rectangle track)
    {
        if (track.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var range = Math.Max(1, Maximum - Minimum);
        var thumbMin = 12;
        var thumbH = Math.Max(thumbMin, (int)Math.Round(track.Height * (LargeChange / (double)(range + LargeChange))));
        thumbH = Math.Min(track.Height, thumbH);

        var trackSpan = Math.Max(1, track.Height - thumbH);
        var t = range == 0 ? 0 : (Value - Minimum) / (double)range;
        var y = track.Top + (int)Math.Round(t * trackSpan);

        return new Rectangle(track.Left, y, track.Width, thumbH);
    }

    private static void Draw3DBorder(Graphics g, Rectangle r)
    {
        var rect = r;
        rect.Width -= 1;
        rect.Height -= 1;

        using var light = new Pen(SystemColors.ControlLightLight);
        using var dark = new Pen(SystemColors.ControlDark);

        g.DrawLine(light, rect.Left, rect.Top, rect.Right, rect.Top);
        g.DrawLine(light, rect.Left, rect.Top, rect.Left, rect.Bottom);
        g.DrawLine(dark, rect.Left, rect.Bottom, rect.Right, rect.Bottom);
        g.DrawLine(dark, rect.Right, rect.Top, rect.Right, rect.Bottom);
    }

    private static void DrawButton(Graphics g, Rectangle r, ArrowDirection? arrow)
    {
        using (var bg = new SolidBrush(SystemColors.Control))
        {
            g.FillRectangle(bg, r);
        }

        Draw3DBorder(g, r);

        if (arrow is null)
        {
            return;
        }

        var cx = r.Left + (r.Width / 2);
        var cy = r.Top + (r.Height / 2);

        Point[] tri = arrow switch
        {
            ArrowDirection.Up =>
            [
                new Point(cx, cy - 3),
                new Point(cx - 4, cy + 2),
                new Point(cx + 4, cy + 2),
            ],
            ArrowDirection.Down =>
            [
                new Point(cx - 4, cy - 2),
                new Point(cx + 4, cy - 2),
                new Point(cx, cy + 3),
            ],
            _ =>
            [
                new Point(cx, cy - 3),
                new Point(cx - 4, cy + 2),
                new Point(cx + 4, cy + 2),
            ],
        };

        using var fill = new SolidBrush(SystemColors.ControlText);
        g.FillPolygon(fill, tri);
    }

    private enum ArrowDirection
    {
        Up,
        Down,
    }
}
