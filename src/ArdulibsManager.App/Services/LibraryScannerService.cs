using ArdulibsManager.Models;

namespace ArdulibsManager.Services;

public sealed class LibraryScannerService
{
    public async Task<IReadOnlyList<InstalledLibrary>> ScanAsync(string librariesPath, CancellationToken ct = default)
    {
        var list = new List<InstalledLibrary>();
        if (!Directory.Exists(librariesPath)) return list;

        foreach (var dir in Directory.EnumerateDirectories(librariesPath))
        {
            ct.ThrowIfCancellationRequested();
            var folder = Path.GetFileName(dir);
            if (folder.StartsWith(".") || folder.Contains(".backup", StringComparison.OrdinalIgnoreCase) || folder.Equals("backups", StringComparison.OrdinalIgnoreCase))
                continue;

            var propsPath = Path.Combine(dir, "library.properties");
            if (!File.Exists(propsPath)) continue;
            try
            {
                var text = await File.ReadAllTextAsync(propsPath, ct);
                var props = LibraryProperties.Parse(text);
                var repo = !string.IsNullOrWhiteSpace(props.Url)
                    ? RepositoryRegistryService.ParseGithubUrl(props.Url!)
                    : null;

                list.Add(new InstalledLibrary
                {
                    Name = props.Name ?? Path.GetFileName(dir),
                    Version = props.Version,
                    Maintainer = props.Maintainer,
                    Url = props.Url,
                    LocalPath = dir,
                    RepositoryFullName = repo?.FullName,
                    Status = repo is null ? "Не GitHub / неизвестный источник" : "OK"
                });
            }
            catch (Exception ex)
            {
                list.Add(new InstalledLibrary
                {
                    Name = Path.GetFileName(dir),
                    LocalPath = dir,
                    Status = "Ошибка чтения library.properties: " + ex.Message
                });
            }
        }

        return list.OrderBy(x => x.Name).ToList();
    }
}
