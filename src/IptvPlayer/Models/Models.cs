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

    [JsonIgnore] public string SearchKey { get; set; } = "";
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
}

public sealed class EpgEntry
{
    public string Title { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string Display => $"{Start:HH:mm}-{End:HH:mm}  {Title}";
}
