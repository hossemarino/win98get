namespace Win98Get.Services;

public enum WingetOperation
{
    Install,
    Upgrade,
    Uninstall,
}

public sealed record WingetFlag(
    string Key,
    string Argument,
    string DisplayName,
    string Category,
    string? ExclusiveGroup = null,
    bool DefaultOn = false);

public static class WingetFlagCatalog
{
    public static IReadOnlyList<WingetFlag> GetFlags(WingetOperation op)
        => op switch
        {
            WingetOperation.Install => InstallFlags,
            WingetOperation.Upgrade => UpgradeFlags,
            WingetOperation.Uninstall => UninstallFlags,
            _ => Array.Empty<WingetFlag>(),
        };

    public static IReadOnlyCollection<string> GetDefaultSelectedKeys(WingetOperation op)
        => GetFlags(op)
            .Where(f => f.DefaultOn)
            .Select(f => f.Key)
            .ToArray();

    public static string BuildArgs(WingetOperation op, IEnumerable<string> selectedKeys)
    {
        var selected = new HashSet<string>(selectedKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var args = GetFlags(op)
            .Where(f => selected.Contains(f.Key))
            .Select(f => f.Argument)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToArray();

        return string.Join(" ", args);
    }

    // Notes:
    // - We only include flags that don't require an extra value field.
    // - Mutually exclusive groups are enforced in SettingsForm.

    private static readonly WingetFlag[] InstallFlags =
    [
        new("install.silent", "--silent", "Silent", "Mode", ExclusiveGroup: "mode"),
        new("install.interactive", "--interactive", "Interactive", "Mode", ExclusiveGroup: "mode"),

        new("install.scope.user", "--scope user", "Scope: User", "Scope", ExclusiveGroup: "scope"),
        new("install.scope.machine", "--scope machine", "Scope: Machine", "Scope", ExclusiveGroup: "scope"),

        new("install.force", "--force", "Force", "Behavior"),
        new("install.skipDependencies", "--skip-dependencies", "Skip dependencies", "Behavior"),
        new("install.allowReboot", "--allow-reboot", "Allow reboot", "Behavior"),
        new("install.ignoreSecurityHash", "--ignore-security-hash", "Ignore security hash", "Behavior"),

        new("install.disableInteractivity", "--disable-interactivity", "Disable interactivity", "Diagnostics"),
        new("install.verboseLogs", "--verbose-logs", "Verbose logs", "Diagnostics"),
        new("install.openLogs", "--open-logs", "Open logs after", "Diagnostics"),
    ];

    private static readonly WingetFlag[] UpgradeFlags =
    [
        // Keep the reliability behavior as a default.
        new("upgrade.includeUnknown", "--include-unknown", "Include unknown versions", "Selection", DefaultOn: true),
        new("upgrade.includePinned", "--include-pinned", "Include pinned", "Selection"),

        new("upgrade.silent", "--silent", "Silent", "Mode", ExclusiveGroup: "mode"),
        new("upgrade.interactive", "--interactive", "Interactive", "Mode", ExclusiveGroup: "mode"),

        new("upgrade.scope.user", "--scope user", "Scope: User", "Scope", ExclusiveGroup: "scope"),
        new("upgrade.scope.machine", "--scope machine", "Scope: Machine", "Scope", ExclusiveGroup: "scope"),

        new("upgrade.uninstallPrevious", "--uninstall-previous", "Uninstall previous", "Behavior"),
        new("upgrade.force", "--force", "Force", "Behavior"),
        new("upgrade.skipDependencies", "--skip-dependencies", "Skip dependencies", "Behavior"),
        new("upgrade.allowReboot", "--allow-reboot", "Allow reboot", "Behavior"),
        new("upgrade.ignoreSecurityHash", "--ignore-security-hash", "Ignore security hash", "Behavior"),

        new("upgrade.disableInteractivity", "--disable-interactivity", "Disable interactivity", "Diagnostics"),
        new("upgrade.verboseLogs", "--verbose-logs", "Verbose logs", "Diagnostics"),
        new("upgrade.openLogs", "--open-logs", "Open logs after", "Diagnostics"),
    ];

    private static readonly WingetFlag[] UninstallFlags =
    [
        new("uninstall.silent", "--silent", "Silent", "Mode", ExclusiveGroup: "mode"),
        new("uninstall.interactive", "--interactive", "Interactive", "Mode", ExclusiveGroup: "mode"),

        new("uninstall.scope.user", "--scope user", "Scope: User", "Scope", ExclusiveGroup: "scope"),
        new("uninstall.scope.machine", "--scope machine", "Scope: Machine", "Scope", ExclusiveGroup: "scope"),

        new("uninstall.force", "--force", "Force", "Behavior"),
        new("uninstall.purge", "--purge", "Purge (portable)", "Behavior", ExclusiveGroup: "portable"),
        new("uninstall.preserve", "--preserve", "Preserve (portable)", "Behavior", ExclusiveGroup: "portable"),
        new("uninstall.acceptSourceAgreements", "--accept-source-agreements", "Accept source agreements", "Behavior"),

        new("uninstall.disableInteractivity", "--disable-interactivity", "Disable interactivity", "Diagnostics"),
        new("uninstall.verboseLogs", "--verbose-logs", "Verbose logs", "Diagnostics"),
        new("uninstall.openLogs", "--open-logs", "Open logs after", "Diagnostics"),
    ];
}
