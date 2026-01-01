using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Win98Get.Modern.Models;
using Win98Get.Services;

namespace Win98Get.Modern.Views
{
    public partial class MainPage : Page
    {
        private static readonly Regex PercentRegex = new(@"(?<!\d)(\d{1,3})%", RegexOptions.Compiled);

        private readonly WingetService _winget = new();
        private CancellationTokenSource? _workCts;

        public ObservableCollection<WingetPackageProxy> Installed { get; } = new();
        public ObservableCollection<WingetPackageProxy> SearchResults { get; } = new();

        private string _installedSortColumn = "Name";
        private bool _installedSortAscending = true;
        private string _searchSortColumn = "Name";
        private bool _searchSortAscending = true;

        private WingetPackageProxy? _installedContextItem;
        private WingetPackageProxy? _searchContextItem;

        public MainPage()
        {
            InitializeComponent();

            DataContext = this;
            Loaded += OnLoaded;

            InitializeThemeCombo();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;

            SetStatus("Checking winget…");
            var (available, detail) = await _winget.CheckAvailableAsync(CancellationToken.None);
            if (!available)
            {
                SetStatus("winget not available");
                InstalledDetailsBox.Text = detail;
                return;
            }

            SetStatus(detail);
            await RefreshInstalledAsync();

            UpdateActionButtons();
        }

        private void InitializeThemeCombo()
        {
            if (Application.Current is not App app)
            {
                return;
            }

            ThemeCombo.SelectedIndex = app.CurrentThemePreference switch
            {
                App.ThemePreference.Light => 1,
                App.ThemePreference.Dark => 2,
                _ => 0,
            };
        }

        private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Application.Current is not App app)
            {
                return;
            }

            var tag = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            var pref = tag switch
            {
                "Light" => App.ThemePreference.Light,
                "Dark" => App.ThemePreference.Dark,
                _ => App.ThemePreference.System,
            };

