namespace ArdulibsManager.Models;

public sealed class DependencyPlanItem
{
    public required string DependencyName { get; init; }
    public required GithubRepository Repository { get; init; }
    public required string LatestTag { get; init; }
    public InstalledLibrary? InstalledLibrary { get; init; }
    public string Action => InstalledLibrary is null ? "установить" : "обновить";
    public string DisplayText => InstalledLibrary is null
        ? $"{DependencyName} → {Repository.FullName} @ {LatestTag}"
        : $"{DependencyName} → {Repository.FullName}: {InstalledLibrary.Version} → {LatestTag}";
}
