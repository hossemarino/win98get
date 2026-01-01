using System.ComponentModel;

namespace Win98Get.Controls;

public sealed class GridStyleScrollablePanel : UserControl
{
    private readonly Panel _border;
    private readonly Panel _viewport;
    private readonly VScrollBar _scroll;

    private Control? _content;
    private bool _syncing;

    public GridStyleScrollablePanel()
    {
        _border = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.Fixed3D,
            BackColor = SystemColors.Control,
            Padding = new Padding(2),
        };

        _scroll = new VScrollBar
        {
            Dock = DockStyle.Right,
            Width = SystemInformation.VerticalScrollBarWidth,
            Minimum = 0,
        };
        _scroll.ValueChanged += (_, _) => SyncToContent();

        _viewport = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Control,
        };
        _viewport.Resize += (_, _) => SyncFromContent();
        _viewport.MouseWheel += (_, e) => HandleWheel(e.Delta);

        _border.Controls.Add(_viewport);
        _border.Controls.Add(_scroll);
        Controls.Add(_border);

        BackColor = SystemColors.Control;
        Resize += (_, _) => SyncFromContent();
    }

    public void SetBorder(BorderStyle borderStyle, Padding padding)
    {
        _border.BorderStyle = borderStyle;
        _border.Padding = padding;
        SyncFromContent();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value))
            {
                return;
            }

            if (_content is not null)
            {
                _content.SizeChanged -= Content_SizeChanged;
                _content.MouseWheel -= Content_MouseWheel;
                _viewport.Controls.Remove(_content);
            }

            _content = value;
            if (_content is not null)
            {
                _content.Location = new Point(0, 0);
                _content.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                _content.Width = _viewport.ClientSize.Width;
                _content.SizeChanged += Content_SizeChanged;
                _content.MouseWheel += Content_MouseWheel;
                _viewport.Controls.Add(_content);
            }

            SyncFromContent();
        }
    }

    private void Content_SizeChanged(object? sender, EventArgs e) => SyncFromContent();

    private void Content_MouseWheel(object? sender, MouseEventArgs e) => HandleWheel(e.Delta);

    private void HandleWheel(int wheelDelta)
    {
        if (!_scroll.Enabled)
        {
            return;
        }

        var notches = wheelDelta / SystemInformation.MouseWheelScrollDelta;
        if (notches == 0)
        {
            return;
        }

        var lines = SystemInformation.MouseWheelScrollLines;
        var delta = lines == -1 ? notches * _scroll.LargeChange : notches * lines * 16;

        var maxValue = GetMaxScrollValue();
        var next = Math.Clamp(_scroll.Value - delta, 0, maxValue);
        if (_scroll.Value != next)
        {
            _scroll.Value = next;
        }
    }

    private int GetMaxScrollValue()
    {
        var maxScroll = Math.Max(0, (_content?.Height ?? 0) - _viewport.ClientSize.Height);
        return maxScroll;
    }

    private void SyncFromContent()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            if (_content is null)
            {
                _scroll.Enabled = false;
                _scroll.Value = 0;
                return;
            }

            _content.Width = _viewport.ClientSize.Width;

            var maxScroll = GetMaxScrollValue();

            _scroll.LargeChange = Math.Max(1, _viewport.ClientSize.Height);
            _scroll.SmallChange = 16;

            // Win32 scrollbar semantics: Maximum is the end plus LargeChange-1.
            _scroll.Maximum = maxScroll + _scroll.LargeChange - 1;
            _scroll.Enabled = maxScroll > 0;

            var v = Math.Clamp(_scroll.Value, 0, maxScroll);
            if (_scroll.Value != v)
            {
                _scroll.Value = v;
            }

            _content.Top = -_scroll.Value;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncToContent()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            if (_content is null)
            {
                return;
            }

            var maxScroll = GetMaxScrollValue();
            var v = Math.Clamp(_scroll.Value, 0, maxScroll);
            if (_scroll.Value != v)
            {
                _scroll.Value = v;
            }

            _content.Top = -_scroll.Value;
        }
        finally
        {
            _syncing = false;
        }
    }
}
