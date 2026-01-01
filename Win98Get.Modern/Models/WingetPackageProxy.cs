using Win98Get.Models;

namespace Win98Get.Modern.Models;

public sealed class WingetPackageProxy
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string AvailableVersion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;

    public static WingetPackageProxy From(WingetPackage p)
        => new()
        {
            Name = p.Name ?? string.Empty,
            Id = p.Id ?? string.Empty,
            Version = p.Version ?? string.Empty,
            AvailableVersion = p.AvailableVersion ?? string.Empty,
            Source = p.Source ?? string.Empty,
        };

    public WingetPackage ToWingetPackage()
        => new()
        {
            Name = Name,
            Id = Id,
            Version = Version,
            AvailableVersion = AvailableVersion,
            Source = Source,
        };
}
