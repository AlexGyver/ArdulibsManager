namespace ArdulibsManager.Models;

public sealed class LibraryProperties
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Maintainer { get; set; }
    public string? Sentence { get; set; }
    public string? Paragraph { get; set; }
    public string? Url { get; set; }
    public string? Architectures { get; set; }
    public string? Depends { get; set; }

    public IReadOnlyList<string> DependencyNames => ParseDepends(Depends);

    public static LibraryProperties Parse(string text)
    {
        var props = new LibraryProperties();
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim().ToLowerInvariant();
            var value = line[(idx + 1)..].Trim();
            switch (key)
            {
                case "name": props.Name = value; break;
                case "version": props.Version = value; break;
                case "maintainer": props.Maintainer = value; break;
                case "sentence": props.Sentence = value; break;
                case "paragraph": props.Paragraph = value; break;
                case "url": props.Url = value; break;
                case "architectures": props.Architectures = value; break;
                case "depends": props.Depends = value; break;
            }
        }
        return props;
    }

    private static IReadOnlyList<string> ParseDepends(string? depends)
    {
        if (string.IsNullOrWhiteSpace(depends)) return Array.Empty<string>();

        return depends
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x =>
            {
                var idx = x.IndexOf('(');
                return idx >= 0 ? x[..idx].Trim() : x.Trim();
            })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
