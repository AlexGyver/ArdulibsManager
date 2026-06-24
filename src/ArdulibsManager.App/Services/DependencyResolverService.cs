using System.IO.Compression;
using ArdulibsManager.Models;

namespace ArdulibsManager.Services;

public sealed class DependencyResolverService
{
    private readonly GithubService _github;
    private readonly Dictionary<string, LibraryProperties> _propsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _latestCache = new(StringComparer.OrdinalIgnoreCase);

    public DependencyResolverService(GithubService github)
    {
        _github = github;
    }

    public async Task<IReadOnlyList<DependencyPlanItem>> BuildInstallPlanAsync(
        GithubRepository rootRepository,
        IReadOnlyList<GithubRepository> registry,
        IReadOnlyList<InstalledLibrary> installedLibraries,
        CancellationToken ct = default)
    {
        var plan = new List<DependencyPlanItem>();
        var visitedRepos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootRepository.FullName };
        var queuedDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await VisitRepositoryAsync(rootRepository, registry, installedLibraries, plan, visitedRepos, queuedDeps, ct);
        return plan;
    }

    private async Task VisitRepositoryAsync(
        GithubRepository repo,
        IReadOnlyList<GithubRepository> registry,
        IReadOnlyList<InstalledLibrary> installedLibraries,
        List<DependencyPlanItem> plan,
        HashSet<string> visitedRepos,
        HashSet<string> queuedDeps,
        CancellationToken ct)
    {
        var latest = await GetLatestTagAsync(repo, ct);
        if (string.IsNullOrWhiteSpace(latest)) return;

        var props = await GetPropertiesAsync(repo, latest, ct);
        foreach (var depName in props.DependencyNames)
        {
            if (!queuedDeps.Add(depName)) continue;

            var depRepo = FindRepositoryForDependency(depName, registry);
            if (depRepo is null) continue;
            if (!visitedRepos.Add(depRepo.FullName)) continue;

            var depLatest = await GetLatestTagAsync(depRepo, ct);
            if (string.IsNullOrWhiteSpace(depLatest)) continue;

            var installed = FindInstalled(depName, depRepo, installedLibraries);
            var needsInstallOrUpdate = installed is null || VersionService.IsNewer(depLatest, installed.Version);
            if (needsInstallOrUpdate)
            {
                plan.Add(new DependencyPlanItem
                {
                    DependencyName = depName,
                    Repository = depRepo,
                    LatestTag = depLatest,
                    InstalledLibrary = installed
                });
            }

            await VisitRepositoryAsync(depRepo, registry, installedLibraries, plan, visitedRepos, queuedDeps, ct);
        }
    }

    private async Task<string?> GetLatestTagAsync(GithubRepository repo, CancellationToken ct)
    {
        if (_latestCache.TryGetValue(repo.FullName, out var latest)) return latest;
        latest = await _github.GetLatestTagNameAsync(repo, ct);
        _latestCache[repo.FullName] = latest;
        return latest;
    }

    private async Task<LibraryProperties> GetPropertiesAsync(GithubRepository repo, string tag, CancellationToken ct)
    {
        var cacheKey = repo.FullName + "@" + tag;
        if (_propsCache.TryGetValue(cacheKey, out var cached)) return cached;

        var tempRoot = Path.Combine(Path.GetTempPath(), "ArdulibsManager", "deps", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var zipPath = Path.Combine(tempRoot, "repo.zip");
        var extractPath = Path.Combine(tempRoot, "extract");

        try
        {
            await _github.DownloadZipballAsync(repo, tag, zipPath, ct);
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            var propsPath = Directory.EnumerateFiles(extractPath, "library.properties", SearchOption.AllDirectories)
                .OrderBy(p => p.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
                .FirstOrDefault();

            var props = propsPath is null
                ? new LibraryProperties { Name = repo.Name, Url = repo.Url }
                : LibraryProperties.Parse(await File.ReadAllTextAsync(propsPath, ct));
            _propsCache[cacheKey] = props;
            return props;
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static GithubRepository? FindRepositoryForDependency(string dependencyName, IReadOnlyList<GithubRepository> registry)
    {
        var needle = Normalize(dependencyName);
        if (string.IsNullOrWhiteSpace(needle)) return null;

        GithubRepository? Match(Func<GithubRepository, bool> predicate) => registry.FirstOrDefault(predicate);

        return Match(r => Normalize(r.Name) == needle)
            ?? Match(r => Normalize(TrimCommonSuffixes(r.Name)) == needle)
            ?? Match(r => Normalize(r.FullName).EndsWith(needle, StringComparison.OrdinalIgnoreCase))
            ?? Match(r => Normalize(r.Name).Contains(needle, StringComparison.OrdinalIgnoreCase))
            ?? Match(r => needle.Contains(Normalize(TrimCommonSuffixes(r.Name)), StringComparison.OrdinalIgnoreCase));
    }

    private static InstalledLibrary? FindInstalled(string dependencyName, GithubRepository repo, IReadOnlyList<InstalledLibrary> installedLibraries)
    {
        var depNorm = Normalize(dependencyName);
        return installedLibraries.FirstOrDefault(x =>
            x.RepositoryFullName?.Equals(repo.FullName, StringComparison.OrdinalIgnoreCase) == true ||
            x.Url?.Contains(repo.FullName, StringComparison.OrdinalIgnoreCase) == true ||
            Normalize(x.Name) == depNorm ||
            Normalize(x.Name) == Normalize(repo.Name));
    }

    private static string TrimCommonSuffixes(string value)
    {
        foreach (var suffix in new[] { "_Library", "-Library", " Library", "_lib", "-lib" })
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return value[..^suffix.Length];
        }
        return value;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var chars = value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
        var normalized = new string(chars);
        foreach (var token in new[] { "arduino", "library", "lib" })
        {
            if (normalized.EndsWith(token, StringComparison.OrdinalIgnoreCase) && normalized.Length > token.Length + 2)
                normalized = normalized[..^token.Length];
        }
        return normalized;
    }
}
