using System.Text.RegularExpressions;

namespace Win98Get;

public sealed class OperationForm : Form
{
    private static readonly Regex PercentRegex = new(@"(?<!\d)(\d{1,3})%", RegexOptions.Compiled);

    private readonly Label _opValue = new() { AutoSize = true };
    private readonly Label _nameValue = new() { AutoSize = true };
    private readonly Label _idValue = new() { AutoSize = true };
    private readonly Label _versionValue = new() { AutoSize = true };
    private readonly Label _flagsValue = new() { AutoSize = true };
    private readonly Label _statusValue = new() { AutoSize = true, Text = "Loading" };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };

    private bool _downloadSeen;

    public OperationForm()
    {
        Text = "Operation";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Microsoft Sans Serif", 8.25f);
        BackColor = SystemColors.Control;
        ClientSize = new Size(520, 180);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 2,
            RowCount = 7,
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddRow(grid, 0, "Operation:", _opValue);
        AddRow(grid, 1, "Name:", _nameValue);
        AddRow(grid, 2, "Id:", _idValue);
        AddRow(grid, 3, "Version:", _versionValue);
        AddRow(grid, 4, "Flags:", _flagsValue);
        AddRow(grid, 5, "Status:", _statusValue);

        grid.Controls.Add(_progress, 0, 6);
        grid.SetColumnSpan(_progress, 2);

        Controls.Add(grid);
    }

    public void SetOperation(string operation, string name, string id, string version, string flags)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => SetOperation(operation, name, id, version, flags));
            return;
        }

        _downloadSeen = false;
        _opValue.Text = operation;
        _nameValue.Text = name;
        _idValue.Text = id;
        _versionValue.Text = version;
        _flagsValue.Text = string.IsNullOrWhiteSpace(flags) ? "(none)" : flags.Trim();
        _statusValue.Text = "Loading";

        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 30;
    }

    public void OnOutputLine(string line)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => OnOutputLine(line));
            return;
        }

        var text = (line ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        var match = PercentRegex.Match(text);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
        {
            percent = Math.Clamp(percent, 0, 100);
            _downloadSeen = true;
            _statusValue.Text = "Downloading";
            if (_progress.Style != ProgressBarStyle.Continuous)
            {
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.MarqueeAnimationSpeed = 0;
                _progress.Minimum = 0;
                _progress.Maximum = 100;
            }
            _progress.Value = percent;
            return;
        }

        if (!_downloadSeen)
        {
            if (text.Contains("Downloading", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Download", StringComparison.OrdinalIgnoreCase))
            {
                _downloadSeen = true;
                _statusValue.Text = "Downloading";
                _progress.Style = ProgressBarStyle.Marquee;
                _progress.MarqueeAnimationSpeed = 30;
                return;
            }
        }

        if (text.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("successfully", StringComparison.OrdinalIgnoreCase))
        {
            _statusValue.Text = "Done";
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.MarqueeAnimationSpeed = 0;
            _progress.Value = 100;
            return;
        }

        if (text.Contains("Installing", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Starting package", StringComparison.OrdinalIgnoreCase))
        {
            _statusValue.Text = "Installing";
            return;
        }

        if (text.Contains("Uninstalling", StringComparison.OrdinalIgnoreCase))
        {
            _statusValue.Text = "Uninstalling";
            return;
        }

        // Keep the last interesting status line.
        _statusValue.Text = text.Length > 80 ? text.Substring(0, 80) + "…" : text;
    }

    private static void AddRow(TableLayoutPanel grid, int row, string labelText, Control value)
    {
        var label = new Label
        {
            AutoSize = true,
            Text = labelText,
            Margin = new Padding(0, 0, 10, 6),
        };
        value.Margin = new Padding(0, 0, 0, 6);

        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(value, 1, row);
    }
}
