using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Xml;
using IptvPlayer.Models;

namespace IptvPlayer.Services;

/// <summary>Télécharge et met en cache le guide XMLTV d'un compte.</summary>
public static class EpgService
{
    private static readonly TimeSpan PastWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan FutureWindow = TimeSpan.FromHours(36);

    public static string CacheFile(Account a) => Path.Combine(AccountStore.DataDir, "cache", a.Id + ".epg.json.gz");

    public static Task<EpgData?> LoadCacheAsync(Account a) => Task.Run(() =>
    {
        try
        {
            var f = CacheFile(a);
            if (!File.Exists(f)) return null;
            using var fs = File.OpenRead(f);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<EpgData>(gz);
        }
        catch { return null; }
    });

    /// <summary>URL XMLTV du compte, ou null si aucune source n'est connue.</summary>
    public static string? SourceUrl(Account a, Catalog catalog)
    {
        if (a.Type == AccountType.Xtream) return new XtreamClient(a).XmltvUrl;
        return string.IsNullOrWhiteSpace(catalog.EpgUrl) ? null : catalog.EpgUrl;
    }

    public static async Task<EpgData?> FetchAsync(Account a, Catalog catalog, CancellationToken ct)
    {
        var url = SourceUrl(a, catalog);
        if (url == null) return null;

        // Chaînes du catalogue à retenir (on ignore le reste du XMLTV pour économiser la mémoire).
        var wantedIds = new HashSet<string>(StringComparer.Ordinal);
        var wantedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var i in catalog.Items)
        {
            if (i.Kind != ContentKind.Live) continue;
            if (!string.IsNullOrEmpty(i.EpgId)) wantedIds.Add(i.EpgId);
            var k = i.NameKey.Length > 0 ? i.NameKey : EpgData.Norm(i.Name);
            if (k.Length > 0) wantedNames.Add(k);
        }

        Stream net;
        HttpResponseMessage? resp = null;
        if (File.Exists(url)) net = File.OpenRead(url);                       // guide XMLTV local
        else
        {
            resp = await Http.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        }

