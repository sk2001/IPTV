using System.IO;
using System.IO.Compression;
using System.Text.Json;
using IptvPlayer.Models;

namespace IptvPlayer.Services;

/// <summary>Charge le catalogue d'un compte : cache disque (instantané) puis rafraîchissement réseau.</summary>
public sealed class CatalogService
{
    private static string CacheFile(Account a) =>
        Path.Combine(AccountStore.DataDir, "cache", a.Id + ".json.gz");

    public static Task<Catalog?> LoadCacheAsync(Account a) => Task.Run(() =>
    {
        try
        {
            var f = CacheFile(a);
            if (!File.Exists(f)) return null;
            using var fs = File.OpenRead(f);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            var cat = JsonSerializer.Deserialize<Catalog>(gz);
            return cat is { Version: Catalog.CurrentVersion } ? cat : null;   // ancien format : on re-télécharge
        }
        catch { return null; }
    });

    public static async Task<Catalog> FetchAsync(Account a, IProgress<string>? progress, CancellationToken ct)
    {
        var cat = a.Type == AccountType.Xtream
            ? await new XtreamClient(a).FetchCatalogAsync(progress, ct).ConfigureAwait(false)
            : await M3uParser.LoadAsync(a.Url, ct).ConfigureAwait(false);
        cat.FetchedAt = DateTime.UtcNow;
        cat.Version = Catalog.CurrentVersion;
        _ = Task.Run(() => SaveCache(a, cat), CancellationToken.None);
        return cat;
    }

    private static void SaveCache(Account a, Catalog cat)
    {
        try
        {
            var f = CacheFile(a);
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            using var fs = File.Create(f);
            using var gz = new GZipStream(fs, CompressionLevel.Fastest);
            JsonSerializer.Serialize(gz, cat);
        }
        catch { /* cache best-effort */ }
    }

    public static void DeleteCache(Account a)
    {
        try { File.Delete(CacheFile(a)); } catch { }
        try { File.Delete(EpgService.CacheFile(a)); } catch { }
    }
}
