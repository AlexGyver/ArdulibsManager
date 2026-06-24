namespace ArdulibsManager.Models;

public sealed class GithubRepository
{
    public required string Url { get; init; }
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public string FullName => $"{Owner}/{Name}";
}
