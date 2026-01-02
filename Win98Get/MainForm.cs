using Win98Get.Models;
using Win98Get.Services;
using System.ComponentModel;
using Win98Get.Controls;
using System.Diagnostics;
using System.Security.Principal;

namespace Win98Get;

public sealed class MainForm : Form
{
    private const int CheckColumnIndex = 0;
    private readonly WingetService _winget = new();

    private readonly MenuStrip _menu = new();
    private readonly ToolStripMenuItem _fileMenu = new() { Text = "File" };
    private readonly ToolStripMenuItem _viewMenu = new() { Text = "View" };
    private readonly ToolStripMenuItem _helpMenu = new() { Text = "Help" };
    private readonly ToolStripMenuItem _fileRefresh = new() { Text = "Refresh" };
    private readonly ToolStripMenuItem _fileInstallAll = new() { Text = "Install all updates" };
    private readonly ToolStripMenuItem _fileExportPackages = new() { Text = "Export package list..." };
    private readonly ToolStripMenuItem _fileImportPackages = new() { Text = "Import package list..." };
    private readonly ToolStripMenuItem _fileSettings = new() { Text = "Settings..." };
    private readonly ToolStripMenuItem _fileRestartAdmin = new() { Text = "Restart as Administrator" };
    private readonly ToolStripMenuItem _fileQuit = new() { Text = "Quit" };
    private readonly ToolStripMenuItem _viewOutputLog = new() { Text = "Output Log", Checked = false, CheckOnClick = true };
    private readonly ToolStripMenuItem _themeMenu = new() { Text = "Theme" };
    private readonly ToolStripMenuItem _themeModern = new() { Text = "Modern" };
    private readonly ToolStripMenuItem _themeWin98 = new() { Text = "Windows 98" };
    private readonly ToolStripMenuItem _helpWingetOverview = new() { Text = "WinGet Overview (Microsoft Learn)" };
    private readonly ToolStripMenuItem _helpWingetTroubleshooting = new() { Text = "WinGet Troubleshooting (Microsoft Learn)" };
    private readonly ToolStripMenuItem _helpWingetExport = new() { Text = "WinGet Export (Microsoft Learn)" };
    private readonly ToolStripMenuItem _helpWingetImport = new() { Text = "WinGet Import (Microsoft Learn)" };
    private readonly ToolStripMenuItem _helpWingetRepair = new() { Text = "WinGet Repair (Microsoft Learn)" };
    private readonly ToolStripMenuItem _helpAbout = new() { Text = "About" };

    private readonly TabControl _tabs = new();
    private readonly TabPage _installedTab = new() { Text = "Installed" };
    private readonly TabPage _searchTab = new() { Text = "Search" };

    private readonly DataGridView _installedGrid = new();
    private readonly Button _refreshButton = new() { Text = "Refresh" };
    private readonly Button _updateButton = new() { Text = "Install Update" };
    private readonly Button _installAllUpdatesButton = new() { Text = "Install All" };
    private readonly Button _uninstallButton = new() { Text = "Uninstall" };
    private readonly Button _updateSelectedButton = new() { Text = "Update Selected" };
    private readonly Button _uninstallSelectedButton = new() { Text = "Uninstall Selected" };
    private readonly Button _cancelInstalledButton = new() { Text = "Cancel" };

    private readonly TextBox _searchBox = new();
    private readonly Button _searchButton = new() { Text = "Search" };
    private readonly Button _cancelSearchButton = new() { Text = "Cancel" };
    private readonly DataGridView _searchGrid = new();
    private readonly Button _installButton = new() { Text = "Install" };
    private readonly Button _installSelectedButton = new() { Text = "Install Selected" };
    private readonly GridStyleScrollableTextBox _descriptionBox = new();