            app.SetThemePreference(pref);
        }

        private async void OnRefreshInstalled(object sender, RoutedEventArgs e)
            => await RefreshInstalledAsync();

        private async Task RefreshInstalledAsync()
        {
            CancelWork();
            _workCts = new CancellationTokenSource();

            try
            {
                SetStatus("Loading installed packages…");
                Installed.Clear();
                InstalledDetailsBox.Text = string.Empty;

                var installed = await _winget.GetInstalledAsync(_workCts.Token);
                var upgrades = await _winget.GetUpgradesByIdAsync(_workCts.Token);

                foreach (var p in installed.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (upgrades.TryGetValue(p.Id, out var upgrade))
                    {
                        p.AvailableVersion = upgrade.AvailableVersion;
                    }

                    Installed.Add(WingetPackageProxy.From(p));
                }

                ApplyInstalledSort();

                SetStatus($"Installed: {Installed.Count}");
                UpdateActionButtons();
            }
            catch (OperationCanceledException)
            {
                SetStatus("Cancelled");
            }
            catch (Exception ex)
            {
                SetStatus("Failed to load installed packages");
                InstalledDetailsBox.Text = ex.ToString();
            }
        }

        private async void OnUpdateAll(object sender, RoutedEventArgs e)
        {
            if (!Installed.Any(p => !string.IsNullOrWhiteSpace(p.AvailableVersion)))
            {
                SetStatus("No updates available");
                UpdateActionButtons();
                return;
            }

            await RunWingetWithOperationDialogAsync(
                title: "Update all packages",
                initial: new OperationInfo(
                    Operation: "Update All",
                    Name: "(multiple)",
                    Id: "--all",
                    Version: string.Empty,
                    Flags: string.Empty),
                runner: (append, _setInfo, token) => _winget.UpgradeAllStreamingAsync("--include-unknown", append, token));

            await RefreshInstalledAsync();
        }

        private async void OnUpdateSelected(object sender, RoutedEventArgs e)
        {
            var selected = InstalledList.SelectedItems.OfType<WingetPackageProxy>().ToList();
            if (selected.Count == 0)
            {
                SetStatus("Select one or more installed packages");
                UpdateActionButtons();
                return;
            }

            var upgradable = selected
                .Where(p => !string.IsNullOrWhiteSpace(p.AvailableVersion))
                .ToList();

            if (upgradable.Count == 0)
            {
                SetStatus("No upgrades available for selected packages");
                UpdateActionButtons();
                return;
            }

            await RunWingetWithOperationDialogAsync(
                title: $"Update selected ({upgradable.Count})",
                initial: new OperationInfo(
                    Operation: "Update",
                    Name: "(multiple)",
                    Id: string.Empty,
                    Version: string.Empty,
                    Flags: string.Empty),
                runner: async (append, setInfo, token) =>
                {
                    var lastExit = 0;
                    foreach (var pkg in upgradable)
                    {
                        setInfo(new OperationInfo(
                            Operation: "Update",
                            Name: pkg.Name,
                            Id: pkg.Id,
                            Version: pkg.Version,
                            Flags: string.Empty));

                        lastExit = await _winget.UpgradeStreamingAsync(pkg.Id, additionalArgs: "--include-unknown", append, token);
                    }
                    return lastExit;
                });

            await RefreshInstalledAsync();
        }

        private async void OnUninstallSelected(object sender, RoutedEventArgs e)
        {
            var selected = InstalledList.SelectedItems.OfType<WingetPackageProxy>().ToList();
            if (selected.Count == 0)
            {
                SetStatus("Select one or more installed packages");
                UpdateActionButtons();
                return;
            }

            var confirm = await ShowConfirmDialogAsync(
                title: "Uninstall",
                message: selected.Count == 1
                    ? $"Uninstall {selected[0].Name} ({selected[0].Id})?"
                    : $"Uninstall {selected.Count} selected packages?",
                primaryButtonText: "Uninstall");

            if (!confirm)
            {
                return;
            }

            await RunWingetWithOperationDialogAsync(
                title: $"Uninstall ({selected.Count})",
                initial: new OperationInfo(
                    Operation: "Uninstall",
                    Name: "(multiple)",
                    Id: string.Empty,
                    Version: string.Empty,
                    Flags: string.Empty),
                runner: async (append, setInfo, token) =>
                {
                    var lastExit = 0;
                    foreach (var pkg in selected)
                    {
                        setInfo(new OperationInfo(
                            Operation: "Uninstall",
                            Name: pkg.Name,
                            Id: pkg.Id,
                            Version: pkg.Version,
                            Flags: string.Empty));

                        lastExit = await _winget.UninstallStreamingAsync(pkg.Id, additionalArgs: null, append, token);
                    }
                    return lastExit;
                });

            await RefreshInstalledAsync();
        }

        private async void OnSearch(object sender, RoutedEventArgs e)
        {
            var query = (SearchBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                SetStatus("Enter a search term");
                return;
            }

            CancelWork();
            _workCts = new CancellationTokenSource();

            try
            {
                SetStatus($"Searching: {query}…");
                SearchResults.Clear();
                DescriptionBox.Text = string.Empty;

                var results = await _winget.SearchAsync(query, _workCts.Token);
                foreach (var p in results.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    SearchResults.Add(WingetPackageProxy.From(p));
                }

                ApplySearchSort();

                SetStatus($"Results: {SearchResults.Count}");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Cancelled");
            }
            catch (Exception ex)
            {
                SetStatus("Search failed");
                DescriptionBox.Text = ex.ToString();
            }
        }

        private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                OnSearch(sender, new RoutedEventArgs());
            }
        }

        private async void OnInstalledSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionButtons();

            if (InstalledList.SelectedItems.Count != 1)
            {
                return;
            }

            if (InstalledList.SelectedItems[0] is WingetPackageProxy pkg)
            {
                await ShowDetailsAsync(pkg, InstalledDetailsBox);
            }
        }

        private async void OnSearchSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionButtons();
            if (SearchResultsList.SelectedItem is WingetPackageProxy pkg)
            {
                await ShowDetailsAsync(pkg, DescriptionBox);
            }
        }

        private void OnInstalledRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var element = e.OriginalSource as DependencyObject;
            while (element is not null)
            {
                if (element is ListViewItem lvi)
                {
                    if (lvi.Content is WingetPackageProxy pkg)
                    {
                        _installedContextItem = pkg;

                        // If right-clicked item isn't already selected, select it.
                        if (!InstalledList.SelectedItems.Contains(pkg))
                        {
                            InstalledList.SelectedItems.Clear();
                            InstalledList.SelectedItems.Add(pkg);
                        }

                        UpdateActionButtons();
                    }

                    break;
                }

                element = VisualTreeHelper.GetParent(element);
            }
        }

        private void OnSearchRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var element = e.OriginalSource as DependencyObject;
            while (element is not null)
            {
                if (element is ListViewItem lvi)
                {
                    if (lvi.Content is WingetPackageProxy pkg)
                    {
                        _searchContextItem = pkg;
                        SearchResultsList.SelectedItem = pkg;
                        UpdateActionButtons();
                    }

                    break;
                }

                element = VisualTreeHelper.GetParent(element);
            }
        }

        private async void OnInstallSelected(object sender, RoutedEventArgs e)
        {
            if (SearchResultsList.SelectedItem is not WingetPackageProxy pkg)
            {
                SetStatus("Select a search result to install");
                UpdateActionButtons();
                return;
            }

            var options = await ShowInstallOptionsDialogAsync(pkg);
            if (options is null)
            {
                return;
            }

            await RunWingetWithOperationDialogAsync(
                title: $"Install: {pkg.Name}",
                initial: new OperationInfo(
                    Operation: "Install",
                    Name: pkg.Name,
                    Id: pkg.Id,
                    Version: pkg.Version,
                    Flags: options.AdditionalArgs ?? string.Empty),
                runner: (append, _setInfo, token) => _winget.InstallStreamingAsync(pkg.Id, options.InstallLocation, options.AdditionalArgs, append, token));

            await RefreshInstalledAsync();
        }

        private void UpdateActionButtons()
        {
            if (UpdateAllButton is not null)
            {
                UpdateAllButton.IsEnabled = Installed.Any(p => !string.IsNullOrWhiteSpace(p.AvailableVersion));
            }

            if (UpdateSelectedButton is not null)
            {
                var selectedUpgradable = InstalledList.SelectedItems
                    .OfType<WingetPackageProxy>()
                    .Any(p => !string.IsNullOrWhiteSpace(p.AvailableVersion));

                UpdateSelectedButton.IsEnabled = selectedUpgradable;
            }

            if (UninstallSelectedButton is not null)
            {
                UninstallSelectedButton.IsEnabled = InstalledList.SelectedItems.Count > 0;
            }

            if (InstallSelectedButton is not null)
            {
                InstallSelectedButton.IsEnabled = SearchResultsList.SelectedItem is WingetPackageProxy;
            }
        }

        private async void OnInstalledContextUpdate(object sender, RoutedEventArgs e)
        {
            var pkg = _installedContextItem ?? InstalledList.SelectedItems.OfType<WingetPackageProxy>().FirstOrDefault();
            if (pkg is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(pkg.AvailableVersion))
            {
                SetStatus("No upgrade available for this package");
                return;
            }

            await RunWingetWithOperationDialogAsync(
                title: $"Update: {pkg.Name}",
                initial: new OperationInfo(
                    Operation: "Update",
                    Name: pkg.Name,
                    Id: pkg.Id,
                    Version: pkg.Version,
                    Flags: string.Empty),
                runner: (append, _setInfo, token) => _winget.UpgradeStreamingAsync(pkg.Id, additionalArgs: "--include-unknown", append, token));

            await RefreshInstalledAsync();
        }

        private async void OnInstalledContextUninstall(object sender, RoutedEventArgs e)
        {
            var pkg = _installedContextItem ?? InstalledList.SelectedItems.OfType<WingetPackageProxy>().FirstOrDefault();
            if (pkg is null)
            {
                return;
            }

            var confirm = await ShowConfirmDialogAsync(
                title: "Uninstall",
                message: $"Uninstall {pkg.Name} ({pkg.Id})?",
                primaryButtonText: "Uninstall");

            if (!confirm)
            {
                return;
            }

            await RunWingetWithOperationDialogAsync(
                title: $"Uninstall: {pkg.Name}",
                initial: new OperationInfo(
                    Operation: "Uninstall",
                    Name: pkg.Name,
                    Id: pkg.Id,
                    Version: pkg.Version,
                    Flags: string.Empty),
                runner: (append, _setInfo, token) => _winget.UninstallStreamingAsync(pkg.Id, additionalArgs: null, append, token));

            await RefreshInstalledAsync();
        }

        private async void OnInstalledContextShowLocation(object sender, RoutedEventArgs e)
        {
            var pkg = _installedContextItem ?? InstalledList.SelectedItems.OfType<WingetPackageProxy>().FirstOrDefault();
            if (pkg is null)
            {
                return;
            }

            var info = InstallLocationResolver.TryResolve(pkg.Id, pkg.Name, pkg.Version);
            var folder = GetBestFolder(info);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                await ShowInfoDialogAsync(
                    title: "File location",
                    message: "Could not resolve an install folder for this entry.");
                return;
            }

            try
            {
                await Launcher.LaunchFolderPathAsync(folder);
            }
            catch
            {
                // Fall back to Explorer.
                try { await Launcher.LaunchUriAsync(new Uri($"file:///{folder.Replace("\\", "/")}")); } catch { }
            }
        }

        private void OnInstalledContextCopyId(object sender, RoutedEventArgs e)
            => CopyToClipboard((_installedContextItem ?? InstalledList.SelectedItems.OfType<WingetPackageProxy>().FirstOrDefault())?.Id);

        private void OnInstalledContextCopyName(object sender, RoutedEventArgs e)
            => CopyToClipboard((_installedContextItem ?? InstalledList.SelectedItems.OfType<WingetPackageProxy>().FirstOrDefault())?.Name);

        private async void OnSearchContextInstall(object sender, RoutedEventArgs e)
        {
            var pkg = _searchContextItem ?? (SearchResultsList.SelectedItem as WingetPackageProxy);
            if (pkg is null)
            {
                return;
            }

            SearchResultsList.SelectedItem = pkg;
            OnInstallSelected(sender, e);
        }

        private void OnSearchContextCopyId(object sender, RoutedEventArgs e)
            => CopyToClipboard((_searchContextItem ?? (SearchResultsList.SelectedItem as WingetPackageProxy))?.Id);

        private void OnSearchContextCopyName(object sender, RoutedEventArgs e)
            => CopyToClipboard((_searchContextItem ?? (SearchResultsList.SelectedItem as WingetPackageProxy))?.Name);

        private void OnInstalledHeaderClick(object sender, RoutedEventArgs e)
        {
            var col = (sender as FrameworkElement)?.Tag as string;
            if (string.IsNullOrWhiteSpace(col))
            {
                return;
            }

            if (string.Equals(_installedSortColumn, col, StringComparison.OrdinalIgnoreCase))
            {
                _installedSortAscending = !_installedSortAscending;
            }
            else
            {
                _installedSortColumn = col;
                _installedSortAscending = true;
            }

            ApplyInstalledSort();
        }

        private void OnSearchHeaderClick(object sender, RoutedEventArgs e)
        {
            var col = (sender as FrameworkElement)?.Tag as string;
            if (string.IsNullOrWhiteSpace(col))
            {
                return;
            }

            if (string.Equals(_searchSortColumn, col, StringComparison.OrdinalIgnoreCase))
            {
                _searchSortAscending = !_searchSortAscending;
            }
            else
            {
                _searchSortColumn = col;
                _searchSortAscending = true;
            }

            ApplySearchSort();
        }

        private void ApplyInstalledSort()
        {
            if (Installed.Count <= 1)
            {
                UpdateInstalledHeaderText();
                return;
            }

            var selectedIds = InstalledList.SelectedItems
                .OfType<WingetPackageProxy>()
                .Select(p => p.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var sorted = Sort(Installed, _installedSortColumn, _installedSortAscending).ToList();
            Installed.Clear();
            foreach (var p in sorted)
            {
                Installed.Add(p);
            }

            InstalledList.SelectedItems.Clear();
            foreach (var p in Installed.Where(p => selectedIds.Contains(p.Id)))
            {
                InstalledList.SelectedItems.Add(p);
            }

            UpdateInstalledHeaderText();
        }

        private void ApplySearchSort()
        {
            if (SearchResults.Count <= 1)
            {
                UpdateSearchHeaderText();
                return;
            }

            var selectedId = (SearchResultsList.SelectedItem as WingetPackageProxy)?.Id;

            var sorted = Sort(SearchResults, _searchSortColumn, _searchSortAscending).ToList();
            SearchResults.Clear();
            foreach (var p in sorted)
            {
                SearchResults.Add(p);
            }

            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                SearchResultsList.SelectedItem = SearchResults.FirstOrDefault(p => string.Equals(p.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            }

            UpdateSearchHeaderText();
        }

        private static IOrderedEnumerable<WingetPackageProxy> Sort(
            IEnumerable<WingetPackageProxy> source,
            string column,
            bool ascending)
        {
            Func<WingetPackageProxy, string> keySelector = column switch
            {
                "Id" => p => p.Id ?? string.Empty,
                "Version" => p => p.Version ?? string.Empty,
                "Available" => p => p.AvailableVersion ?? string.Empty,
                "Source" => p => p.Source ?? string.Empty,
                _ => p => p.Name ?? string.Empty,
            };

            return ascending
                ? source.OrderBy(keySelector, StringComparer.CurrentCultureIgnoreCase)
                : source.OrderByDescending(keySelector, StringComparer.CurrentCultureIgnoreCase);
        }

        private void UpdateInstalledHeaderText()
        {
            InstalledHeaderName.Text = HeaderText("Name", _installedSortColumn, _installedSortAscending);
            InstalledHeaderId.Text = HeaderText("Id", _installedSortColumn, _installedSortAscending);
            InstalledHeaderVersion.Text = HeaderText("Version", _installedSortColumn, _installedSortAscending);
            InstalledHeaderAvailable.Text = HeaderText("Available", _installedSortColumn, _installedSortAscending);
        }

        private void UpdateSearchHeaderText()
        {
            SearchHeaderName.Text = HeaderText("Name", _searchSortColumn, _searchSortAscending);
            SearchHeaderId.Text = HeaderText("Id", _searchSortColumn, _searchSortAscending);
            SearchHeaderVersion.Text = HeaderText("Version", _searchSortColumn, _searchSortAscending);
            SearchHeaderSource.Text = HeaderText("Source", _searchSortColumn, _searchSortAscending);
        }

        private static string HeaderText(string label, string activeColumn, bool ascending)
        {
            if (!string.Equals(label, activeColumn, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }

            return ascending ? $"{label} ▲" : $"{label} ▼";
        }

        private static string GetBestFolder(InstallLocationInfo? info)
        {
            if (info is null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(info.InstallLocation))
            {
                return info.InstallLocation.Trim();
            }

            // Many entries provide DisplayIcon pointing at an executable.
            var icon = (info.DisplayIcon ?? string.Empty).Trim();
            if (icon.Length == 0)
            {
                return string.Empty;
            }

            // Common formats: "C:\Path\App.exe,0" or C:\Path\App.exe
            var path = icon;
            if (path.StartsWith("\"", StringComparison.Ordinal) && path.Contains('"'))
            {
                var end = path.IndexOf('"', 1);
                if (end > 1)
                {
                    path = path.Substring(1, end - 1);
                }
            }

            var comma = path.IndexOf(',');
            if (comma > 0)
            {
                path = path.Substring(0, comma);
            }

            try
            {
                var dir = Path.GetDirectoryName(path);
                return dir ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void CopyToClipboard(string? text)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return;
            }

            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
        }

        private async Task ShowInfoDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
            };

            await dialog.ShowAsync();
        }

        private async Task ShowDetailsAsync(WingetPackageProxy pkg, TextBox target)
        {
            CancelWork();
            _workCts = new CancellationTokenSource();

            try
            {
                SetStatus($"Loading: {pkg.Id}…");

                var description = await _winget.GetDescriptionAsync(pkg.Id, _workCts.Token);
                target.Text =
                    $"Name: {pkg.Name}{Environment.NewLine}" +
                    $"Id: {pkg.Id}{Environment.NewLine}" +
                    $"Version: {pkg.Version}{Environment.NewLine}" +
                    (string.IsNullOrWhiteSpace(pkg.AvailableVersion) ? string.Empty : $"Available: {pkg.AvailableVersion}{Environment.NewLine}") +
                    (string.IsNullOrWhiteSpace(pkg.Source) ? string.Empty : $"Source: {pkg.Source}{Environment.NewLine}") +
                    (string.IsNullOrWhiteSpace(description) ? string.Empty : $"{Environment.NewLine}{description}");

                SetStatus("Ready");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Cancelled");
            }
            catch (Exception ex)
            {
                SetStatus("Failed to load details");
                target.Text = ex.ToString();
            }
        }

        private void SetStatus(string text) => StatusText.Text = text;

        private void CancelWork()
        {
            try
            {
                _workCts?.Cancel();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _workCts?.Dispose();
                _workCts = null;
            }
        }

        private sealed record OperationInfo(string Operation, string Name, string Id, string Version, string Flags);

        private async Task<int> RunWingetWithOperationDialogAsync(
            string title,
            OperationInfo initial,
            Func<Action<string>, Action<OperationInfo>, CancellationToken, Task<int>> runner)
        {
            CancelWork();
            _workCts = new CancellationTokenSource();

            var opText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var nameText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var idText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var versionText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var flagsText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var statusText = new TextBlock { Text = "Loading", TextWrapping = TextWrapping.Wrap };
            var progress = new ProgressBar { IsIndeterminate = true, Minimum = 0, Maximum = 100, Value = 0 };

            void ApplyInfo(OperationInfo info)
            {
                opText.Text = info.Operation;
                nameText.Text = info.Name;
                idText.Text = info.Id;
                versionText.Text = info.Version;
                flagsText.Text = string.IsNullOrWhiteSpace(info.Flags) ? "(none)" : info.Flags.Trim();
                statusText.Text = "Loading";
                progress.IsIndeterminate = true;
                progress.Value = 0;
            }

            ApplyInfo(initial);

            var done = false;
            var exitCode = -1;

            var contentHost = new StackPanel
            {
                MinWidth = 520,
                MaxWidth = 720,
                Spacing = 10,
            };

            var fields = new Grid();
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            void AddRow(int row, string label, FrameworkElement value)
            {
                fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, Margin = new Thickness(0, 0, 10, 0) };
                Grid.SetRow(l, row);
                Grid.SetColumn(l, 0);
                Grid.SetRow(value, row);
                Grid.SetColumn(value, 1);
                fields.Children.Add(l);
                fields.Children.Add(value);
            }

            AddRow(0, "Operation:", opText);
            AddRow(1, "Name:", nameText);
            AddRow(2, "Id:", idText);
            AddRow(3, "Version:", versionText);
            AddRow(4, "Flags:", flagsText);
            AddRow(5, "Status:", statusText);

            contentHost.Children.Add(fields);
            contentHost.Children.Add(progress);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = contentHost,
                PrimaryButtonText = "Cancel",
                XamlRoot = XamlRoot,
            };

            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (done)
                {
                    return;
                }

                try
                {
                    _workCts?.Cancel();
                }
                catch
                {
                    // ignore
                }

                args.Cancel = true;
            };

            dialog.Closing += (_, _) =>
            {
                if (!done)
                {
                    try { _workCts?.Cancel(); } catch { }
                }
            };

            void Append(string line)
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    var text = (line ?? string.Empty).Trim();
                    if (text.Length == 0)
                    {
                        return;
                    }

                    var m = PercentRegex.Match(text);
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var percent))
                    {
                        percent = Math.Clamp(percent, 0, 100);
                        progress.IsIndeterminate = false;
                        progress.Value = percent;
                        statusText.Text = "Downloading";
                        return;
                    }

                    if (text.Contains("Downloading", StringComparison.OrdinalIgnoreCase) || text.Contains("Download", StringComparison.OrdinalIgnoreCase))
                    {
                        statusText.Text = "Downloading";
                        progress.IsIndeterminate = true;
                        return;
                    }

                    if (text.Contains("Installing", StringComparison.OrdinalIgnoreCase) || text.Contains("Starting package", StringComparison.OrdinalIgnoreCase))
                    {
                        statusText.Text = "Installing";
                        return;
                    }

                    if (text.Contains("Uninstalling", StringComparison.OrdinalIgnoreCase))
                    {
                        statusText.Text = "Uninstalling";
                        return;
                    }

                    // Keep the last interesting status line.
                    statusText.Text = text.Length > 90 ? text.Substring(0, 90) + "…" : text;
                });
            }

            void SetInfo(OperationInfo info)
            {
                _ = DispatcherQueue.TryEnqueue(() => ApplyInfo(info));
            }

            var opTask = Task.Run(async () =>
            {
                try
                {
                    exitCode = await runner(Append, SetInfo, _workCts.Token);
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        statusText.Text = exitCode == 0 ? "Done" : $"Exit code: {exitCode}";
                        progress.IsIndeterminate = false;
                        progress.Value = exitCode == 0 ? 100 : progress.Value;
                    });
                }
                catch (OperationCanceledException)
                {
                    _ = DispatcherQueue.TryEnqueue(() => statusText.Text = "Cancelled");
                }
                catch (Exception ex)
                {
                    _ = DispatcherQueue.TryEnqueue(() => statusText.Text = ex.Message);
                }
                finally
                {
                    done = true;
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        dialog.PrimaryButtonText = "Close";
                    });
                }

                return exitCode;
            });

            await dialog.ShowAsync();
            return await opTask;
        }

        private async Task<bool> ShowConfirmDialogAsync(string title, string message, string primaryButtonText)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot,
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private sealed record InstallOptions(string InstallLocation, string AdditionalArgs);

        private async Task<InstallOptions?> ShowInstallOptionsDialogAsync(WingetPackageProxy pkg)
        {
            var locationBox = new TextBox
            {
                PlaceholderText = "(optional) Install location (folder path)",
            };

            var argsBox = new TextBox
            {
                PlaceholderText = "(optional) Additional winget arguments",
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = $"Install: {pkg.Name} ({pkg.Id})",
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(locationBox);
            panel.Children.Add(argsBox);

            var dialog = new ContentDialog
            {
                Title = "Install options",
                Content = panel,
                PrimaryButtonText = "Install",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot,
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            return new InstallOptions(
                InstallLocation: (locationBox.Text ?? string.Empty).Trim(),
                AdditionalArgs: (argsBox.Text ?? string.Empty).Trim());
        }
    }
}
