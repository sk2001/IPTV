using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IptvPlayer.Services.Remote;

/// <summary>Un enregistrement synchronisé (protocole v1, §8) ; <c>Data</c> est null pour une suppression.</summary>
public sealed record SyncRecord(string Id, long UpdatedAt, bool Deleted, JsonObject? Data, string By);

/// <summary>Lien entre le moteur et le stockage d'une collection.</summary>
public interface ISyncAdapter
{
    Dictionary<string, JsonObject> Current();
    void Apply(Dictionary<string, JsonObject> upserts, ISet<string> deletes);
    /// <summary>Horodatage naturel (ex. dernière lecture), null si inconnu.</summary>
    long? Stamp(string id, JsonObject data) => null;
}

public sealed class MetaEntry
{
    public long UpdatedAt { get; set; }
    public bool Deleted { get; set; }
    public string Hash { get; set; } = "";
    public string By { get; set; } = "";
}

/// <summary>Dates de modification connues par collection ; les changements locaux sont détectés à la synchronisation.</summary>
public sealed class SyncMeta
{
    private static readonly string FilePath = Path.Combine(AccountStore.DataDir, "sync-meta.json");
    private Dictionary<string, Dictionary<string, MetaEntry>> _data = new();
    private readonly bool _persist;

    public SyncMeta(bool persist = true)
    {
        _persist = persist;
        try { if (persist && File.Exists(FilePath)) _data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, MetaEntry>>>(File.ReadAllText(FilePath)) ?? new(); }
        catch { _data = new(); }
    }

    public Dictionary<string, MetaEntry> Of(string collection)
    {
        if (!_data.TryGetValue(collection, out var m)) _data[collection] = m = new();
        return m;
    }

    public void Save()
    {
        if (!_persist) return;
        try { Directory.CreateDirectory(AccountStore.DataDir); File.WriteAllText(FilePath, JsonSerializer.Serialize(_data)); } catch { }
    }
}

/// <summary>Même algorithme que l'application Android (SyncEngine.kt) : la dernière écriture gagne ; à égalité, l'identifiant d'appareil le plus grand.</summary>
public sealed class SyncEngine
{
    public const long RetentionMs = 90L * 24 * 3600 * 1000;
    private readonly string _deviceId;
    private readonly SyncMeta _meta;
    private readonly Dictionary<string, ISyncAdapter> _adapters;
    private readonly Func<long> _clock;

