using System.Net.Http;
using System.Text.RegularExpressions;
using ArdulibsManager.Models;

namespace ArdulibsManager.Services;

public sealed class RepositoryRegistryService
{
    private readonly HttpClient _http;
    private readonly CacheService _cache;
    private readonly SettingsService _settings;

    public RepositoryRegistryService(HttpClient http, CacheService cache, SettingsService settings)
    {
        _http = http;
        _cache = cache;
        _settings = settings;
    }

    public async Task<IReadOnlyList<GithubRepository>> LoadRepositoriesAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var registryUrl = string.IsNullOrWhiteSpace(_settings.Current.RepositoryListUrl)
            ? AppSettings.DefaultRepositoryListUrl
            : _settings.Current.RepositoryListUrl.Trim();

        var cacheFile = _cache.GetPath("repositories_" + Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(registryUrl)) + ".txt");
        string text;
        if (!forceRefresh && _cache.IsFresh(cacheFile, TimeSpan.FromHours(_settings.Current.CacheTtlHours)))
        {
            text = await File.ReadAllTextAsync(cacheFile, ct);
        }
        else
        {
            text = await _http.GetStringAsync(registryUrl, ct);
            await File.WriteAllTextAsync(cacheFile, text, ct);
        }

        return text.Replace("\r\n", "\n")
            .Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && !x.StartsWith('#'))
            .Select(ParseGithubUrl)
            .Where(x => x is not null)
            .Cast<GithubRepository>()
            .DistinctBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Name)
            .ToList();
    }

    public static GithubRepository? ParseGithubUrl(string url)
    {
        var match = Regex.Match(url, @"github\.com[:/](?<owner>[^/\s]+)/(?<repo>[^/\s\.]+)(?:\.git)?", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return new GithubRepository
        {
            Url = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"https://github.com/{match.Groups["owner"].Value}/{match.Groups["repo"].Value}",
            Owner = match.Groups["owner"].Value,
            Name = match.Groups["repo"].Value
        };
    }
}
