namespace ArdulibsManager.Services;

public sealed class CacheService
{
    public string AppCacheDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArdulibsManager",
        "cache");

    public CacheService() => Directory.CreateDirectory(AppCacheDir);

    public string GetPath(string fileName)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
        return Path.Combine(AppCacheDir, fileName);
    }

    public bool IsFresh(string path, TimeSpan ttl)
        => File.Exists(path) && DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path) < ttl;
}
