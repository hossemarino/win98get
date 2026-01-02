using Win98Get.Models;

namespace Win98Get.Services;

public sealed class WingetService
{
    private const string WingetExe = "winget";

    public async Task<(bool Available, string Detail)> CheckAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await ProcessRunner.RunCaptureAsync(WingetExe, "--version", timeoutCts.Token);

            if (result.ExitCode == 0)
            {
                var version = (result.StdOut ?? string.Empty).Trim();
                return (true, version.Length == 0 ? "winget available" : $"winget {version}");
            }

            var msg = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            msg = string.IsNullOrWhiteSpace(msg) ? $"winget exited with code {result.ExitCode}" : msg.Trim();
            return (false, msg);
        }
        catch (OperationCanceledException)
        {
            return (false, "Timed out while checking winget");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<WingetPackage>> GetInstalledAsync(CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunCaptureAsync(WingetExe, "list", cancellationToken);
        ThrowIfWingetFailed(result, "winget list");

        var rows = WingetTableParser.Parse(result.StdOut);
        var packages = new List<WingetPackage>();

        foreach (var row in rows)
        {
            var name = Get(row, "Name");
            var id = Get(row, "Id");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            packages.Add(new WingetPackage
            {
                Name = name,
                Id = id,
                Version = Get(row, "Version"),
                AvailableVersion = Get(row, "Available"),
                Source = Get(row, "Source"),
            });
        }

        return packages;
    }

    public async Task<Dictionary<string, WingetPackage>> GetUpgradesByIdAsync(CancellationToken cancellationToken)
    {
        // `winget upgrade` lists packages with available upgrades.
        var result = await ProcessRunner.RunCaptureAsync(WingetExe, "upgrade --include-unknown", cancellationToken);

        // Some environments may require source agreement acceptance to list upgrades.
        if (result.ExitCode != 0)
        {
            result = await ProcessRunner.RunCaptureAsync(WingetExe, "upgrade --include-unknown --accept-source-agreements", cancellationToken);
        }

        if (result.ExitCode != 0)
        {
            // If upgrade listing fails (e.g., no sources, policy), treat as no upgrades.
            return new Dictionary<string, WingetPackage>(StringComparer.OrdinalIgnoreCase);
        }

        var rows = WingetTableParser.Parse(result.StdOut);
        var dict = new Dictionary<string, WingetPackage>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var id = Get(row, "Id");
            var name = Get(row, "Name");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            dict[id] = new WingetPackage
            {
                Name = string.IsNullOrWhiteSpace(name) ? id : name,
                Id = id,
                Version = Get(row, "Version"),
                AvailableVersion = Get(row, "Available"),
                Source = Get(row, "Source"),
            };
        }

        return dict;
    }

    public async Task<IReadOnlyList<WingetPackage>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return Array.Empty<WingetPackage>();
        }

        // Use exact string as query, escaping quotes.
        var safeQuery = query.Replace("\"", "\\\"");
        var result = await ProcessRunner.RunCaptureAsync(WingetExe, $"search \"{safeQuery}\" --accept-source-agreements", cancellationToken);
        ThrowIfWingetFailed(result, "winget search");

        var rows = WingetTableParser.Parse(result.StdOut);
        var packages = new List<WingetPackage>();

        foreach (var row in rows)
        {
            var name = Get(row, "Name");
            var id = Get(row, "Id");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            packages.Add(new WingetPackage
            {
                Name = name,
                Id = id,
                Version = Get(row, "Version"),
                Source = Get(row, "Source"),
            });
        }

        return packages;
    }

    public async Task<string> GetDescriptionAsync(string packageId, CancellationToken cancellationToken)
    {
        packageId = (packageId ?? string.Empty).Trim();
        if (packageId.Length == 0)
        {
            return string.Empty;
        }

        var safeId = packageId.Replace("\"", "\\\"");
        var result = await ProcessRunner.RunCaptureAsync(WingetExe, $"show --id \"{safeId}\" --accept-source-agreements", cancellationToken);
        if (result.ExitCode != 0)
        {
            return string.Empty;
        }

        return ParseDescriptionFromShow(result.StdOut);
    }

    public Task<int> InstallAsync(string packageId, CancellationToken cancellationToken)
        => InstallStreamingAsync(packageId, _ => { }, cancellationToken);

    public Task<int> UpgradeAsync(string packageId, CancellationToken cancellationToken)
        => UpgradeStreamingAsync(packageId, _ => { }, cancellationToken);

    public Task<int> UninstallAsync(string packageId, CancellationToken cancellationToken)
        => UninstallStreamingAsync(packageId, _ => { }, cancellationToken);

    public Task<int> InstallStreamingAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken)
        => InstallStreamingAsync(packageId, installLocation: null, additionalArgs: null, onOutputLine, cancellationToken);

    public Task<int> InstallStreamingAsync(string packageId, string? installLocation, string? additionalArgs, Action<string> onOutputLine, CancellationToken cancellationToken)
    {
        var safeId = packageId.Replace("\"", "\\\"");

        var args = $"install --id \"{safeId}\" --accept-source-agreements --accept-package-agreements";
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            var safeLocation = installLocation.Trim().Replace("\"", "\\\"");
            args += $" --location \"{safeLocation}\"";
        }

        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            args += " " + additionalArgs.Trim();
        }

        return RunWingetStreamingAsync(args, onOutputLine, cancellationToken);
    }

    public Task<int> UpgradeStreamingAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken)
        => UpgradeStreamingAsync(packageId, additionalArgs: null, onOutputLine, cancellationToken);

    public Task<int> UpgradeStreamingAsync(string packageId, string? additionalArgs, Action<string> onOutputLine, CancellationToken cancellationToken)
    {
        var safeId = packageId.Replace("\"", "\\\"");
        var args = $"upgrade --id \"{safeId}\" --accept-source-agreements --accept-package-agreements";

        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            args += " " + additionalArgs.Trim();
        }

        return RunWingetStreamingAsync(args, onOutputLine, cancellationToken);
    }

    public Task<int> UpgradeAllStreamingAsync(Action<string> onOutputLine, CancellationToken cancellationToken)
        => UpgradeAllStreamingAsync(additionalArgs: null, onOutputLine, cancellationToken);

    public Task<int> UpgradeAllStreamingAsync(string? additionalArgs, Action<string> onOutputLine, CancellationToken cancellationToken)
    {
        var args = "upgrade --all --accept-source-agreements --accept-package-agreements";
        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            args += " " + additionalArgs.Trim();
        }

        return RunWingetStreamingAsync(args, onOutputLine, cancellationToken);
    }

    public Task<int> UninstallStreamingAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken)
        => UninstallStreamingAsync(packageId, additionalArgs: null, onOutputLine, cancellationToken);

    public Task<int> UninstallStreamingAsync(string packageId, string? additionalArgs, Action<string> onOutputLine, CancellationToken cancellationToken)
    {
        var safeId = packageId.Replace("\"", "\\\"");
        var args = $"uninstall --id \"{safeId}\"";

        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            args += " " + additionalArgs.Trim();
        }

        return RunWingetStreamingAsync(args, onOutputLine, cancellationToken);
    }

    public Task<int> ExportStreamingAsync(string outputPath, Action<string> onOutputLine, CancellationToken cancellationToken)
        => ExportStreamingAsync(outputPath, additionalArgs: null, onOutputLine, cancellationToken);

    public Task<int> ExportStreamingAsync(string outputPath, string? additionalArgs, Action<string> onOutputLine, CancellationToken cancellationToken)
    {
        outputPath = (outputPath ?? string.Empty).Trim();
        if (outputPath.Length == 0)
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var safePath = outputPath.Replace("\"", "\\\"");
        var args = $"export -o \"{safePath}\" --accept-source-agreements";

        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            args += " " + additionalArgs.Trim();
        }

        return RunWingetStreamingAsync(args, onOutputLine, cancellationToken);
    }

    public Task<int> ImportStreamingAsync(string importFile, Action<string> onOutputLine, CancellationToken cancellationToken)
        => ImportStreamingAsync(importFile, additionalArgs: null, onOutputLine, cancellationToken);

    public Task<int> ImportStreamingAsync(string importFile, string? additionalArgs, Action<string> onOutputLine, CancellationToken cancellationToken)
    {
        importFile = (importFile ?? string.Empty).Trim();
        if (importFile.Length == 0)
        {
            throw new ArgumentException("Import file path is required.", nameof(importFile));
        }

        var safePath = importFile.Replace("\"", "\\\"");
        var args = $"import -i \"{safePath}\" --accept-source-agreements --accept-package-agreements";

        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            args += " " + additionalArgs.Trim();
        }

        return RunWingetStreamingAsync(args, onOutputLine, cancellationToken);
    }

    public Task<int> RepairByIdStreamingAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken)
        => RepairByIdStreamingAsync(packageId, additionalArgs: null, onOutputLine, cancellationToken);

    public Task<int> RepairByIdStreamingAsync(string packageId, string? additionalArgs, Action<string> onOutputLine, CancellationToken cancellationToken)
    {
        packageId = (packageId ?? string.Empty).Trim();
        if (packageId.Length == 0)
        {
            throw new ArgumentException("Package Id is required.", nameof(packageId));
        }

        var safeId = packageId.Replace("\"", "\\\"");

        // `winget repair` supports filtering by --id. Use --exact to avoid partial matches.
        var args = $"repair --id \"{safeId}\" --exact --accept-source-agreements --accept-package-agreements";
        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            args += " " + additionalArgs.Trim();
        }

        return RunWingetStreamingAsync(args, onOutputLine, cancellationToken);
    }

    public Task<int> RepairByQueryStreamingAsync(string query, Action<string> onOutputLine, CancellationToken cancellationToken)
        => RepairByQueryStreamingAsync(query, additionalArgs: null, onOutputLine, cancellationToken);

    public Task<int> RepairByQueryStreamingAsync(string query, string? additionalArgs, Action<string> onOutputLine, CancellationToken cancellationToken)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            throw new ArgumentException("Query is required.", nameof(query));
        }

        var safeQuery = query.Replace("\"", "\\\"");
        var args = $"repair -q \"{safeQuery}\" --accept-source-agreements --accept-package-agreements";
        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            args += " " + additionalArgs.Trim();
        }

        return RunWingetStreamingAsync(args, onOutputLine, cancellationToken);
    }

    public Task<int> RepairByManifestStreamingAsync(string manifestPath, Action<string> onOutputLine, CancellationToken cancellationToken)
        => RepairByManifestStreamingAsync(manifestPath, additionalArgs: null, onOutputLine, cancellationToken);

    public Task<int> RepairByManifestStreamingAsync(string manifestPath, string? additionalArgs, Action<string> onOutputLine, CancellationToken cancellationToken)
    {
        manifestPath = (manifestPath ?? string.Empty).Trim();
        if (manifestPath.Length == 0)
        {
            throw new ArgumentException("Manifest path is required.", nameof(manifestPath));
        }

        var safePath = manifestPath.Replace("\"", "\\\"");
        var args = $"repair -m \"{safePath}\" --accept-source-agreements --accept-package-agreements";
        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            args += " " + additionalArgs.Trim();
        }

        return RunWingetStreamingAsync(args, onOutputLine, cancellationToken);
    }

    private static Task<int> RunWingetStreamingAsync(string args, Action<string> onOutputLine, CancellationToken cancellationToken)
        => ProcessRunner.RunStreamingAsync(WingetExe, args, onOutputLine, cancellationToken);

    private static void ThrowIfWingetFailed(CommandResult result, string context)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        var message = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"{context} failed with exit code {result.ExitCode}.";
        }

        throw new InvalidOperationException(message.Trim());
    }

    private static string Get(Dictionary<string, string> row, string key)
        => row.TryGetValue(key, out var v) ? v : string.Empty;

    private static string ParseDescriptionFromShow(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!line.TrimStart().StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var first = line.Substring(line.IndexOf(':') + 1).Trim();
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(first))
            {
                parts.Add(first);
            }

            // Continuation lines are commonly indented. Stop when a new field starts.
            for (var j = i + 1; j < lines.Length; j++)
            {
                var next = lines[j];
                if (string.IsNullOrWhiteSpace(next))
                {
                    break;
                }

                var trimmed = next.TrimEnd();
                var looksLikeField = trimmed.Length > 0
                    && !char.IsWhiteSpace(next, 0)
                    && trimmed.Contains(':');

                if (looksLikeField)
                {
                    break;
                }

                parts.Add(trimmed.Trim());
            }

            return string.Join(Environment.NewLine, parts).Trim();
        }

        return string.Empty;
    }
}
