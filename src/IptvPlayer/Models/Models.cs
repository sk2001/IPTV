using System.Text.Json.Serialization;

namespace IptvPlayer.Models;

public enum AccountType { Xtream, M3u }

public enum ContentKind { Live, Movie, Series }

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public AccountType Type { get; set; } = AccountType.Xtream;

    /// <summary>URL du serveur Xtream (http://host:port) ou URL / chemin de la playlist M3U.</summary>
    public string Url { get; set; } = "";
    public string Username { get; set; } = "";

    /// <summary>Mot de passe en clair en mémoire uniquement (chiffré DPAPI sur disque).</summary>
    [JsonIgnore] public string Password { get; set; } = "";

    /// <summary>Valeur chiffrée DPAPI persistée.</summary>
    public string? PasswordProtected { get; set; }

    public override string ToString() => Name;
}

public sealed class Category : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private int _count;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ContentKind Kind { get; set; }
    public int Count { get => _count; set => SetProperty(ref _count, value); }
}

public sealed class MediaItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private bool _isFavorite;

    [JsonIgnore]
    public bool IsFavorite { get => _isFavorite; set => SetProperty(ref _isFavorite, value); }

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Logo { get; set; }
    public string CategoryId { get; set; } = "";
    public ContentKind Kind { get; set; }

    /// <summary>URL directe de lecture (live, film, ou M3U). Vide pour les séries (résolues à la demande).</summary>
    public string? StreamUrl { get; set; }
    public string? Extension { get; set; }
    public string? Rating { get; set; }

    /// <summary>Identifiant de chaîne dans le guide XMLTV (epg_channel_id / tvg-id).</summary>
    public string? EpgId { get; set; }

    [JsonIgnore] public string SearchKey { get; set; } = "";
    [JsonIgnore] public string NameKey { get; set; } = "";

    // ---- Émission en cours (calculée à la volée depuis EpgData.Current) ----

    [JsonIgnore]
    public EpgEntry? NowEntry
    {
        get
        {
            if (Kind != ContentKind.Live) return null;
            var list = EpgData.Current?.Find(this);
            return list == null ? null : EpgData.NowNext(list, DateTime.Now).now;
        }
    }

    /// <summary>Deuxième ligne de la liste : émission en cours (live) ou note (films/séries).</summary>
    [JsonIgnore]
    public string? SubText
    {
        get
        {
            if (Kind == ContentKind.Live)
            {
                var n = NowEntry;
                return n == null ? null : $"{n.Start:HH:mm}  {n.Title}";
            }
            return string.IsNullOrWhiteSpace(Rating) ? null : "★ " + Rating;
        }
    }

    [JsonIgnore]
    public double NowProgress
    {
        get
        {
            var n = NowEntry;
            if (n == null) return 0;
            var total = (n.End - n.Start).TotalSeconds;
            return total <= 0 ? 0 : Math.Clamp((DateTime.Now - n.Start).TotalSeconds / total, 0, 1);
        }
    }

    [JsonIgnore] public bool HasNow => NowEntry != null;

    public void RefreshEpg()
    {
        OnPropertyChanged(nameof(SubText));
        OnPropertyChanged(nameof(NowProgress));
        OnPropertyChanged(nameof(HasNow));
    }
}

public sealed class Episode
{
    public string Id { get; set; } = "";
    public int Season { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Display => $"S{Season:00}E{Number:00} · {Title}";
}

public sealed class Catalog
{
    public List<Category> Categories { get; set; } = new();
    public List<MediaItem> Items { get; set; } = new();
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>URL du guide XMLTV annoncée par une playlist M3U (url-tvg / x-tvg-url).</summary>
    public string? EpgUrl { get; set; }

    /// <summary>Version du format de cache (les caches plus anciens sont ignorés).</summary>
    public int Version { get; set; }
    public const int CurrentVersion = 2;
}

public sealed class EpgEntry
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    [JsonIgnore] public string Display => $"{Start:HH:mm}-{End:HH:mm}  {Title}";
}

/// <summary>Fichier d'enregistrement (.ts) listé dans l'onglet Enregistrements.</summary>
public sealed class RecordingFile
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime Date { get; set; }
    public long Size { get; set; }
    public bool IsActive { get; set; }
    public string SizeText => Services.RecordingService.FormatSize(Size);
    public string DateText => Date.ToString("ddd d MMM · HH:mm", System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>Ligne du guide TV : une chaîne et ses programmes.</summary>
public sealed class GuideRow
{
    public GuideRow(MediaItem channel, IReadOnlyList<EpgEntry> entries) { Channel = channel; Entries = entries; }
    public MediaItem Channel { get; }
    public IReadOnlyList<EpgEntry> Entries { get; }
}

/// <summary>Guide des programmes d'un compte (XMLTV filtré sur la fenêtre utile).</summary>
public sealed class EpgData
{
    /// <summary>Guide du compte actif (lu par MediaItem pour afficher « en cours »).</summary>
    public static volatile EpgData? Current;

    public Dictionary<string, List<EpgEntry>> ById { get; set; } = new();
    public Dictionary<string, string> NameToId { get; set; } = new();
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    public List<EpgEntry>? Find(MediaItem item)
    {
        List<EpgEntry>? list;
        if (!string.IsNullOrEmpty(item.EpgId) && ById.TryGetValue(item.EpgId, out list) && list.Count > 0) return list;
        if (item.NameKey.Length > 0 && NameToId.TryGetValue(item.NameKey, out var id)
            && ById.TryGetValue(id, out list) && list.Count > 0) return list;
        return null;
    }

    public static (EpgEntry? now, EpgEntry? next) NowNext(List<EpgEntry> list, DateTime at)
    {
        EpgEntry? now = null, next = null;
        foreach (var e in list)
        {
            if (e.Start <= at && e.End > at) now = e;
            else if (e.Start > at) { next = e; break; }
        }
        return (now, next);
    }

    /// <summary>Clé de rapprochement par nom : minuscules, lettres et chiffres uniquement.</summary>
    public static string Norm(string s)
    {
        Span<char> buf = stackalloc char[Math.Min(s.Length, 128)];
        var n = 0;
        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c)) continue;
            if (n >= buf.Length) break;
            buf[n++] = char.ToLowerInvariant(c);
        }
        return new string(buf[..n]);
    }
}
