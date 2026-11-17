using System.IO;
using System.Text.RegularExpressions;
using IptvPlayer.Models;

namespace IptvPlayer.Services;

public static partial class M3uParser
{
    [GeneratedRegex("([\\w-]+)=\"([^\"]*)\"")]
    private static partial Regex AttrRegex();

    public static async Task<Catalog> LoadAsync(string urlOrPath, CancellationToken ct)
    {
        string text;
        if (File.Exists(urlOrPath)) text = await File.ReadAllTextAsync(urlOrPath, ct).ConfigureAwait(false);
        else text = await Http.Client.GetStringAsync(urlOrPath, ct).ConfigureAwait(false);
        return Parse(text);
    }

    public static Catalog Parse(string text)
    {
        var cat = new Catalog();
        var groups = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        string? name = null, logo = null, group = null, tvgId = null;
        var n = 0;

        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in AttrRegex().Matches(line))
                {
                    var key = m.Groups[1].Value.ToLowerInvariant();
                    if ((key == "url-tvg" || key == "x-tvg-url") && cat.EpgUrl == null)
                        cat.EpgUrl = m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                }
            }
            else if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                logo = group = tvgId = null;
                foreach (Match m in AttrRegex().Matches(line))
                {
                    switch (m.Groups[1].Value.ToLowerInvariant())
                    {
                        case "tvg-logo": logo = m.Groups[2].Value; break;
                        case "group-title": group = m.Groups[2].Value; break;
                        case "tvg-id": tvgId = m.Groups[2].Value; break;
                    }
                }
                var comma = line.LastIndexOf(',');
                name = comma >= 0 ? line[(comma + 1)..].Trim() : "Sans nom";
            }
            else if (line[0] != '#' && name != null)
            {
                var g = string.IsNullOrWhiteSpace(group) ? Localization.Loc.T("m3u.uncategorized") : group!;
                if (!groups.TryGetValue(g, out var c))
                {
                    c = new Category { Id = g, Name = g, Kind = ContentKind.Live };
                    groups[g] = c;
                    cat.Categories.Add(c);
                }
                c.Count++;
                cat.Items.Add(new MediaItem
                {
                    Id = (++n).ToString(),
                    Name = name,
                    Logo = string.IsNullOrWhiteSpace(logo) ? null : logo,
                    CategoryId = g,
                    Kind = ContentKind.Live,
                    EpgId = string.IsNullOrWhiteSpace(tvgId) ? null : tvgId,
                    StreamUrl = line,
                });
                name = null;
            }
        }
        return cat;
    }
}