        EpgData data;
        try { data = await Task.Run(() => Parse(net, wantedIds, wantedNames, ct), ct).ConfigureAwait(false); }
        finally { await net.DisposeAsync(); resp?.Dispose(); }
        data.FetchedAt = DateTime.UtcNow;
        _ = Task.Run(() => SaveCache(a, data), CancellationToken.None);
        return data;
    }

    private static EpgData Parse(Stream raw, HashSet<string> wantedIds, HashSet<string> wantedNames, CancellationToken ct)
    {
        // Détecte un flux gzip (xmltv.xml.gz) à l'octet magique.
        var buffered = new BufferedStream(raw, 64 * 1024);
        var head = new byte[2];
        var peek = new PeekStream(buffered);
        var n = peek.PeekRead(head, 2);
        Stream xmlStream = n == 2 && head[0] == 0x1F && head[1] == 0x8B
            ? new GZipStream(peek, CompressionMode.Decompress)
            : peek;

        var data = new EpgData();
        var now = DateTime.Now;
        var minEnd = now - PastWindow;
        var maxStart = now + FutureWindow;

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CheckCharacters = false,
        };

        using var xr = XmlReader.Create(xmlStream, settings);
        var names = new List<string>();

        while (!xr.EOF)
        {
            ct.ThrowIfCancellationRequested();
            if (xr.NodeType != XmlNodeType.Element) { xr.Read(); continue; }

            if (xr.Name == "channel")
            {
                var id = xr.GetAttribute("id") ?? "";
                names.Clear();
                using (var sub = xr.ReadSubtree())
                {
                    sub.Read();
                    while (!sub.EOF)
                    {
                        if (sub.NodeType == XmlNodeType.Element && sub.Name == "display-name")
                            names.Add(sub.ReadElementContentAsString());
                        else sub.Read();
                    }
                }
                xr.Read();

                var byName = names.Select(EpgData.Norm).Where(x => x.Length > 0).ToList();
                if (id.Length > 0 && (wantedIds.Contains(id) || byName.Any(wantedNames.Contains)))
                {
                    data.ById.TryAdd(id, new List<EpgEntry>());
                    foreach (var k in byName) data.NameToId.TryAdd(k, id);
                }
            }
            else if (xr.Name == "programme")
            {
                var ch = xr.GetAttribute("channel") ?? "";
                var start = ParseTime(xr.GetAttribute("start"));
                var stop = ParseTime(xr.GetAttribute("stop"));

                if (start == null || stop == null || stop <= minEnd || start >= maxStart
                    || !data.ById.TryGetValue(ch, out var list))
                {
                    xr.Skip();
                    continue;
                }

                string title = "", desc = "";
                using (var sub = xr.ReadSubtree())
                {
                    sub.Read();
                    while (!sub.EOF)
                    {
                        if (sub.NodeType == XmlNodeType.Element && sub.Name == "title" && title.Length == 0)
                            title = sub.ReadElementContentAsString();
                        else if (sub.NodeType == XmlNodeType.Element && sub.Name == "desc" && desc.Length == 0)
                            desc = sub.ReadElementContentAsString();
                        else sub.Read();
                    }
                }
                xr.Read();

                list.Add(new EpgEntry
                {
                    Title = title.Trim(),
                    Description = desc.Length == 0 ? null : (desc.Length > 400 ? desc[..400] + "…" : desc.Trim()),
                    Start = start.Value,
                    End = stop.Value,
                });
            }
            else xr.Read();
        }

        foreach (var l in data.ById.Values) l.Sort((x, y) => x.Start.CompareTo(y.Start));
        return data;
    }

    /// <summary>Format XMLTV : « 20261116180000 +0100 » (sans décalage → UTC).</summary>
    private static DateTime? ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        var len = s.Length >= 14 && char.IsDigit(s[13]) ? 14 : 12;
        if (s.Length < len) return null;
        var fmt = len == 14 ? "yyyyMMddHHmmss" : "yyyyMMddHHmm";
        if (!DateTime.TryParseExact(s[..len], fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return null;

        var offset = TimeSpan.Zero;
        var rest = s[len..].Trim();
        if (rest.Length >= 5 && (rest[0] == '+' || rest[0] == '-')
            && int.TryParse(rest.AsSpan(1, 2), out var hh) && int.TryParse(rest.AsSpan(3, 2), out var mm))
        {
            offset = new TimeSpan(hh, mm, 0);
            if (rest[0] == '-') offset = -offset;
        }
        return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), offset).LocalDateTime;
    }

    private static void SaveCache(Account a, EpgData data)
    {
        try
        {
            var f = CacheFile(a);
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            using var fs = File.Create(f);
            using var gz = new GZipStream(fs, CompressionLevel.Fastest);
            JsonSerializer.Serialize(gz, data);
        }
        catch { }
    }

    /// <summary>Stream en lecture seule permettant de « regarder » les premiers octets sans les consommer.</summary>
    private sealed class PeekStream : Stream
    {
        private readonly Stream _inner;
        private byte[] _peeked = Array.Empty<byte>();
        private int _pos;

        public PeekStream(Stream inner) => _inner = inner;

        public int PeekRead(byte[] buffer, int count)
        {
            var read = 0;
            while (read < count)
            {
                var r = _inner.Read(buffer, read, count - read);
                if (r <= 0) break;
                read += r;
            }
            _peeked = buffer.AsSpan(0, read).ToArray();
            _pos = 0;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos < _peeked.Length)
            {
                var n = Math.Min(count, _peeked.Length - _pos);
                Array.Copy(_peeked, _pos, buffer, offset, n);
                _pos += n;
                return n;
            }
            return _inner.Read(buffer, offset, count);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
