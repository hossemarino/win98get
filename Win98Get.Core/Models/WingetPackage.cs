namespace Win98Get.Models;

public sealed class WingetPackage
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string AvailableVersion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public bool HasUpgradeAvailable => !string.IsNullOrWhiteSpace(AvailableVersion);
}
