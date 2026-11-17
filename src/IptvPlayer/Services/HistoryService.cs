using System.IO;
using System.Text.Json;
using IptvPlayer.Models;

namespace IptvPlayer.Services;

/// <summary>Historique de visionnage des films et séries (history.json dans %AppData%\IptvPlayer).</summary>
public sealed class HistoryService
{
    /// <summary>Service actif, lu par <see cref="MediaItem"/> pour afficher « vu » / progression.</summary>
    public static HistoryService? Current { get; private set; }

    /// <summary>Compte dont on affiche le catalogue.</summary>
    public static Guid CurrentAccount { get; set; }

    private const int MaxEntries = 5000;
    private static readonly string FilePath = Path.Combine(AccountStore.DataDir, "history.json");

    private readonly Dictionary<string, WatchEntry> _entries = new();
    private readonly object _lock = new();
    private bool _dirty;

    public HistoryService()
    {
        Load();
        Current = this;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var list = JsonSerializer.Deserialize<List<WatchEntry>>(File.ReadAllText(FilePath)) ?? new();
            foreach (var e in list) _entries[e.Key] = e;
        }
        catch { /* historique corrompu : on repart de zéro */ }
    }

    public WatchEntry? Get(Guid account, ContentKind kind, string itemId, string? episodeId = null)
    {
        lock (_lock) return _entries.GetValueOrDefault(WatchEntry.MakeKey(account, kind, itemId, episodeId));
    }

    /// <summary>Dernier épisode regardé d'une série (le plus récent).</summary>
    public WatchEntry? LastForSeries(Guid account, string seriesId)
    {
        lock (_lock)
            return _entries.Values
                .Where(e => e.AccountId == account && e.Kind == ContentKind.Series && e.ItemId == seriesId)
                .OrderByDescending(e => e.LastWatched).FirstOrDefault();
    }

    public List<WatchEntry> EpisodesOf(Guid account, string seriesId)
    {
        lock (_lock)
            return _entries.Values.Where(e => e.AccountId == account && e.Kind == ContentKind.Series && e.ItemId == seriesId).ToList();
    }

    /// <summary>Éléments (films, ou séries regroupées) triés du plus récent au plus ancien.</summary>
    public List<WatchEntry> Recent(Guid account, ContentKind kind)
    {
        lock (_lock)
            return _entries.Values
                .Where(e => e.AccountId == account && e.Kind == kind)
                .GroupBy(e => e.ItemId)
                .Select(g => g.OrderByDescending(e => e.LastWatched).First())
                .OrderByDescending(e => e.LastWatched)
                .ToList();
    }

    /// <summary>Met à jour la position d'un média ; terminé à 92 % (ou à moins d'une minute de la fin).</summary>
    public void Update(WatchContext ctx, long positionMs, long durationMs)
    {
        if (!ctx.TracksHistory) return;
        lock (_lock)
        {
            var key = WatchEntry.MakeKey(ctx.AccountId, ctx.Kind, ctx.ItemId, ctx.EpisodeId);
            if (!_entries.TryGetValue(key, out var e))
            {
                e = new WatchEntry
                {
                    AccountId = ctx.AccountId, Kind = ctx.Kind, ItemId = ctx.ItemId, EpisodeId = ctx.EpisodeId,
                };
                _entries[key] = e;
            }
            e.Name = ctx.Name;
            e.Logo = ctx.Logo;
            e.Season = ctx.Season;
            e.Number = ctx.Number;
            e.EpisodeTitle = ctx.EpisodeTitle;
            e.DurationMs = durationMs;
            e.LastWatched = DateTime.Now;

            var done = durationMs > 0 && (positionMs >= durationMs * 0.92 || durationMs - positionMs < 60_000);
            e.Completed = done;
            e.PositionMs = done ? 0 : positionMs;
            _dirty = true;
            Trim();
        }
    }

    public void SetWatched(WatchContext ctx, bool watched)
    {
        lock (_lock)
        {
            var key = WatchEntry.MakeKey(ctx.AccountId, ctx.Kind, ctx.ItemId, ctx.EpisodeId);
            if (!watched) { _entries.Remove(key); _dirty = true; return; }
            if (!_entries.TryGetValue(key, out var e))
            {
                e = new WatchEntry { AccountId = ctx.AccountId, Kind = ctx.Kind, ItemId = ctx.ItemId, EpisodeId = ctx.EpisodeId };
                _entries[key] = e;
            }
            e.Name = ctx.Name; e.Logo = ctx.Logo; e.Season = ctx.Season; e.Number = ctx.Number; e.EpisodeTitle = ctx.EpisodeTitle;
            e.Completed = true; e.PositionMs = 0; e.LastWatched = DateTime.Now;
            _dirty = true;
        }
    }

    /// <summary>Retire un film ou une série entière (tous les épisodes) de l'historique.</summary>
    public void RemoveItem(Guid account, ContentKind kind, string itemId)
    {
        lock (_lock)
        {
            var keys = _entries.Where(kv => kv.Value.AccountId == account && kv.Value.Kind == kind && kv.Value.ItemId == itemId)
                               .Select(kv => kv.Key).ToList();
            foreach (var k in keys) _entries.Remove(k);
            if (keys.Count > 0) _dirty = true;
        }
    }

    /// <summary>Copie de toutes les entrées (pour la synchronisation).</summary>
    public List<WatchEntry> Snapshot() { lock (_lock) return _entries.Values.ToList(); }

    /// <summary>Applique des changements reçus d'un autre appareil.</summary>
    public void ApplySync(IEnumerable<WatchEntry> upserts, IEnumerable<string> deleteKeys)
    {
        lock (_lock)
        {
            foreach (var k in deleteKeys) _entries.Remove(k);
            foreach (var e in upserts) _entries[e.Key] = e;
            _dirty = true;
            Trim();
        }
        Save();
    }

    public void Clear()
    {
        lock (_lock) { _entries.Clear(); _dirty = true; }
        Save();
    }

    private void Trim()
    {
        if (_entries.Count <= MaxEntries) return;
        foreach (var k in _entries.OrderBy(kv => kv.Value.LastWatched).Take(_entries.Count - MaxEntries).Select(kv => kv.Key).ToList())
            _entries.Remove(k);
    }

    public void Save()
    {
        string json;
        lock (_lock)
        {
            if (!_dirty) return;
            json = JsonSerializer.Serialize(_entries.Values.ToList());
            _dirty = false;
        }
        try
        {
            Directory.CreateDirectory(AccountStore.DataDir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { }
    }
}