    public SyncEngine(string deviceId, SyncMeta meta, Dictionary<string, ISyncAdapter> adapters, Func<long>? clock = null)
    {
        _deviceId = deviceId; _meta = meta; _adapters = adapters; _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public IEnumerable<string> Collections => _adapters.Keys;

    public Dictionary<string, SyncRecord> Snapshot(string collection)
    {
        var res = new Dictionary<string, SyncRecord>();
        if (!_adapters.TryGetValue(collection, out var adapter)) return res;
        var m = _meta.Of(collection); var now = _clock(); var cur = adapter.Current();
        foreach (var (id, data) in cur)
        {
            var h = Hash(data);
            if (!m.TryGetValue(id, out var e) || e.Deleted || e.Hash != h)
                m[id] = e = new MetaEntry { UpdatedAt = adapter.Stamp(id, data) ?? now, Deleted = false, Hash = h, By = _deviceId };
            res[id] = new SyncRecord(id, e.UpdatedAt, false, data, e.By);
        }
        foreach (var (id, e0) in m.ToList())
        {
            if (cur.ContainsKey(id)) continue;
            var e = e0;
            if (!e.Deleted) m[id] = e = new MetaEntry { UpdatedAt = now, Deleted = true, By = _deviceId };
            if (now - e.UpdatedAt > RetentionMs) m.Remove(id); else res[id] = new SyncRecord(id, e.UpdatedAt, true, null, e.By);
        }
        _meta.Save();
        return res;
    }

    /// <summary>Empreinte rapide de l'etat local (nombre d'enregistrements et derniere date par collection).</summary>
    public string Signature(bool includeAccounts) => string.Join(";", _adapters.Keys.Where(c => c != "accounts" || includeAccounts)
        .OrderBy(c => c, StringComparer.Ordinal).Select(c =>
        {
            var r = Snapshot(c).Values.ToList();
            return $"{c}:{r.Count}:{(r.Count == 0 ? 0 : r.Max(x => x.UpdatedAt))}";
        }));

    public int Merge(string collection, IEnumerable<SyncRecord> incoming)
    {
        if (!_adapters.TryGetValue(collection, out var adapter)) return 0;
        var local = Snapshot(collection);
        var ups = new Dictionary<string, SyncRecord>(); var dels = new Dictionary<string, SyncRecord>();
        foreach (var r in incoming)
        {
            if (local.TryGetValue(r.Id, out var l))
            {
                var same = l.Deleted == r.Deleted && (r.Deleted || (l.Data != null && r.Data != null && Hash(l.Data) == Hash(r.Data)));
                if (same) continue;
                var wins = r.UpdatedAt > l.UpdatedAt || (r.UpdatedAt == l.UpdatedAt && string.CompareOrdinal(r.By, l.By) > 0);
                if (!wins) continue;
            }
            else if (r.Deleted) continue;
            if (r.Deleted) dels[r.Id] = r; else if (r.Data != null) ups[r.Id] = r;
        }
        if (ups.Count == 0 && dels.Count == 0) return 0;
        adapter.Apply(ups.ToDictionary(k => k.Key, k => k.Value.Data!), new HashSet<string>(dels.Keys));
        var m = _meta.Of(collection); var after = adapter.Current();
        foreach (var (id, r) in ups) m[id] = new MetaEntry { UpdatedAt = r.UpdatedAt, Deleted = false, Hash = Hash(after.GetValueOrDefault(id) ?? r.Data!), By = r.By };
        foreach (var (id, r) in dels) m[id] = new MetaEntry { UpdatedAt = r.UpdatedAt, Deleted = true, Hash = "", By = r.By };
        _meta.Save();
        return ups.Count + dels.Count;
    }

    public List<SyncRecord> MissingOn(string collection, Dictionary<string, SyncRecord> remote)
    {
        var res = new List<SyncRecord>();
        foreach (var l in Snapshot(collection).Values)
        {
            if (!remote.TryGetValue(l.Id, out var r)) { if (!l.Deleted) res.Add(l); continue; }
            if (l.Deleted != r.Deleted || (!l.Deleted && Hash(l.Data!) != Hash(r.Data!)))
                if (l.UpdatedAt > r.UpdatedAt || (l.UpdatedAt == r.UpdatedAt && string.CompareOrdinal(l.By, r.By) > 0)) res.Add(l);
        }
        return res;
    }

    // ------------------------------------------------------------------ Représentation

    /// <summary>Représentation canonique (clés triées) : même empreinte sur tous les appareils pour le même contenu.</summary>
    public static string Canon(JsonNode? n) => n switch
    {
        null => "null",
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"\"{p.Key}\":{Canon(p.Value)}")) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Canon)) + "]",
        _ => n.ToJsonString(),
    };

    public static string Hash(JsonObject o) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canon(o)))).ToLowerInvariant();

    public static string Iso(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    public static long ParseIso(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).ToUnixTimeMilliseconds();

    public static string ToJson(IEnumerable<SyncRecord> records)
    {
        var arr = new JsonArray();
        foreach (var r in records)
            arr.Add(new JsonObject { ["id"] = r.Id, ["updatedAt"] = Iso(r.UpdatedAt), ["deleted"] = r.Deleted, ["by"] = r.By, ["data"] = r.Data?.DeepClone() ?? new JsonObject() });
        return arr.ToJsonString();
    }

    public static List<SyncRecord> FromJson(string text) =>
        ((JsonArray)JsonNode.Parse(text)!).Select(n =>
        {
            var o = (JsonObject)n!;
            var deleted = o["deleted"]?.GetValue<bool>() ?? false;
            return new SyncRecord(o["id"]!.GetValue<string>(), ParseIso(o["updatedAt"]!.GetValue<string>()), deleted,
                deleted ? null : (JsonObject?)o["data"]?.DeepClone(), o["by"]?.GetValue<string>() ?? "");
        }).ToList();
}
