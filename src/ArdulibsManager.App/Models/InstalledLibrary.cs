using ArdulibsManager.Infrastructure;

namespace ArdulibsManager.Models;

public sealed class InstalledLibrary : ObservableObject
{
    private string? _latestVersion;
    private bool _isChecking;
    private bool _hasUpdate;
    private string? _status;

    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? Maintainer { get; init; }
    public string? Url { get; init; }
    public required string LocalPath { get; init; }
    public string? RepositoryFullName { get; init; }

    public string? LatestVersion
    {
        get => _latestVersion;
        set => SetProperty(ref _latestVersion, value);
    }

    public bool HasUpdate
    {
        get => _hasUpdate;
        set => SetProperty(ref _hasUpdate, value);
    }

    public bool IsChecking
    {
        get => _isChecking;
        set => SetProperty(ref _isChecking, value);
    }

    public string? Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}
