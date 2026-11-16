using System.Text;
using System.Text.Json;
using IptvPlayer.Models;

namespace IptvPlayer.Services;

/// <summary>Client de l'API Xtream Codes (player_api.php).</summary>
public sealed class XtreamClient
{
    private readonly string _base;
    private readonly string _user;
    private readonly string _pass;

    public XtreamClient(Account a)
    {
        _base = a.Url.Trim().TrimEnd('/');
        if (!_base.StartsWith("http", StringComparison.OrdinalIgnoreCase)) _base = "http://" + _base;
        _user = a.Username.Trim();
        _pass = a.Password;
    }

    private string Api(string action = "", string extra = "")
    {
        var u = $"{_base}/player_api.php?username={Uri.EscapeDataString(_user)}&password={Uri.EscapeDataString(_pass)}";
        if (action.Length > 0) u += $"&action={action}";
        return u + extra;
    }

    private async Task<JsonDocument> GetAsync(string url, CancellationToken ct)
    {
        await using var s = await Http.Client.GetStreamAsync(url, ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(s, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>Vérifie les identifiants. Retourne un message d'erreur, ou null si OK.</summary>
    public async Task<string?> TestAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = await GetAsync(Api(), ct).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("user_info", out var ui))
            {
                if (ui.TryGetProperty("auth", out var auth) && Str(auth) == "0")
                    return "Identifiants refusés par le serveur.";
                return null;
            }
            return "Réponse inattendue du serveur.";
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task<Catalog> FetchCatalogAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var cat = new Catalog();
        var kinds = new (ContentKind kind, string catAction, string itemAction)[]
        {
            (ContentKind.Live, "get_live_categories", "get_live_streams"),
            (ContentKind.Movie, "get_vod_categories", "get_vod_streams"),
            (ContentKind.Series, "get_series_categories", "get_series"),
        };

        // Les 6 requêtes partent en parallèle.
        var tasks = kinds.Select(async k => (k,
            cats: await SafeGet(Api(k.catAction), ct),
            items: await SafeGet(Api(k.itemAction), ct))).ToArray();

        foreach (var t in tasks)
        {
            var (k, cats, items) = await t.ConfigureAwait(false);
            progress?.Report(k.kind switch { ContentKind.Live => "Chaînes…", ContentKind.Movie => "Films…", _ => "Séries…" });

            if (cats is not null)
            {
                using (cats)
                    foreach (var c in cats.RootElement.EnumerateArrayOrEmpty())
                        cat.Categories.Add(new Category { Id = Str(c, "category_id"), Name = Str(c, "category_name"), Kind = k.kind });
            }
            if (items is not null)
            {
                using (items)
                    foreach (var e in items.RootElement.EnumerateArrayOrEmpty())
                        cat.Items.Add(ParseItem(e, k.kind));
            }
        }

        var counts = cat.Items.GroupBy(i => (i.Kind, i.CategoryId)).ToDictionary(g => g.Key, g => g.Count());
        foreach (var c in cat.Categories) c.Count = counts.GetValueOrDefault((c.Kind, c.Id));
        return cat;
    }

    private async Task<JsonDocument?> SafeGet(string url, CancellationToken ct)
    {
        try { return await GetAsync(url, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private MediaItem ParseItem(JsonElement e, ContentKind kind)
    {
        var item = new MediaItem { Kind = kind, CategoryId = Str(e, "category_id") };
        switch (kind)
        {
            case ContentKind.Live:
                item.Id = Str(e, "stream_id");
                item.Name = Str(e, "name");
                item.Logo = NullIfEmpty(Str(e, "stream_icon"));
                item.StreamUrl = $"{_base}/live/{Uri.EscapeDataString(_user)}/{Uri.EscapeDataString(_pass)}/{item.Id}.ts";
                break;
            case ContentKind.Movie:
                item.Id = Str(e, "stream_id");
                item.Name = Str(e, "name");
                item.Logo = NullIfEmpty(Str(e, "stream_icon"));
                item.Extension = NullIfEmpty(Str(e, "container_extension")) ?? "mp4";
                item.Rating = NullIfEmpty(Str(e, "rating"));
                item.StreamUrl = $"{_base}/movie/{Uri.EscapeDataString(_user)}/{Uri.EscapeDataString(_pass)}/{item.Id}.{item.Extension}";
                break;
            default:
                item.Id = Str(e, "series_id");
                item.Name = Str(e, "name");
                item.Logo = NullIfEmpty(Str(e, "cover"));
                item.Rating = NullIfEmpty(Str(e, "rating"));
                break;
        }
        return item;
    }

    public async Task<List<Episode>> GetEpisodesAsync(string seriesId, CancellationToken ct = default)
    {
        var list = new List<Episode>();
        using var doc = await GetAsync(Api("get_series_info", $"&series_id={seriesId}"), ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("episodes", out var eps)) return list;

        IEnumerable<JsonElement> all = eps.ValueKind switch
        {
            JsonValueKind.Object => eps.EnumerateObject().SelectMany(p => p.Value.EnumerateArrayOrEmpty()),
            JsonValueKind.Array => eps.EnumerateArray().SelectMany(p => p.EnumerateArrayOrEmpty()),
            _ => Array.Empty<JsonElement>(),
        };

        foreach (var e in all)
        {
            var ext = NullIfEmpty(Str(e, "container_extension")) ?? "mp4";
            var id = Str(e, "id");
            list.Add(new Episode
            {
                Id = id,
                Season = int.TryParse(Str(e, "season"), out var s) ? s : 0,
                Number = int.TryParse(Str(e, "episode_num"), out var n) ? n : 0,
                Title = Str(e, "title"),
                Url = $"{_base}/series/{Uri.EscapeDataString(_user)}/{Uri.EscapeDataString(_pass)}/{id}.{ext}",
            });
        }
        return list.OrderBy(x => x.Season).ThenBy(x => x.Number).ToList();
    }

    public async Task<List<EpgEntry>> GetEpgAsync(string streamId, CancellationToken ct = default)
    {
        var res = new List<EpgEntry>();
        try
        {
            using var doc = await GetAsync(Api("get_short_epg", $"&stream_id={streamId}&limit=4"), ct).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("epg_listings", out var l)) return res;
            foreach (var e in l.EnumerateArrayOrEmpty())
            {
                if (!long.TryParse(Str(e, "start_timestamp"), out var st) || !long.TryParse(Str(e, "stop_timestamp"), out var en)) continue;
                res.Add(new EpgEntry
                {
                    Title = DecodeB64(Str(e, "title")),
                    Start = DateTimeOffset.FromUnixTimeSeconds(st).LocalDateTime,
                    End = DateTimeOffset.FromUnixTimeSeconds(en).LocalDateTime,
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* EPG facultatif */ }
        return res;
    }

    private static string DecodeB64(string s)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        catch { return s; }
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    internal static string Str(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) ? Str(v) : "";

    internal static string Str(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "1",
        JsonValueKind.False => "0",
        _ => "",
    };
}

internal static class JsonExt
{
    public static IEnumerable<JsonElement> EnumerateArrayOrEmpty(this JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : Enumerable.Empty<JsonElement>();
}
