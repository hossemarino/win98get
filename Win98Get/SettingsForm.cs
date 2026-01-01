using Win98Get.Controls;
using Win98Get.Services;

namespace Win98Get;

public sealed class SettingsForm : Form
{
    private readonly Dictionary<CheckBox, WingetFlag> _checkToFlag = new();
    private readonly Dictionary<(WingetOperation Op, string Group), List<CheckBox>> _exclusiveGroups = new();

    private readonly Button _ok = new() { Text = "OK", DialogResult = DialogResult.OK };
    private readonly Button _cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };

    public SettingsForm()
    {
        Text = "Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Width = 720,
            Height = 420,
        };

        tabs.TabPages.Add(BuildFlagsTab("Install", WingetOperation.Install));
        tabs.TabPages.Add(BuildFlagsTab("Update", WingetOperation.Upgrade));
        tabs.TabPages.Add(BuildFlagsTab("Uninstall", WingetOperation.Uninstall));

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.Add(_ok);
        buttons.Controls.Add(_cancel);

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(tabs, 0, 0);
        root.Controls.Add(buttons, 0, 1);

        Controls.Add(root);

        AcceptButton = _ok;
        CancelButton = _cancel;

        FormClosing += (_, _) =>
        {
            if (DialogResult != DialogResult.OK)
            {
                return;
            }

            PersistSelections();
        };
    }

    private TabPage BuildFlagsTab(string title, WingetOperation op)
    {
        var selected = AppSettings.GetSelectedFlagKeys(op);
        var flags = WingetFlagCatalog.GetFlags(op);

        var byCategory = flags
            .GroupBy(f => f.Category)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6),
        };

        foreach (var cat in byCategory)
        {
            var group = new GroupBox
            {
                Text = cat.Key,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Width = 660,
                Padding = new Padding(10),
            };

            var inner = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };

            foreach (var flag in cat.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var cb = new CheckBox
                {
                    AutoSize = true,
                    Text = $"{flag.DisplayName}   {flag.Argument}",
                    Checked = selected.Contains(flag.Key) || flag.DefaultOn,
                };

                _checkToFlag[cb] = flag;

                if (!string.IsNullOrWhiteSpace(flag.ExclusiveGroup))
                {
                    var k = (op, flag.ExclusiveGroup!);
                    if (!_exclusiveGroups.TryGetValue(k, out var list))
                    {
                        list = new List<CheckBox>();
                        _exclusiveGroups[k] = list;
                    }
                    list.Add(cb);

                    cb.CheckedChanged += (_, _) =>
                    {
                        if (!cb.Checked)
                        {
                            return;
                        }

                        // Uncheck other options in the same exclusive group.
                        foreach (var other in _exclusiveGroups[k])
                        {
                            if (ReferenceEquals(other, cb))
                            {
                                continue;
                            }
                            if (!other.Enabled)
                            {
                                continue;
                            }
                            other.Checked = false;
                        }
                    };
                }

                inner.Controls.Add(cb);
            }

            group.Controls.Add(inner);
            flow.Controls.Add(group);
        }

        var scroll = new GridStyleScrollablePanel
        {
            Dock = DockStyle.Fill,
        };
        scroll.SetBorder(BorderStyle.Fixed3D, new Padding(2));
        scroll.Content = flow;

        var page = new TabPage(title);
        page.Controls.Add(scroll);
        return page;
    }

    private void PersistSelections()
    {
        PersistFor(WingetOperation.Install);
        PersistFor(WingetOperation.Upgrade);
        PersistFor(WingetOperation.Uninstall);
    }

    private void PersistFor(WingetOperation op)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _checkToFlag)
        {
            var cb = kvp.Key;
            var flag = kvp.Value;
            if (!cb.Checked)
            {
                continue;
            }

            // Determine which operation tab this flag belongs to by key prefix.
            // This keeps SettingsForm simple without maintaining separate maps.
            var belongs = op switch
            {
                WingetOperation.Install => flag.Key.StartsWith("install.", StringComparison.OrdinalIgnoreCase),
                WingetOperation.Upgrade => flag.Key.StartsWith("upgrade.", StringComparison.OrdinalIgnoreCase),
                WingetOperation.Uninstall => flag.Key.StartsWith("uninstall.", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };

            if (!belongs)
            {
                continue;
            }

            keys.Add(flag.Key);
        }

        AppSettings.SetSelectedFlagKeys(op, keys);
    }
}