    private readonly GridStyleScrollableTextBox _logBox = new();
    private readonly SplitContainer _mainSplit = new();

    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Text = "Ready" };
    private readonly ToolStripStatusLabel _statusSpring = new() { Spring = true };

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _descCts;

    private string? _installedSortProperty;
    private ListSortDirection _installedSortDirection = ListSortDirection.Ascending;
    private string? _searchSortProperty;
    private ListSortDirection _searchSortDirection = ListSortDirection.Ascending;

    private ThemeMode _theme = ThemeMode.Windows98;

    private readonly HashSet<string> _checkedInstalledIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _checkedSearchIds = new(StringComparer.OrdinalIgnoreCase);

    public MainForm()
    {
        Text = "Win98Get";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 480);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = true;
        MaximizeBox = true;

        Font = new Font("Microsoft Sans Serif", 8.25f);
        BackColor = SystemColors.Control;

        InitializeLayout();

        // Default to Windows 98 styling.
        SetTheme(ThemeMode.Windows98);

        // If already elevated, no need to offer restart.
        if (IsRunningAsAdministrator())
        {
            _fileRestartAdmin.Enabled = false;
            _fileRestartAdmin.Text = "Running as Administrator";
        }

        Shown += async (_, _) => await OnShownAsync();
    }

    private enum ThemeMode
    {
        Modern,
        Windows98,
    }

    private async Task OnShownAsync()
    {
        SetBusy(true, "Checking winget…");
        var (available, detail) = await _winget.CheckAvailableAsync(CancellationToken.None);
        if (!available)
        {
            SetBusy(false, "winget not found");
            DisableForMissingWinget();
            MessageBox.Show(
                this,
                $"Win98Get requires the Windows Package Manager (winget).\n\nDetails: {detail}\n\nInstall/enable winget and try again.",
                "Win98Get",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        AppendLog($"> {detail}");
        await RefreshInstalledAsync();
    }

    private void DisableForMissingWinget()
    {
        _refreshButton.Enabled = false;
        _updateButton.Enabled = false;
        _installAllUpdatesButton.Enabled = false;
        _uninstallButton.Enabled = false;
        _updateSelectedButton.Enabled = false;
        _uninstallSelectedButton.Enabled = false;
        _cancelInstalledButton.Enabled = false;
        _searchButton.Enabled = false;
        _cancelSearchButton.Enabled = false;
        _installButton.Enabled = false;
        _installSelectedButton.Enabled = false;

        _fileRefresh.Enabled = false;
        _fileInstallAll.Enabled = false;

        _installedGrid.Enabled = false;
        _searchGrid.Enabled = false;
        _searchBox.Enabled = false;
        _descriptionBox.Enabled = false;
    }

    private void InitializeLayout()
    {
        SuspendLayout();

        BuildMenu();

        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.Add(_installedTab);
        _tabs.TabPages.Add(_searchTab);

        _status.SizingGrip = true;
        _status.Items.Add(_statusLabel);
        _status.Items.Add(_statusSpring);
        _status.Dock = DockStyle.Bottom;

        BuildInstalledTab();
        BuildSearchTab();

        ConfigureLogBox();

        _mainSplit.Dock = DockStyle.Fill;
        _mainSplit.Orientation = Orientation.Horizontal;
        _mainSplit.SplitterWidth = 6;
        _mainSplit.Panel1MinSize = 260;
        _mainSplit.Panel2MinSize = 90;
        _mainSplit.SplitterDistance = 360;
        _mainSplit.Panel2Collapsed = true;
        _mainSplit.Panel1.Controls.Add(_tabs);
        _mainSplit.Panel2.Controls.Add(_logBox);

        Controls.Add(_mainSplit);
        Controls.Add(_menu);
        Controls.Add(_status);

        ResumeLayout();
    }

    private void BuildMenu()
    {
        _menu.Dock = DockStyle.Top;
        _menu.RenderMode = ToolStripRenderMode.System;

        _fileRefresh.Click += async (_, _) => await RefreshInstalledAsync();
        _fileInstallAll.Click += async (_, _) => await UpgradeAllAsync();
        _fileExportPackages.Click += async (_, _) => await ExportPackagesAsync();
        _fileImportPackages.Click += async (_, _) => await ImportPackagesAsync();
        _fileSettings.Click += (_, _) => ShowSettings();
        _fileRestartAdmin.Click += (_, _) => RestartAsAdministrator();
        _fileQuit.Click += (_, _) => Close();
        _helpWingetOverview.Click += (_, _) => OpenUrl("https://learn.microsoft.com/en-us/windows/package-manager/winget/");
        _helpWingetTroubleshooting.Click += (_, _) => OpenUrl("https://learn.microsoft.com/en-us/windows/package-manager/winget/troubleshooting");
        _helpWingetExport.Click += (_, _) => OpenUrl("https://aka.ms/winget-command-export");
        _helpWingetImport.Click += (_, _) => OpenUrl("https://aka.ms/winget-command-import");
        _helpWingetRepair.Click += (_, _) => OpenUrl("https://aka.ms/winget-command-repair");
        _helpAbout.Click += (_, _) => ShowAbout();

        _viewOutputLog.CheckedChanged += (_, _) => ToggleOutputLog(_viewOutputLog.Checked);

        _themeModern.Click += (_, _) => SetTheme(ThemeMode.Modern);
        _themeWin98.Click += (_, _) => SetTheme(ThemeMode.Windows98);

        _fileMenu.DropDownItems.Add(_fileRefresh);
        _fileMenu.DropDownItems.Add(_fileInstallAll);
        _fileMenu.DropDownItems.Add(new ToolStripSeparator());
        _fileMenu.DropDownItems.Add(_fileExportPackages);
        _fileMenu.DropDownItems.Add(_fileImportPackages);
        _fileMenu.DropDownItems.Add(new ToolStripSeparator());
        _fileMenu.DropDownItems.Add(_fileSettings);
        _fileMenu.DropDownItems.Add(_fileRestartAdmin);
        _fileMenu.DropDownItems.Add(new ToolStripSeparator());
        _fileMenu.DropDownItems.Add(_fileQuit);

        _viewMenu.DropDownItems.Add(_viewOutputLog);

        _themeMenu.DropDownItems.Add(_themeModern);
        _themeMenu.DropDownItems.Add(_themeWin98);

        _helpMenu.DropDownItems.Add(_helpWingetOverview);
        _helpMenu.DropDownItems.Add(_helpWingetTroubleshooting);
        _helpMenu.DropDownItems.Add(_helpWingetExport);
        _helpMenu.DropDownItems.Add(_helpWingetImport);
        _helpMenu.DropDownItems.Add(_helpWingetRepair);
        _helpMenu.DropDownItems.Add(new ToolStripSeparator());
        _helpMenu.DropDownItems.Add(_helpAbout);

        _menu.Items.Add(_fileMenu);
        _menu.Items.Add(_viewMenu);
        _menu.Items.Add(_themeMenu);
        _menu.Items.Add(_helpMenu);

        MainMenuStrip = _menu;
    }

    private async Task ExportPackagesAsync()
    {
        using var sfd = new SaveFileDialog
        {
            Title = "Export package list",
            Filter = "WinGet export (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            AddExtension = true,
            FileName = "winget-export.json",
            OverwritePrompt = true,
        };

        if (sfd.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(sfd.FileName))
        {
            return;
        }

        var outPath = sfd.FileName;
        var args = "-o \"" + outPath.Replace("\"", "\\\"") + "\" --accept-source-agreements";
        AppendLog($"> winget export {args}");

        await RunWingetActionWithOperationFormAsync(
            "Exporting package list…",
            () =>
            {
                var f = new OperationForm();
                f.SetOperation("Export", "(installed packages)", outPath, string.Empty, "--accept-source-agreements");
                return f;
            },
            (ct, form) => _winget.ExportStreamingAsync(
                outputPath: outPath,
                onOutputLine: line =>
                {
                    form.OnOutputLine(line);
                    AppendLogRaw(line);
                },
                cancellationToken: ct));
    }

    private async Task ImportPackagesAsync()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Import package list",
            Filter = "WinGet export (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true,
            CheckPathExists = true,
        };

        if (ofd.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(ofd.FileName))
        {
            return;
        }

        var inPath = ofd.FileName;
        var prompt = $"Import packages from file?\n\n{inPath}\n\nThis will install packages listed in the file. Proceed?";
        if (MessageBox.Show(this, prompt, "Win98Get", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var args = "-i \"" + inPath.Replace("\"", "\\\"") + "\" --accept-source-agreements --accept-package-agreements";
        AppendLog($"> winget import {args}");

        await RunWingetActionWithOperationFormAsync(
            "Importing package list…",
            () =>
            {
                var f = new OperationForm();
                f.SetOperation("Import", "(package list)", inPath, string.Empty, "--accept-source-agreements --accept-package-agreements");
                return f;
            },
            (ct, form) => _winget.ImportStreamingAsync(
                importFile: inPath,
                onOutputLine: line =>
                {
                    form.OnOutputLine(line);
                    AppendLogRaw(line);
                },
                cancellationToken: ct));
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // ignore
        }
    }

    private void SetTheme(ThemeMode theme)
    {
        _theme = theme;
        _themeModern.Checked = theme == ThemeMode.Modern;
        _themeWin98.Checked = theme == ThemeMode.Windows98;

        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (_theme == ThemeMode.Modern)
        {
            Font = SystemFonts.MessageBoxFont;

            ApplyModernGridTheme(_installedGrid);
            ApplyModernGridTheme(_searchGrid);

            _logBox.SetBorder(BorderStyle.FixedSingle, new Padding(1));
            _descriptionBox.SetBorder(BorderStyle.FixedSingle, new Padding(1));
        }
        else
        {
            Font = new Font("Microsoft Sans Serif", 8.25f);

            ApplyWin98GridTheme(_installedGrid);
            ApplyWin98GridTheme(_searchGrid);

            _logBox.SetBorder(BorderStyle.Fixed3D, new Padding(2));
            _descriptionBox.SetBorder(BorderStyle.Fixed3D, new Padding(2));
        }

        // Keep text areas consistent with form font.
        _logBox.Font = Font;
        _descriptionBox.Font = Font;
        _searchBox.Font = Font;

        // Repaint headers and update sort glyph behavior.
        ApplySortGlyph(_installedGrid, _installedSortProperty, _installedSortDirection);
        ApplySortGlyph(_searchGrid, _searchSortProperty, _searchSortDirection);
        _installedGrid.Invalidate();
        _searchGrid.Invalidate();
    }

    private static void ApplyModernGridTheme(DataGridView grid)
    {
        grid.BorderStyle = BorderStyle.FixedSingle;

        // Let the OS paint headers (Win11 style) and use built-in sort glyphs.
        grid.EnableHeadersVisualStyles = true;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Raised;

        // Remove Win98 padding reserved for the custom glyph.
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(0);
    }

    private static void ApplyWin98GridTheme(DataGridView grid)
    {
        grid.BorderStyle = BorderStyle.Fixed3D;

        // Win98-ish header styling and extra room for a custom sort glyph.
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(0, 0, 18, 0);

        // Only highlight the selected row, not the header “legend”.
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SystemColors.Control;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
    }

    private void BuildInstalledTab()
    {
        var topRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(6, 6, 6, 6),
            ColumnCount = 2,
            RowCount = 1,
        };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var leftButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        var rightButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        ApplyWin98ButtonStyle(_refreshButton);
        ApplyWin98ButtonStyle(_updateButton);
        ApplyWin98ButtonStyle(_installAllUpdatesButton);
        ApplyWin98ButtonStyle(_uninstallButton);
        ApplyWin98ButtonStyle(_updateSelectedButton);
        ApplyWin98ButtonStyle(_uninstallSelectedButton);
        ApplyWin98ButtonStyle(_cancelInstalledButton);
        _cancelInstalledButton.Enabled = false;
        _cancelInstalledButton.Dock = DockStyle.None;
        _cancelInstalledButton.Margin = new Padding(0);

        _refreshButton.Click += async (_, _) => await RefreshInstalledAsync();
        _updateButton.Click += async (_, _) => await UpgradeSelectedInstalledAsync();
        _installAllUpdatesButton.Click += async (_, _) => await UpgradeAllAsync();
        _uninstallButton.Click += async (_, _) => await UninstallSelectedInstalledAsync();
        _updateSelectedButton.Click += async (_, _) => await UpgradeCheckedInstalledAsync();
        _uninstallSelectedButton.Click += async (_, _) => await UninstallCheckedInstalledAsync();
        _cancelInstalledButton.Click += (_, _) => CancelCurrentWork();

        leftButtons.Controls.Add(_refreshButton);
        leftButtons.Controls.Add(_updateButton);
        leftButtons.Controls.Add(_installAllUpdatesButton);
        leftButtons.Controls.Add(_uninstallButton);
        leftButtons.Controls.Add(_updateSelectedButton);
        leftButtons.Controls.Add(_uninstallSelectedButton);
        rightButtons.Controls.Add(_cancelInstalledButton);

        topRow.Controls.Add(leftButtons, 0, 0);
        topRow.Controls.Add(rightButtons, 1, 0);

        _installedGrid.Dock = DockStyle.Fill;
        ConfigureGrid(_installedGrid);

        _installedGrid.Columns.Add(CreateCheckColumn());

        _installedGrid.Columns.Add(CreateTextColumn("Name", "Name", 240));
        _installedGrid.Columns.Add(CreateTextColumn("Id", "Id", 240));
        _installedGrid.Columns.Add(CreateTextColumn("Version", "Version", 90));
        _installedGrid.Columns.Add(CreateTextColumn("AvailableVersion", "Available", 90));
        _installedGrid.Columns.Add(CreateTextColumn("Source", "Source", 90));

        MarkAllTextColumnsReadOnly(_installedGrid);

        _installedGrid.SelectionChanged += (_, _) => UpdateInstalledButtons();
        _installedGrid.ColumnHeaderMouseClick += (_, e) => SortGrid(_installedGrid, e.ColumnIndex);
        _installedGrid.CellPainting += Grid_CellPainting;
        _installedGrid.CellMouseDown += InstalledGrid_CellMouseDown;
        _installedGrid.CellContentClick += (_, e) => OnGridCheckboxClick(_installedGrid, e, _checkedInstalledIds);
        _installedGrid.CurrentCellDirtyStateChanged += (_, _) => CommitCheckboxEdit(_installedGrid);
        _installedGrid.ContextMenuStrip = BuildInstalledContextMenu();

        _installedTab.Controls.Add(_installedGrid);
        _installedTab.Controls.Add(topRow);

        UpdateInstalledButtons();
    }

    private void BuildSearchTab()
    {
        var topRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(6, 6, 6, 6),
            ColumnCount = 2,
            RowCount = 1,
        };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var leftSearch = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        var rightSearchButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        _searchBox.Dock = DockStyle.None;
        _searchBox.Width = 320;
        _searchBox.PlaceholderText = "Search packages…";
        _searchBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await RunSearchAsync();
            }
        };

        ApplyWin98ButtonStyle(_searchButton);
        _searchButton.Dock = DockStyle.None;
        _searchButton.AutoSize = true;
        _searchButton.Click += async (_, _) => await RunSearchAsync();

        ApplyWin98ButtonStyle(_cancelSearchButton);
        _cancelSearchButton.Dock = DockStyle.None;
        _cancelSearchButton.AutoSize = true;
        _cancelSearchButton.Enabled = false;
        _cancelSearchButton.Click += (_, _) => CancelCurrentWork();

        leftSearch.Controls.Add(_searchBox);
        rightSearchButtons.Controls.Add(_searchButton);
        rightSearchButtons.Controls.Add(_cancelSearchButton);

        topRow.Controls.Add(leftSearch, 0, 0);
        topRow.Controls.Add(rightSearchButtons, 1, 0);

        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(6, 6, 6, 6),
            WrapContents = false,
        };

        ApplyWin98ButtonStyle(_installButton);
        _installButton.Click += async (_, _) => await InstallSelectedSearchAsync();
        bottomPanel.Controls.Add(_installButton);

        ApplyWin98ButtonStyle(_installSelectedButton);
        _installSelectedButton.Click += async (_, _) => await InstallCheckedSearchAsync();
        bottomPanel.Controls.Add(_installSelectedButton);

        _searchGrid.Dock = DockStyle.Fill;
        ConfigureGrid(_searchGrid);

        _searchGrid.Columns.Add(CreateCheckColumn());
        _searchGrid.Columns.Add(CreateTextColumn("Name", "Name", 240));
        _searchGrid.Columns.Add(CreateTextColumn("Id", "Id", 240));
        _searchGrid.Columns.Add(CreateTextColumn("Version", "Version", 90));
        _searchGrid.Columns.Add(CreateTextColumn("Source", "Source", 90));

        MarkAllTextColumnsReadOnly(_searchGrid);

        _searchGrid.SelectionChanged += async (_, _) => await LoadSelectedDescriptionAsync();
        _searchGrid.ColumnHeaderMouseClick += (_, e) => SortGrid(_searchGrid, e.ColumnIndex);
        _searchGrid.CellPainting += Grid_CellPainting;
        _searchGrid.CellMouseDown += SearchGrid_CellMouseDown;
        _searchGrid.CellContentClick += (_, e) => OnGridCheckboxClick(_searchGrid, e, _checkedSearchIds);
        _searchGrid.CurrentCellDirtyStateChanged += (_, _) => CommitCheckboxEdit(_searchGrid);
        _searchGrid.ContextMenuStrip = BuildSearchContextMenu();

        _descriptionBox.Dock = DockStyle.Fill;
        _descriptionBox.ReadOnly = true;
        _descriptionBox.BackColor = SystemColors.Window;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            Panel1MinSize = 160,
            Panel2MinSize = 80,
            SplitterDistance = 240,
        };
        split.Panel1.Controls.Add(_searchGrid);
        split.Panel2.Controls.Add(_descriptionBox);

        _searchTab.Controls.Add(split);
        _searchTab.Controls.Add(bottomPanel);
        _searchTab.Controls.Add(topRow);

        UpdateSearchButtons();
    }

    private void ConfigureLogBox()
    {
        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        _logBox.BackColor = SystemColors.Window;
        _logBox.Font = Font;
    }

    private async Task RefreshInstalledAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        SetBusy(true, "Loading installed packages…");
        AppendLog("> winget list");
        try
        {
            var installed = await _winget.GetInstalledAsync(ct);
            var upgrades = await _winget.GetUpgradesByIdAsync(ct);

            foreach (var pkg in installed)
            {
                if (upgrades.TryGetValue(pkg.Id, out var up) && !string.IsNullOrWhiteSpace(up.AvailableVersion))
                {
                    pkg.AvailableVersion = up.AvailableVersion;
                }
            }

            installed = installed
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            PopulateGrid(_installedGrid, installed);
            RestoreCheckedStates(_installedGrid, _checkedInstalledIds);
            SetBusy(false, $"Installed: {installed.Count}");
        }
        catch (OperationCanceledException)
        {
            SetBusy(false, "Canceled");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Error");
            MessageBox.Show(this, ex.Message, "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateInstalledButtons();
        }
    }

    private async Task RunSearchAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        var query = _searchBox.Text.Trim();
        if (query.Length == 0)
        {
            return;
        }

        SetBusy(true, "Searching…");
        AppendLog($"> winget search \"{query}\"");
        try
        {
            var results = await _winget.SearchAsync(query, ct);
            results = results
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            PopulateGrid(_searchGrid, results);
            RestoreCheckedStates(_searchGrid, _checkedSearchIds);
            _descriptionBox.Text = string.Empty;
            SetBusy(false, $"Results: {results.Count}");
        }
        catch (OperationCanceledException)
        {
            SetBusy(false, "Canceled");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Error");
            MessageBox.Show(this, ex.Message, "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateSearchButtons();
        }
    }

    private async Task LoadSelectedDescriptionAsync()
    {
        _descCts?.Cancel();
        _descCts = new CancellationTokenSource();
        var ct = _descCts.Token;

        var pkg = GetSelectedPackage(_searchGrid);
        UpdateSearchButtons();

        if (pkg is null)
        {
            _descriptionBox.Text = string.Empty;
            return;
        }

        _descriptionBox.Text = "Loading description…";

        try
        {
            var desc = await _winget.GetDescriptionAsync(pkg.Id, ct);
            _descriptionBox.Text = string.IsNullOrWhiteSpace(desc) ? "(No description available)" : desc;
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch
        {
            _descriptionBox.Text = "(No description available)";
        }
    }

    private async Task UpgradeSelectedInstalledAsync()
    {
        var pkg = GetSelectedPackage(_installedGrid);
        if (pkg is null)
        {
            return;
        }

        if (!pkg.HasUpgradeAvailable)
        {
            MessageBox.Show(this, "No upgrade is available for the selected package.", "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var prompt = $"Install update for:\n\n{pkg.Name}\n{pkg.Id}\n\nCurrent: {pkg.Version}\nAvailable: {pkg.AvailableVersion}\n\nProceed?";
        if (MessageBox.Show(this, prompt, "Win98Get", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        AppendLog($"> winget upgrade --id {pkg.Id} {AppSettings.UpgradeExtraArgs}".TrimEnd());
        await RunWingetActionWithOperationFormAsync(
            $"Upgrading {pkg.Id}…",
            () =>
            {
                var f = new OperationForm();
                f.SetOperation("Update", pkg.Name, pkg.Id, pkg.Version, AppSettings.UpgradeExtraArgs);
                return f;
            },
            (ct, form) => _winget.UpgradeStreamingAsync(
                pkg.Id,
                AppSettings.UpgradeExtraArgs,
                line =>
                {
                    form.OnOutputLine(line);
                    AppendLogRaw(line);
                },
                ct));
        await RefreshInstalledAsync();
    }

    private async Task RepairSelectedInstalledAsync()
    {
        var pkg = GetSelectedPackage(_installedGrid);
        if (pkg is null)
        {
            return;
        }

        var prompt = $"Repair:\n\n{pkg.Name}\n{pkg.Id}\n\nProceed?";
        if (MessageBox.Show(this, prompt, "Win98Get", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        AppendLog($"> winget repair --id {pkg.Id} --exact".TrimEnd());
        await RunWingetActionWithOperationFormAsync(
            $"Repairing {pkg.Id}…",
            () =>
            {
                var f = new OperationForm();
                f.SetOperation("Repair", pkg.Name, pkg.Id, pkg.Version, "--exact");
                return f;
            },
            (ct, form) => _winget.RepairByIdStreamingAsync(
                packageId: pkg.Id,
                onOutputLine: line =>
                {
                    form.OnOutputLine(line);
                    AppendLogRaw(line);
                },
                cancellationToken: ct));

        await RefreshInstalledAsync();
    }

    private async Task UpgradeAllAsync()
    {
        var pkgs = GetInstalledPackagesFromGrid();
        var count = pkgs.Count(p => p.HasUpgradeAvailable);
        if (count == 0)
        {
            MessageBox.Show(this, "No updates are available.", "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var prompt = $"Install all available updates?\n\nUpdates available: {count}\n\nProceed?";
        if (MessageBox.Show(this, prompt, "Win98Get", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        AppendLog($"> winget upgrade --all {AppSettings.UpgradeExtraArgs}".TrimEnd());
        await RunWingetActionWithOperationFormAsync(
            "Installing all updates…",
            () =>
            {
                var f = new OperationForm();
                f.SetOperation("Update All", "(multiple)", "--all", string.Empty, AppSettings.UpgradeExtraArgs);
                return f;
            },
            (ct, form) => _winget.UpgradeAllStreamingAsync(
                AppSettings.UpgradeExtraArgs,
                line =>
                {
                    form.OnOutputLine(line);
                    AppendLogRaw(line);
                },
                ct));
        await RefreshInstalledAsync();
    }

    private async Task UninstallSelectedInstalledAsync()
    {
        var pkg = GetSelectedPackage(_installedGrid);
        if (pkg is null)
        {
            return;
        }

        var prompt = $"Uninstall:\n\n{pkg.Name}\n{pkg.Id}\n\nProceed?";
        if (MessageBox.Show(this, prompt, "Win98Get", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        AppendLog($"> winget uninstall --id {pkg.Id} {AppSettings.UninstallExtraArgs}".TrimEnd());
        await RunWingetActionWithOperationFormAsync(
            $"Uninstalling {pkg.Id}…",
            () =>
            {
                var f = new OperationForm();
                f.SetOperation("Uninstall", pkg.Name, pkg.Id, pkg.Version, AppSettings.UninstallExtraArgs);
                return f;
            },
            (ct, form) => _winget.UninstallStreamingAsync(
                pkg.Id,
                AppSettings.UninstallExtraArgs,
                line =>
                {
                    form.OnOutputLine(line);
                    AppendLogRaw(line);
                },
                ct));
        await RefreshInstalledAsync();
    }

    private async Task InstallSelectedSearchAsync()
    {
        var pkg = GetSelectedPackage(_searchGrid);
        if (pkg is null)
        {
            return;
        }

        var prompt = $"Install:\n\n{pkg.Name}\n{pkg.Id}\n\nProceed?";
        if (MessageBox.Show(this, prompt, "Win98Get", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        AppendLog($"> winget install --id {pkg.Id} {AppSettings.InstallExtraArgs}".TrimEnd());
        await RunWingetActionWithOperationFormAsync(
            $"Installing {pkg.Id}…",
            () =>
            {
                var f = new OperationForm();
                f.SetOperation("Install", pkg.Name, pkg.Id, pkg.Version, AppSettings.InstallExtraArgs);
                return f;
            },
            (ct, form) => _winget.InstallStreamingAsync(
                packageId: pkg.Id,
                installLocation: null,
                additionalArgs: AppSettings.InstallExtraArgs,
                onOutputLine: line =>
                {
                    form.OnOutputLine(line);
                    AppendLogRaw(line);
                },
                cancellationToken: ct));
        await RefreshInstalledAsync();
    }

    private async Task UpgradeCheckedInstalledAsync()
    {
        var installed = GetInstalledPackagesFromGrid();
        var selected = installed
            .Where(p => _checkedInstalledIds.Contains(p.Id) && p.HasUpgradeAvailable)
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show(this, "No upgradable packages are checked.", "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var prompt = $"Install updates for checked packages?\n\nUpdates to install: {selected.Count}\n\nProceed?";
        if (MessageBox.Show(this, prompt, "Win98Get", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        await RunWingetActionWithOperationFormAsync(
            $"Installing {selected.Count} updates…",
            () =>
            {
                var f = new OperationForm();
                f.SetOperation("Update", "(multiple)", string.Empty, string.Empty, AppSettings.UpgradeExtraArgs);
                return f;
            },
            async (ct, form) =>
            {
                foreach (var pkg in selected)
                {
                    form.SetOperation("Update", pkg.Name, pkg.Id, pkg.Version, AppSettings.UpgradeExtraArgs);
                    AppendLog($"> winget upgrade --id {pkg.Id} {AppSettings.UpgradeExtraArgs}".TrimEnd());

                    var exit = await _winget.UpgradeStreamingAsync(
                        pkg.Id,
                        AppSettings.UpgradeExtraArgs,
                        line =>
                        {
                            form.OnOutputLine(line);
                            AppendLogRaw(line);
                        },
                        ct);

                    if (exit != 0)
                    {
                        return exit;
                    }
                }

                return 0;
            });

        await RefreshInstalledAsync();
    }

    private async Task UninstallCheckedInstalledAsync()
    {
        var installed = GetInstalledPackagesFromGrid();
        var selected = installed
            .Where(p => _checkedInstalledIds.Contains(p.Id))
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show(this, "No packages are checked.", "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var prompt = $"Uninstall checked packages?\n\nPackages to uninstall: {selected.Count}\n\nProceed?";
        if (MessageBox.Show(this, prompt, "Win98Get", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        await RunWingetActionWithOperationFormAsync(
            $"Uninstalling {selected.Count} packages…",
            () =>
            {
                var f = new OperationForm();
                f.SetOperation("Uninstall", "(multiple)", string.Empty, string.Empty, AppSettings.UninstallExtraArgs);
                return f;
            },
            async (ct, form) =>
            {
                foreach (var pkg in selected)
                {
                    form.SetOperation("Uninstall", pkg.Name, pkg.Id, pkg.Version, AppSettings.UninstallExtraArgs);
                    AppendLog($"> winget uninstall --id {pkg.Id} {AppSettings.UninstallExtraArgs}".TrimEnd());

                    var exit = await _winget.UninstallStreamingAsync(
                        pkg.Id,
                        AppSettings.UninstallExtraArgs,
                        line =>
                        {
                            form.OnOutputLine(line);
                            AppendLogRaw(line);
                        },
                        ct);

                    if (exit != 0)
                    {
                        return exit;
                    }
                }

                return 0;
            });

        await RefreshInstalledAsync();
    }

    private async Task InstallCheckedSearchAsync()
    {
        var search = GetPackagesFromGrid(_searchGrid);
        var selected = search
            .Where(p => _checkedSearchIds.Contains(p.Id))
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show(this, "No packages are checked.", "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var prompt = $"Install checked packages?\n\nPackages to install: {selected.Count}\n\nProceed?";
        if (MessageBox.Show(this, prompt, "Win98Get", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        await RunWingetActionWithOperationFormAsync(
            $"Installing {selected.Count} packages…",
            () =>
            {
                var f = new OperationForm();
                f.SetOperation("Install", "(multiple)", string.Empty, string.Empty, AppSettings.InstallExtraArgs);
                return f;
            },
            async (ct, form) =>
            {
                foreach (var pkg in selected)
                {
                    form.SetOperation("Install", pkg.Name, pkg.Id, pkg.Version, AppSettings.InstallExtraArgs);
                    AppendLog($"> winget install --id {pkg.Id} {AppSettings.InstallExtraArgs}".TrimEnd());

                    var exit = await _winget.InstallStreamingAsync(
                        packageId: pkg.Id,
                        installLocation: null,
                        additionalArgs: AppSettings.InstallExtraArgs,
                        onOutputLine: line =>
                        {
                            form.OnOutputLine(line);
                            AppendLogRaw(line);
                        },
                        cancellationToken: ct);

                    if (exit != 0)
                    {
                        return exit;
                    }
                }

                return 0;
            });

        await RefreshInstalledAsync();
    }

    private async Task RunWingetActionAsync(string statusText, Func<CancellationToken, Task<int>> action)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        SetBusy(true, statusText);
        try
        {
            var exit = await action(ct);
            SetBusy(false, exit == 0 ? "Done" : $"winget exited with code {exit}");
        }
        catch (OperationCanceledException)
        {
            SetBusy(false, "Canceled");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Error");
            MessageBox.Show(this, ex.Message, "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateInstalledButtons();
            UpdateSearchButtons();
        }
    }

    private async Task RunWingetActionWithOperationFormAsync(
        string statusText,
        Func<OperationForm> createForm,
        Func<CancellationToken, OperationForm, Task<int>> action)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        OperationForm? form = null;

        SetBusy(true, statusText);
        try
        {
            form = createForm();
            form.Show(this);
            form.BringToFront();

            var exit = await action(ct, form);
            SetBusy(false, exit == 0 ? "Done" : $"winget exited with code {exit}");
        }
        catch (OperationCanceledException)
        {
            SetBusy(false, "Canceled");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Error");
            MessageBox.Show(this, ex.Message, "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try
            {
                if (form is not null && !form.IsDisposed)
                {
                    form.Close();
                    form.Dispose();
                }
            }
            catch
            {
                // ignore
            }

            UpdateInstalledButtons();
            UpdateSearchButtons();
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _statusLabel.Text = message;
        UseWaitCursor = busy;
        _cancelInstalledButton.Enabled = busy;
        _cancelSearchButton.Enabled = busy;
        _refreshButton.Enabled = !busy;
        _searchButton.Enabled = !busy;
        _installButton.Enabled = !busy && GetSelectedPackage(_searchGrid) is not null;
        _installSelectedButton.Enabled = !busy && _checkedSearchIds.Count > 0;
        _updateButton.Enabled = !busy && (GetSelectedPackage(_installedGrid)?.HasUpgradeAvailable ?? false);
        _updateSelectedButton.Enabled = !busy && _checkedInstalledIds.Count > 0;
        _installAllUpdatesButton.Enabled = !busy && GetInstalledPackagesFromGrid().Any(p => p.HasUpgradeAvailable);
        _uninstallButton.Enabled = !busy && GetSelectedPackage(_installedGrid) is not null;
        _uninstallSelectedButton.Enabled = !busy && _checkedInstalledIds.Count > 0;
        _installedGrid.Enabled = !busy;
        _searchGrid.Enabled = !busy;

        _fileRefresh.Enabled = !busy;
        _fileInstallAll.Enabled = !busy && GetInstalledPackagesFromGrid().Any(p => p.HasUpgradeAvailable);
    }

    private void ToggleOutputLog(bool show)
    {
        _mainSplit.Panel2Collapsed = !show;
    }

    private void UpdateInstalledButtons()
    {
        var pkg = GetSelectedPackage(_installedGrid);
        _updateButton.Enabled = pkg?.HasUpgradeAvailable ?? false;
        _installAllUpdatesButton.Enabled = GetInstalledPackagesFromGrid().Any(p => p.HasUpgradeAvailable);
        _uninstallButton.Enabled = pkg is not null;

        var installed = GetInstalledPackagesFromGrid();
        _uninstallSelectedButton.Enabled = _checkedInstalledIds.Count > 0;
        _updateSelectedButton.Enabled = _checkedInstalledIds.Any(id => installed.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase) && p.HasUpgradeAvailable));

        _fileInstallAll.Enabled = GetInstalledPackagesFromGrid().Any(p => p.HasUpgradeAvailable);
    }

    private void CancelCurrentWork()
    {
        AppendLog("(cancel requested)");
        try { _loadCts?.Cancel(); } catch { }
        try { _descCts?.Cancel(); } catch { }
    }

    private void ShowAbout()
    {
        var version = Application.ProductVersion;
        var text = $"Win98Get\n\nA WinForms GUI for winget.\nVersion: {version}\n\nTip: Right-click rows for actions.";
        MessageBox.Show(this, text, "About Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void AppendLog(string text)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
        AppendLogRaw(line);
    }

    private void AppendLogRaw(string line)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLogRaw(line));
            return;
        }

        _logBox.AppendText(line + Environment.NewLine);
    }

    private IReadOnlyList<WingetPackage> GetInstalledPackagesFromGrid()
    {
        if (_installedGrid.DataSource is BindingSource bs && bs.DataSource is List<WingetPackage> list)
        {
            return list;
        }

        if (_installedGrid.DataSource is BindingSource bs2 && bs2.DataSource is IEnumerable<WingetPackage> enumerable)
        {
            return enumerable.ToList();
        }

        return Array.Empty<WingetPackage>();
    }

    private ContextMenuStrip BuildInstalledContextMenu()
    {
        var menu = new ContextMenuStrip();

        var installUpdate = new ToolStripMenuItem("Install update") { Enabled = false };
        installUpdate.Click += async (_, _) => await UpgradeSelectedInstalledAsync();

        var repair = new ToolStripMenuItem("Repair") { Enabled = false };
        repair.Click += async (_, _) => await RepairSelectedInstalledAsync();

        var uninstall = new ToolStripMenuItem("Uninstall");
        uninstall.Click += async (_, _) => await UninstallSelectedInstalledAsync();

        var showDetails = new ToolStripMenuItem("Show details");
        showDetails.Click += async (_, _) => await ShowSelectedInstalledDetailsAsync();

        var showLocation = new ToolStripMenuItem("Show file location");
        showLocation.Click += (_, _) => ShowSelectedInstalledLocation();

        var runProgram = new ToolStripMenuItem("Run program");
        runProgram.Click += (_, _) => RunSelectedInstalledProgram();

        var copyId = new ToolStripMenuItem("Copy Id");
        copyId.Click += (_, _) => CopySelected(_installedGrid, p => p.Id);

        var copyName = new ToolStripMenuItem("Copy Name");
        copyName.Click += (_, _) => CopySelected(_installedGrid, p => p.Name);

        menu.Opening += (_, _) =>
        {
            var pkg = GetSelectedPackage(_installedGrid);
            var has = pkg is not null;
            installUpdate.Enabled = has && pkg!.HasUpgradeAvailable;
            repair.Enabled = has;
            uninstall.Enabled = has;
            showDetails.Enabled = has;
            showLocation.Enabled = has;
            runProgram.Enabled = has;
            copyId.Enabled = has;
            copyName.Enabled = has;
        };

        menu.Items.Add(installUpdate);
        menu.Items.Add(repair);
        menu.Items.Add(uninstall);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(showDetails);
        menu.Items.Add(showLocation);
        menu.Items.Add(runProgram);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(copyId);
        menu.Items.Add(copyName);

        return menu;
    }

    private ContextMenuStrip BuildSearchContextMenu()
    {
        var menu = new ContextMenuStrip();

        var install = new ToolStripMenuItem("Install");
        install.Click += async (_, _) => await InstallSelectedSearchAsync();

        var showDetails = new ToolStripMenuItem("Show details");
        showDetails.Click += async (_, _) => await ShowSelectedSearchDetailsAsync();

        var copyId = new ToolStripMenuItem("Copy Id");
        copyId.Click += (_, _) => CopySelected(_searchGrid, p => p.Id);

        var copyName = new ToolStripMenuItem("Copy Name");
        copyName.Click += (_, _) => CopySelected(_searchGrid, p => p.Name);

        menu.Opening += (_, _) =>
        {
            var pkg = GetSelectedPackage(_searchGrid);
            var has = pkg is not null;
            install.Enabled = has;
            showDetails.Enabled = has;
            copyId.Enabled = has;
            copyName.Enabled = has;
        };

        menu.Items.Add(install);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(showDetails);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(copyId);
        menu.Items.Add(copyName);

        return menu;
    }

    private void InstalledGrid_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Control) == Keys.Control)
        {
            ToggleCheckboxForRow(_installedGrid, e.RowIndex, _checkedInstalledIds);
            return;
        }

        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        _installedGrid.ClearSelection();
        _installedGrid.Rows[e.RowIndex].Selected = true;
        _installedGrid.CurrentCell = _installedGrid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
    }

    private void SearchGrid_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Control) == Keys.Control)
        {
            ToggleCheckboxForRow(_searchGrid, e.RowIndex, _checkedSearchIds);
            return;
        }

        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        _searchGrid.ClearSelection();
        _searchGrid.Rows[e.RowIndex].Selected = true;
        _searchGrid.CurrentCell = _searchGrid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
    }

    private async Task ShowSelectedInstalledDetailsAsync()
    {
        var pkg = GetSelectedPackage(_installedGrid);
        if (pkg is null)
        {
            return;
        }

        _descCts?.Cancel();
        _descCts = new CancellationTokenSource();
        var ct = _descCts.Token;

        AppendLog($"> winget show --id {pkg.Id}");
        try
        {
            var result = await ProcessRunner.RunCaptureAsync("winget", $"show --id \"{pkg.Id.Replace("\"", "\\\"")}\" --accept-source-agreements", ct);
            if (!string.IsNullOrWhiteSpace(result.StdOut))
            {
                AppendLogRaw(result.StdOut.TrimEnd());
            }
            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                AppendLogRaw(result.StdErr.TrimEnd());
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("(details canceled)");
        }
    }

    private async Task ShowSelectedSearchDetailsAsync()
    {
        var pkg = GetSelectedPackage(_searchGrid);
        if (pkg is null)
        {
            return;
        }

        _descCts?.Cancel();
        _descCts = new CancellationTokenSource();
        var ct = _descCts.Token;

        AppendLog($"> winget show --id {pkg.Id}");
        try
        {
            var result = await ProcessRunner.RunCaptureAsync("winget", $"show --id \"{pkg.Id.Replace("\"", "\\\"")}\" --accept-source-agreements", ct);
            if (!string.IsNullOrWhiteSpace(result.StdOut))
            {
                AppendLogRaw(result.StdOut.TrimEnd());
            }
            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                AppendLogRaw(result.StdErr.TrimEnd());
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("(details canceled)");
        }
    }

    private void ShowSelectedInstalledLocation()
    {
        var pkg = GetSelectedPackage(_installedGrid);
        if (pkg is null)
        {
            return;
        }

        var info = InstallLocationResolver.TryResolve(pkg.Id, pkg.Name, pkg.Version);
        if (info is null)
        {
            MessageBox.Show(this, "Could not find an install location for this program.", "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var path = ExtractPath(info.DisplayIcon);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(info.InstallLocation) && Directory.Exists(info.InstallLocation))
        {
            _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{info.InstallLocation}\"",
                UseShellExecute = true,
            });
            return;
        }

        MessageBox.Show(this, "Could not find an install location for this program.", "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RunSelectedInstalledProgram()
    {
        var pkg = GetSelectedPackage(_installedGrid);
        if (pkg is null)
        {
            return;
        }

        var info = InstallLocationResolver.TryResolve(pkg.Id, pkg.Name, pkg.Version);
        if (info is null)
        {
            MessageBox.Show(this, "Could not determine how to run this program.", "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var path = ExtractPath(info.DisplayIcon);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                UseShellExecute = true,
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(info.InstallLocation) && Directory.Exists(info.InstallLocation))
        {
            _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{info.InstallLocation}\"",
                UseShellExecute = true,
            });
            return;
        }

        MessageBox.Show(this, "Could not determine how to run this program.", "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string ExtractPath(string displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return string.Empty;
        }

        var s = displayIcon.Trim();
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            if (end > 1)
            {
                return s.Substring(1, end - 1);
            }
        }

        var comma = s.IndexOf(',');
        if (comma > 0)
        {
            s = s.Substring(0, comma);
        }

        return s.Trim();
    }

    private static void CopySelected(DataGridView grid, Func<WingetPackage, string> selector)
    {
        var pkg = GetSelectedPackage(grid);
        if (pkg is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(selector(pkg));
        }
        catch
        {
            // Ignore clipboard issues.
        }
    }

    private void UpdateSearchButtons()
    {
        _installButton.Enabled = GetSelectedPackage(_searchGrid) is not null;
        _installSelectedButton.Enabled = _checkedSearchIds.Count > 0;
    }

    private static void ApplyWin98ButtonStyle(Button b)
    {
        b.FlatStyle = FlatStyle.System;
        b.AutoSize = true;
        b.Margin = new Padding(0, 0, 6, 0);
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.ReadOnly = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible = false;
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.Fixed3D;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.EditMode = DataGridViewEditMode.EditOnEnter;

        // Header styling is theme-dependent; see ApplyTheme().
    }

    private static DataGridViewCheckBoxColumn CreateCheckColumn()
        => new()
        {
            HeaderText = string.Empty,
            Width = 28,
            MinimumWidth = 28,
            FillWeight = 10,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Resizable = DataGridViewTriState.False,
        };

    private static void MarkAllTextColumnsReadOnly(DataGridView grid)
    {
        foreach (DataGridViewColumn col in grid.Columns)
        {
            col.ReadOnly = col is not DataGridViewCheckBoxColumn;
        }
    }

    private static void CommitCheckboxEdit(DataGridView grid)
    {
        if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
        {
            grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void OnGridCheckboxClick(DataGridView grid, DataGridViewCellEventArgs e, HashSet<string> checkedIds)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != CheckColumnIndex)
        {
            return;
        }

        grid.EndEdit();
        UpdateCheckedIdsFromRow(grid, e.RowIndex, checkedIds);

        if (ReferenceEquals(grid, _installedGrid))
        {
            UpdateInstalledButtons();
        }
        else
        {
            UpdateSearchButtons();
        }
    }

    private void ToggleCheckboxForRow(DataGridView grid, int rowIndex, HashSet<string> checkedIds)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
        {
            return;
        }

        var cell = grid.Rows[rowIndex].Cells[CheckColumnIndex];
        var current = cell.Value is bool b && b;
        cell.Value = !current;
        UpdateCheckedIdsFromRow(grid, rowIndex, checkedIds);

        if (ReferenceEquals(grid, _installedGrid))
        {
            UpdateInstalledButtons();
        }
        else
        {
            UpdateSearchButtons();
        }
    }

    private static void UpdateCheckedIdsFromRow(DataGridView grid, int rowIndex, HashSet<string> checkedIds)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
        {
            return;
        }

        if (grid.Rows[rowIndex].DataBoundItem is not WingetPackage pkg)
        {
            return;
        }

        var value = grid.Rows[rowIndex].Cells[CheckColumnIndex].Value;
        var isChecked = value is bool b && b;
        if (isChecked)
        {
            checkedIds.Add(pkg.Id);
        }
        else
        {
            checkedIds.Remove(pkg.Id);
        }
    }

    private void RestoreCheckedStates(DataGridView grid, HashSet<string> checkedIds)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pkg in GetPackagesFromGrid(grid))
        {
            present.Add(pkg.Id);
        }

        checkedIds.RemoveWhere(id => !present.Contains(id));

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is not WingetPackage pkg)
            {
                continue;
            }

            row.Cells[CheckColumnIndex].Value = checkedIds.Contains(pkg.Id);
        }
    }

    private void ShowSettings()
    {
        using var dlg = new SettingsForm();
        _ = dlg.ShowDialog(this);
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void RestartAsAdministrator()
    {
        if (IsRunningAsAdministrator())
        {
            MessageBox.Show(this, "Win98Get is already running as Administrator.", "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var exe = Application.ExecutablePath;
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
            };

            Process.Start(psi);
            Close();
        }
        catch (Win32Exception)
        {
            // User canceled UAC prompt.
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Win98Get", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static DataGridViewTextBoxColumn CreateTextColumn(string dataPropertyName, string headerText, int minWidth)
        => new()
        {
            DataPropertyName = dataPropertyName,
            HeaderText = headerText,
            MinimumWidth = minWidth,
            SortMode = DataGridViewColumnSortMode.Programmatic,
        };

    private static void PopulateGrid(DataGridView grid, IReadOnlyList<WingetPackage> packages)
    {
        // BindingList gives nicer UI refresh behavior.
        var list = new BindingSource { DataSource = packages.ToList() };
        grid.DataSource = list;
        if (grid.Rows.Count > 0)
        {
            grid.Rows[0].Selected = true;
        }
    }

    private void SortGrid(DataGridView grid, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
        {
            return;
        }

        var column = grid.Columns[columnIndex];
        var property = column.DataPropertyName;
        if (string.IsNullOrWhiteSpace(property))
        {
            return;
        }

        var selectedId = GetSelectedPackage(grid)?.Id;

        var list = GetPackagesFromGrid(grid).ToList();
        if (list.Count == 0)
        {
            return;
        }

        ref var currentProp = ref _installedSortProperty;
        ref var currentDir = ref _installedSortDirection;
        if (ReferenceEquals(grid, _searchGrid))
        {
            currentProp = ref _searchSortProperty;
            currentDir = ref _searchSortDirection;
        }

        // Toggle if same column, otherwise default to ascending.
        if (string.Equals(currentProp, property, StringComparison.OrdinalIgnoreCase))
        {
            currentDir = currentDir == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            currentProp = property;
            currentDir = ListSortDirection.Ascending;
        }

        var sorted = SortPackages(list, property, currentDir).ToList();
        PopulateGrid(grid, sorted);
        ApplySortGlyph(grid, property, currentDir);

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            ReselectById(grid, selectedId);
        }
    }

    private IReadOnlyList<WingetPackage> GetPackagesFromGrid(DataGridView grid)
    {
        if (grid.DataSource is BindingSource bs && bs.DataSource is IEnumerable<WingetPackage> enumerable)
        {
            return enumerable.ToList();
        }

        return Array.Empty<WingetPackage>();
    }

    private static IEnumerable<WingetPackage> SortPackages(IEnumerable<WingetPackage> packages, string property, ListSortDirection direction)
    {
        var desc = direction == ListSortDirection.Descending;

        IOrderedEnumerable<WingetPackage> ordered = property switch
        {
            "Name" => desc
                ? packages.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenByDescending(p => p.Id, StringComparer.OrdinalIgnoreCase)
                : packages.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase),
            "Id" => desc
                ? packages.OrderByDescending(p => p.Id, StringComparer.OrdinalIgnoreCase)
                : packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase),
            "Source" => desc
                ? packages.OrderByDescending(p => p.Source, StringComparer.OrdinalIgnoreCase).ThenByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase)
                : packages.OrderBy(p => p.Source, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            "Version" => desc
                ? packages.OrderByDescending(p => p.Version, VersionStringComparer.Instance).ThenByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase)
                : packages.OrderBy(p => p.Version, VersionStringComparer.Instance).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            "AvailableVersion" => desc
                ? packages.OrderByDescending(p => p.AvailableVersion, VersionStringComparer.Instance).ThenByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase)
                : packages.OrderBy(p => p.AvailableVersion, VersionStringComparer.Instance).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => desc
                ? packages.OrderByDescending(p => GetStringProperty(p, property), StringComparer.OrdinalIgnoreCase)
                : packages.OrderBy(p => GetStringProperty(p, property), StringComparer.OrdinalIgnoreCase),
        };

        return ordered;
    }

    private static string GetStringProperty(WingetPackage pkg, string property)
        => property switch
        {
            "Description" => pkg.Description,
            _ => string.Empty,
        };

    private void ApplySortGlyph(DataGridView grid, string? property, ListSortDirection direction)
    {
        foreach (DataGridViewColumn col in grid.Columns)
        {
            col.HeaderCell.SortGlyphDirection = SortOrder.None;
        }

        if (_theme != ThemeMode.Modern)
        {
            // In Win98 mode we draw a custom glyph in Grid_CellPainting.
            grid.Invalidate();
            return;
        }

        if (string.IsNullOrWhiteSpace(property))
        {
            grid.Invalidate();
            return;
        }

        var match = grid.Columns
            .Cast<DataGridViewColumn>()
            .FirstOrDefault(c => string.Equals(c.DataPropertyName, property, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            match.HeaderCell.SortGlyphDirection = direction == ListSortDirection.Ascending
                ? SortOrder.Ascending
                : SortOrder.Descending;
        }

        grid.Invalidate();
    }

    private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (_theme != ThemeMode.Windows98)
        {
            return;
        }

        if (sender is not DataGridView grid)
        {
            return;
        }

        // Header row only.
        if (e.RowIndex != -1 || e.ColumnIndex < 0)
        {
            return;
        }

        if (e.Graphics is null)
        {
            return;
        }

        var col = grid.Columns[e.ColumnIndex];
        var headerText = col.HeaderText ?? string.Empty;
        var property = col.DataPropertyName;

        var (activeProp, dir) = ReferenceEquals(grid, _searchGrid)
            ? (_searchSortProperty, _searchSortDirection)
            : (_installedSortProperty, _installedSortDirection);

        var isSortedCol = !string.IsNullOrWhiteSpace(property)
            && !string.IsNullOrWhiteSpace(activeProp)
            && string.Equals(activeProp, property, StringComparison.OrdinalIgnoreCase);

        var bounds = e.CellBounds;

        // Always paint headers ourselves to avoid the OS/current-column highlight (blue).
        using (var bg = new SolidBrush(SystemColors.Control))
        {
            e.Graphics.FillRectangle(bg, bounds);
        }

        // Classic 3D-ish header edge.
        using (var light = new Pen(SystemColors.ControlLightLight))
        using (var dark = new Pen(SystemColors.ControlDark))
        {
            e.Graphics.DrawLine(light, bounds.Left, bounds.Top, bounds.Right - 1, bounds.Top);
            e.Graphics.DrawLine(light, bounds.Left, bounds.Top, bounds.Left, bounds.Bottom - 1);
            e.Graphics.DrawLine(dark, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
            e.Graphics.DrawLine(dark, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
        }

        var padding = grid.ColumnHeadersDefaultCellStyle.Padding;
        var textRect = Rectangle.FromLTRB(
            bounds.Left + 6 + padding.Left,
            bounds.Top,
            bounds.Right - (6 + padding.Right),
            bounds.Bottom);

        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        var font = e.CellStyle?.Font ?? grid.Font ?? Control.DefaultFont;
        TextRenderer.DrawText(e.Graphics, headerText, font, textRect, SystemColors.ControlText, flags);

        if (isSortedCol)
        {
            var glyphWidth = 10;
            var glyphHeight = 7;
            var marginRight = 6;

            var centerY = bounds.Top + (bounds.Height / 2);
            var left = bounds.Right - marginRight - glyphWidth;
            var top = centerY - (glyphHeight / 2);

            Point[] triangle;
            if (dir == ListSortDirection.Ascending)
            {
                triangle =
                [
                    new Point(left + (glyphWidth / 2), top),
                    new Point(left + glyphWidth, top + glyphHeight),
                    new Point(left, top + glyphHeight),
                ];
            }
            else
            {
                triangle =
                [
                    new Point(left, top),
                    new Point(left + glyphWidth, top),
                    new Point(left + (glyphWidth / 2), top + glyphHeight),
                ];
            }

            using var fill = new SolidBrush(SystemColors.ControlText);
            e.Graphics.FillPolygon(fill, triangle);

            using var lightPen = new Pen(SystemColors.ControlLightLight);
            using var darkPen = new Pen(SystemColors.ControlDark);
            e.Graphics.DrawLine(lightPen, triangle[0], triangle[2]);
            e.Graphics.DrawLine(lightPen, triangle[0], triangle[1]);
            e.Graphics.DrawLine(darkPen, triangle[1], triangle[2]);
        }

        e.Handled = true;
    }

    private static void ReselectById(DataGridView grid, string id)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is WingetPackage pkg && string.Equals(pkg.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                row.Selected = true;
                grid.CurrentCell = row.Cells[0];
                return;
            }
        }
    }

    private sealed class VersionStringComparer : IComparer<string>
    {
        public static readonly VersionStringComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var xs = (x ?? string.Empty).Trim();
            var ys = (y ?? string.Empty).Trim();

            if (xs.Length == 0 && ys.Length == 0) return 0;
            if (xs.Length == 0) return -1;
            if (ys.Length == 0) return 1;

            var xParsed = Version.TryParse(Normalize(xs), out var xv);
            var yParsed = Version.TryParse(Normalize(ys), out var yv);

            if (xParsed && yParsed)
            {
                var cmp = xv!.CompareTo(yv);
                if (cmp != 0) return cmp;
                return StringComparer.OrdinalIgnoreCase.Compare(xs, ys);
            }

            if (xParsed != yParsed)
            {
                // Prefer parsed versions over non-parsed.
                return xParsed ? 1 : -1;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(xs, ys);
        }

        private static string Normalize(string s)
        {
            // Winget versions sometimes include prefixes like 'v1.2.3'.
            s = s.Trim();
            if (s.StartsWith('v') || s.StartsWith('V'))
            {
                var rest = s.Substring(1);
                if (Version.TryParse(rest, out _))
                {
                    return rest;
                }
            }
            return s;
        }
    }

    private static WingetPackage? GetSelectedPackage(DataGridView grid)
    {
        if (grid.CurrentRow?.DataBoundItem is WingetPackage pkg)
        {
            return pkg;
        }

        if (grid.SelectedRows.Count == 1 && grid.SelectedRows[0].DataBoundItem is WingetPackage pkg2)
        {
            return pkg2;
        }

        return null;
    }
}
