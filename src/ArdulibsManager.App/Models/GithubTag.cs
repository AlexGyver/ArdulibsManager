namespace ArdulibsManager.Models;

public sealed class GithubTag
{
    public required string Name { get; init; }
    public string DisplayName => Name;
}
