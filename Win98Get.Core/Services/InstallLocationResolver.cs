using Microsoft.Win32;

namespace Win98Get.Services;

public sealed class InstallLocationInfo
{
    public string DisplayName { get; set; } = string.Empty;
    public string DisplayVersion { get; set; } = string.Empty;
    public string InstallLocation { get; set; } = string.Empty;
    public string DisplayIcon { get; set; } = string.Empty;
    public string UninstallString { get; set; } = string.Empty;
}

public static class InstallLocationResolver
{
    public static InstallLocationInfo? TryResolve(string wingetId, string displayName, string version)
    {
        var byId = TryResolveByWingetId(wingetId);
        if (byId is not null)
        {
            return byId;
        }

        return TryResolve(displayName, version);
    }

    public static InstallLocationInfo? TryResolve(string displayName, string version)
    {
        displayName = (displayName ?? string.Empty).Trim();
        if (displayName.Length == 0)
        {
            return null;
        }

        var candidates = new List<(int Score, InstallLocationInfo Info)>();

        foreach (var root in new[]
        {
            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        })
        {
            if (root is null)
            {
                continue;
            }

            foreach (var subKeyName in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(subKeyName);
                if (sub is null)
                {
                    continue;
                }

                var dn = (sub.GetValue("DisplayName") as string) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(dn))
                {
                    continue;
                }

                var dv = (sub.GetValue("DisplayVersion") as string) ?? string.Empty;
                var installLocation = (sub.GetValue("InstallLocation") as string) ?? string.Empty;
                var displayIcon = (sub.GetValue("DisplayIcon") as string) ?? string.Empty;
                var uninstallString = (sub.GetValue("UninstallString") as string) ?? string.Empty;

                var score = Score(displayName, version, dn, dv);
                if (score <= 0)
                {
                    continue;
                }

                candidates.Add((score, new InstallLocationInfo
                {
                    DisplayName = dn,
                    DisplayVersion = dv,
                    InstallLocation = installLocation,
                    DisplayIcon = displayIcon,
                    UninstallString = uninstallString,
                }));
            }
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .Select(c => c.Info)
            .FirstOrDefault();
    }

    private static InstallLocationInfo? TryResolveByWingetId(string wingetId)
    {
        wingetId = (wingetId ?? string.Empty).Trim();
        if (!wingetId.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Expected patterns:
        // ARP\Machine\X64\<UninstallSubKey>
        // ARP\Machine\X86\<UninstallSubKey>
        // ARP\User\X64\<UninstallSubKey>
        // (Some ids may have more segments in the tail; treat the tail as the subkey path.)
        var parts = wingetId.Split('\\');
        if (parts.Length < 4)
        {
            return null;
        }

        var scope = parts[1];
        var arch = parts[2];
        var tail = string.Join("\\", parts.Skip(3));
        if (string.IsNullOrWhiteSpace(tail))
        {
            return null;
        }

        var isMachine = scope.Equals("Machine", StringComparison.OrdinalIgnoreCase);
        var isUser = scope.Equals("User", StringComparison.OrdinalIgnoreCase) || scope.Equals("CurrentUser", StringComparison.OrdinalIgnoreCase);
        var isX86 = arch.Equals("X86", StringComparison.OrdinalIgnoreCase);
        var isX64 = arch.Equals("X64", StringComparison.OrdinalIgnoreCase);

        if (!isMachine && !isUser)
        {
            return null;
        }

        if (!isX86 && !isX64)
        {
            // Unknown arch marker.
            return null;
        }

        var roots = new List<RegistryKey?>();
        if (isMachine)
        {
            roots.Add(isX86
                ? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
                : Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"));

            // Fallback: try the other view too.
            roots.Add(isX86
                ? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
                : Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"));
        }
        else
        {
            roots.Add(isX86
                ? Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
                : Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"));

            // Fallback: try the other view too.
            roots.Add(isX86
                ? Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
                : Registry.CurrentUser.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"));
        }

        foreach (var root in roots.Where(r => r is not null))
        {
            try
            {
                using var sub = root!.OpenSubKey(tail);
                if (sub is null)
                {
                    continue;
                }

                var dn = (sub.GetValue("DisplayName") as string) ?? string.Empty;
                var dv = (sub.GetValue("DisplayVersion") as string) ?? string.Empty;
                var installLocation = (sub.GetValue("InstallLocation") as string) ?? string.Empty;
                var displayIcon = (sub.GetValue("DisplayIcon") as string) ?? string.Empty;
                var uninstallString = (sub.GetValue("UninstallString") as string) ?? string.Empty;

                if (string.IsNullOrWhiteSpace(dn))
                {
                    dn = tail;
                }

                return new InstallLocationInfo
                {
                    DisplayName = dn,
                    DisplayVersion = dv,
                    InstallLocation = installLocation,
                    DisplayIcon = displayIcon,
                    UninstallString = uninstallString,
                };
            }
            catch
            {
                // ignore and keep trying
            }
        }

        return null;
    }

    private static int Score(string wantedName, string wantedVersion, string candidateName, string candidateVersion)
    {
        var wName = wantedName.Trim();
        var cName = candidateName.Trim();
        var wVer = (wantedVersion ?? string.Empty).Trim();
        var cVer = (candidateVersion ?? string.Empty).Trim();

        var score = 0;

        if (cName.Equals(wName, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }
        else if (cName.Contains(wName, StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }
        else if (wName.Contains(cName, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }
        else
        {
            return 0;
        }

        if (wVer.Length > 0 && cVer.Length > 0)
        {
            if (cVer.Equals(wVer, StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
            }
            else if (cVer.StartsWith(wVer, StringComparison.OrdinalIgnoreCase) || wVer.StartsWith(cVer, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }
        }

        return score;
    }
}
