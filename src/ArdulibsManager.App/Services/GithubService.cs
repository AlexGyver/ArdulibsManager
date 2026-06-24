using System.Net.Http;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using ArdulibsManager.Models;

namespace ArdulibsManager.Services;

public sealed class GithubService
{
    private readonly HttpClient _http;
    private readonly SettingsService _settings;
    private readonly Dictionary<string, (DateTimeOffset CreatedAt, IReadOnlyList<GithubTag> Tags)> _tagsCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan TagsCacheTtl = TimeSpan.FromMinutes(30);

    public GithubService(HttpClient http, SettingsService settings)
    {
        _http = http;
        _settings = settings;
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ArdulibsManager", "0.1"));
    }

    private HttpRequestMessage Request(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(_settings.Current.GitHubToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Current.GitHubToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return req;
    }

    public async Task<IReadOnlyList<GithubTag>> GetTagsAsync(GithubRepository repo, CancellationToken ct = default)
    {
        var cacheKey = repo.FullName;
        if (_tagsCache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.CreatedAt < TagsCacheTtl)
            return cached.Tags;

        var url = $"https://api.github.com/repos/{repo.Owner}/{repo.Name}/tags?per_page=100";
        using var req = Request(HttpMethod.Get, url);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var tags = new List<GithubTag>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("name", out var n))
                tags.Add(new GithubTag { Name = n.GetString() ?? string.Empty });
        }
        var sorted = VersionService.SortTags(tags);
        _tagsCache[cacheKey] = (DateTimeOffset.UtcNow, sorted);
        return sorted;
    }

    public async Task DownloadZipballAsync(GithubRepository repo, string tagOrRef, string targetZipPath, CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{repo.Owner}/{repo.Name}/zipball/{Uri.EscapeDataString(tagOrRef)}";
        using var req = Request(HttpMethod.Get, url);
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        await using var input = await res.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(targetZipPath);
        await input.CopyToAsync(output, ct);
    }

    public async Task<string?> GetLatestTagNameAsync(GithubRepository repo, CancellationToken ct = default)
        => (await GetTagsAsync(repo, ct)).FirstOrDefault()?.Name;
}
