using Microsoft.Win32;

namespace Win98Get.Services;

public static class AppSettings
{
    private const string RootKeyPath = @"Software\Win98Get";

    private const string InstallFlagsKey = "InstallFlagKeys";
    private const string UpgradeFlagsKey = "UpgradeFlagKeys";
    private const string UninstallFlagsKey = "UninstallFlagKeys";

    public static string InstallExtraArgs => WingetFlagCatalog.BuildArgs(WingetOperation.Install, GetSelectedFlagKeys(WingetOperation.Install));

    public static string UpgradeExtraArgs => WingetFlagCatalog.BuildArgs(WingetOperation.Upgrade, GetSelectedFlagKeys(WingetOperation.Upgrade));

    public static string UninstallExtraArgs => WingetFlagCatalog.BuildArgs(WingetOperation.Uninstall, GetSelectedFlagKeys(WingetOperation.Uninstall));

    public static HashSet<string> GetSelectedFlagKeys(WingetOperation operation)
    {
        var keyName = operation switch
        {
            WingetOperation.Install => InstallFlagsKey,
            WingetOperation.Upgrade => UpgradeFlagsKey,
            WingetOperation.Uninstall => UninstallFlagsKey,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        var raw = ReadNullableString(keyName);
        if (raw is null)
        {
            return new HashSet<string>(WingetFlagCatalog.GetDefaultSelectedKeys(operation), StringComparer.OrdinalIgnoreCase);
        }

        return ParseKeyList(raw);
    }

    public static void SetSelectedFlagKeys(WingetOperation operation, IEnumerable<string> keys)
    {
        var keyName = operation switch
        {
            WingetOperation.Install => InstallFlagsKey,
            WingetOperation.Upgrade => UpgradeFlagsKey,
            WingetOperation.Uninstall => UninstallFlagsKey,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        var set = new HashSet<string>(keys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        WriteString(keyName, string.Join(";", set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
    }

    private static string? ReadNullableString(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RootKeyPath, writable: false);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteString(string name, string? value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RootKeyPath);
            key.SetValue(name, (value ?? string.Empty).Trim(), RegistryValueKind.String);
        }
        catch
        {
            // ignore
        }
    }

    private static HashSet<string> ParseKeyList(string raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return set;
        }

        foreach (var part in raw.Split(new[] { ';', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var k = part.Trim();
            if (k.Length == 0)
            {
                continue;
            }
            set.Add(k);
        }

        return set;
    }
}
