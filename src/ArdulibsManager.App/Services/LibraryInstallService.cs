using System.IO.Compression;
using System.Text.Json;
using ArdulibsManager.Models;

namespace ArdulibsManager.Services;

public sealed class LibraryInstallService
{
    private readonly GithubService _github;
    private readonly SettingsService _settings;

    public LibraryInstallService(GithubService github, SettingsService settings)
    {
        _github = github;
        _settings = settings;
    }

    public async Task<InstalledLibrary> InstallAsync(GithubRepository repo, string tag, string librariesPath, IProgress<string>? log = null, string? targetPathOverride = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(librariesPath);
        var tempRoot = Path.Combine(Path.GetTempPath(), "ArdulibsManager", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var zipPath = Path.Combine(tempRoot, "repo.zip");
        var extractPath = Path.Combine(tempRoot, "extract");

        try
        {
            log?.Report($"Скачивание {repo.FullName} {tag}...");
            await _github.DownloadZipballAsync(repo, tag, zipPath, ct);

            log?.Report("Распаковка...");
            ZipFile.ExtractToDirectory(zipPath, extractPath);

            var propsPath = Directory.EnumerateFiles(extractPath, "library.properties", SearchOption.AllDirectories)
                .OrderBy(p => p.Count(ch => ch == Path.DirectorySeparatorChar))
                .FirstOrDefault();

            if (propsPath is null)
                throw new InvalidOperationException("В архиве не найден library.properties. Это может быть не Arduino-библиотека.");

            var libRoot = Path.GetDirectoryName(propsPath)!;
            var props = LibraryProperties.Parse(await File.ReadAllTextAsync(propsPath, ct));
            var folderName = SanitizeFolderName(props.Name ?? repo.Name);
            var targetPath = !string.IsNullOrWhiteSpace(targetPathOverride)
                ? targetPathOverride
                : Path.Combine(librariesPath, folderName);
            var backupPath = CreateBackupPath(repo, tag, targetPath);

            if (Directory.Exists(targetPath))
            {
                if (_settings.Current.BackupBeforeReplace)
                {
                    log?.Report("Создание backup...");
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    Directory.Move(targetPath, backupPath);
                }
                else
                {
                    Directory.Delete(targetPath, recursive: true);
                }
            }

            try
            {
                log?.Report("Копирование в папку libraries...");
                CopyDirectory(libRoot, targetPath);
                await WriteManagerMetadataAsync(targetPath, repo, tag, ct);

                if (Directory.Exists(backupPath))
                    log?.Report($"Backup сохранён: {backupPath}");
            }
            catch
            {
                if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);
                if (Directory.Exists(backupPath)) Directory.Move(backupPath, targetPath);
                throw;
            }

            log?.Report("Готово.");
            return new InstalledLibrary
            {
                Name = props.Name ?? repo.Name,
                Version = props.Version ?? tag,
                Maintainer = props.Maintainer,
                Url = props.Url ?? repo.Url,
                LocalPath = targetPath,
                RepositoryFullName = repo.FullName,
                Status = "Установлено"
            };
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }

    public Task RemoveAsync(InstalledLibrary lib)
    {
        if (Directory.Exists(lib.LocalPath)) Directory.Delete(lib.LocalPath, recursive: true);
        return Task.CompletedTask;
    }

    private static string CreateBackupPath(GithubRepository repo, string tag, string targetPath)
    {
        var appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArdulibsManager",
            "Backups");
        var safeRepo = SanitizeFolderName(repo.FullName.Replace('/', '_'));
        var safeTag = SanitizeFolderName(tag);
        var folder = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(appDir, safeRepo, $"{folder}_{safeTag}_{DateTime.Now:yyyyMMdd_HHmmss}");
    }

    private static async Task WriteManagerMetadataAsync(string targetPath, GithubRepository repo, string tag, CancellationToken ct)
    {
        var metadata = new
        {
            repo = repo.FullName,
            url = repo.Url,
            installedRef = tag,
            installedAt = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(targetPath, ".github-library-manager.json"), json, ct);
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, destination));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination), overwrite: true);
        }
    }
}
