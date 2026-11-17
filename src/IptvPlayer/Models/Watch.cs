using System.Text.Json.Serialization;

namespace IptvPlayer.Models;

/// <summary>Une ligne d'historique : un film, ou un épisode d'une série.</summary>
public sealed class WatchEntry
{
    public Guid AccountId { get; set; }
    public ContentKind Kind { get; set; }

    /// <summary>Id du film, ou de la série (les épisodes sont distingués par <see cref="EpisodeId"/>).</summary>
    public string ItemId { get; set; } = "";
    public string? EpisodeId { get; set; }
    public int Season { get; set; }
    public int Number { get; set; }
    public string? EpisodeTitle { get; set; }

    public string Name { get; set; } = "";
    public string? Logo { get; set; }

    public long PositionMs { get; set; }
    public long DurationMs { get; set; }
    public bool Completed { get; set; }
    public DateTime LastWatched { get; set; }

    [JsonIgnore] public string Key => MakeKey(AccountId, Kind, ItemId, EpisodeId);

    [JsonIgnore]
    public double Progress => DurationMs > 0 ? Math.Clamp(PositionMs / (double)DurationMs, 0, 1) : 0;

    /// <summary>Entamé mais pas terminé (assez avancé pour proposer de reprendre).</summary>
    [JsonIgnore] public bool InProgress => !Completed && PositionMs >= 30_000 && Progress < 0.97;

    public static string MakeKey(Guid account, ContentKind kind, string itemId, string? episodeId) =>
        $"{account:N}|{(int)kind}|{itemId}|{episodeId}";
}

/// <summary>Ce que la vue doit savoir sur le média lancé, pour suivre la progression et reprendre la lecture.</summary>
public sealed class WatchContext
{
    public Guid AccountId { get; init; }
    public ContentKind Kind { get; init; }
    public string ItemId { get; init; } = "";
    public string? EpisodeId { get; init; }
    public int Season { get; init; }
    public int Number { get; init; }
    public string? EpisodeTitle { get; init; }
    public string Name { get; init; } = "";
    public string? Logo { get; init; }

    /// <summary>Position à reprendre (ms), 0 pour commencer au début.</summary>
    public long ResumeMs { get; init; }

    /// <summary>Élément du catalogue concerné (pour rafraîchir son affichage).</summary>
    public MediaItem? Item { get; init; }

    public bool TracksHistory => Kind != ContentKind.Live;
}
