using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;

namespace IptvPlayer.Services.Remote;

/// <summary>
/// Serveur <b>éphémère</b> de transfert de configuration : ouvert seulement pendant que la fenêtre « Partager » est affichée
/// (le PC n'écoute rien le reste du temps). Même protocole que les appareils Android : HTTPS à certificat éphémère épinglé,
/// appairage par PIN à usage unique (affiché aussi dans le QR code), jeton, routes /sync/{collection}.
/// </summary>
public sealed class ShareServer : IDisposable
{
    private readonly Func<string, string> _syncGet;                 // collection -> tableau JSON
    private readonly Func<string, string, int> _syncPut;            // collection, tableau JSON -> nombre appliqué
    private readonly string _deviceId, _deviceName;
    private readonly X509Certificate2 _cert;
    private TcpListener? _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();

    // Offre : PIN a usage unique (5 minutes), 10 essais au total.
    private readonly DateTime _expires = DateTime.UtcNow.AddMinutes(5);
    private int _failures, _starts;
    private DateTime _startsWindow = DateTime.UtcNow;
    private sealed record Pending(string Id, string ClientNonce, string ServerNonce, string DeviceId, string DeviceName);
    private Pending? _pending;
    private string? _tokenHash;
    private bool _offerUsed;

    public string Pin { get; } = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
    public string Fingerprint { get; }
    public int Port { get; private set; }
    public string DeviceId => _deviceId;
    public string DeviceName => _deviceName;
    public bool Paired { get; private set; }
    public string? PairedName { get; private set; }
    public int Collections { get; private set; }
    public event Action? Changed;

    public ShareServer(string deviceId, string deviceName, Func<string, string> syncGet, Func<string, string, int> syncPut)
    {
        _deviceId = deviceId; _deviceName = deviceName; _syncGet = syncGet; _syncPut = syncPut;
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=IPTV Player", key, HashAlgorithmName.SHA256);
        using var self = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        _cert = new X509Certificate2(self.Export(X509ContentType.Pfx));        // reimport : la cle privee doit etre utilisable par SChannel
        Fingerprint = Convert.ToHexString(SHA256.HashData(_cert.RawData)).ToLowerInvariant();
    }

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoop);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        _cert.Dispose();
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await _listener!.AcceptTcpClientAsync(_cts.Token); } catch { break; }
            _ = Task.Run(() => Serve(c));
        }
    }

    private static bool IsPrivate(IPAddress? a)
    {
        if (a == null) return false;
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (IPAddress.IsLoopback(a)) return true;
        var b = a.GetAddressBytes();
        if (b.Length == 4) return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        return a.IsIPv6LinkLocal || a.IsIPv6UniqueLocal;
    }

    private async Task Serve(TcpClient client)
    {
        using var _ = client;
        try
        {
            if (!IsPrivate((client.Client.RemoteEndPoint as IPEndPoint)?.Address)) return;
            client.ReceiveTimeout = client.SendTimeout = 30_000;
            using var ssl = new SslStream(client.GetStream(), false);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _cert, ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, _cts.Token);

            var (method, path, headers, body) = await ReadRequest(ssl);
            var (status, text) = Handle(method, path, headers, body);
            var bytes = Encoding.UTF8.GetBytes(text);
            var head = $"HTTP/1.1 {status} {Reason(status)}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
            await ssl.WriteAsync(Encoding.ASCII.GetBytes(head));
            await ssl.WriteAsync(bytes);
            await ssl.FlushAsync();
        }
        catch { }
    }

    private static string Reason(int s) => s switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 409 => "Conflict", 429 => "Too Many Requests", _ => "Error" };

    private static async Task<(string, string, Dictionary<string, string>, string)> ReadRequest(Stream s)
    {
        async Task<string> Line()
        {
            var sb = new StringBuilder(); var one = new byte[1];
            while (sb.Length < 8192)
            {
                if (await s.ReadAsync(one) == 0) throw new IOException();
                if (one[0] == '\n') return sb.ToString().TrimEnd('\r');
                sb.Append((char)one[0]);
            }
            throw new IOException();
        }
        var first = (await Line()).Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var h = await Line();
            if (h.Length == 0) break;
            var i = h.IndexOf(':'); if (i > 0) headers[h[..i].Trim()] = h[(i + 1)..].Trim();
            if (headers.Count > 50) throw new IOException();
        }
        var len = headers.TryGetValue("Content-Length", out var l) && int.TryParse(l, out var n) ? n : 0;
        if (len < 0 || len > 16_000_000) throw new IOException();
        var buf = new byte[len]; var got = 0;
        while (got < len) { var r = await s.ReadAsync(buf.AsMemory(got, len - got)); if (r == 0) throw new IOException(); got += r; }
        return (first[0], first[1].Split('?')[0], headers, Encoding.UTF8.GetString(buf));
    }

    private static string Err(string code, string msg) => new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = msg } }.ToJsonString();

    private (int, string) Handle(string method, string path, Dictionary<string, string> headers, string body)
    {
        if (!path.StartsWith("/api/v1/")) return (404, Err("not_found", "Unknown path"));
        var route = path[7..].TrimEnd('/');
        try
        {
            if (route == "/info" && method == "GET")
                return (200, new JsonObject { ["protocol"] = 1, ["id"] = _deviceId, ["name"] = _deviceName, ["platform"] = "windows", ["appVersion"] = "1", ["remoteEnabled"] = true, ["paired"] = Paired }.ToJsonString());
            if (route == "/pair/start" && method == "POST") return PairStart(JsonNode.Parse(body)!.AsObject());
            if (route == "/pair/confirm" && method == "POST") return PairConfirm(JsonNode.Parse(body)!.AsObject());

            // Le reste exige le jeton obtenu a l'appairage.
            var auth = headers.TryGetValue("Authorization", out var a) ? a.Replace("Bearer ", "").Trim() : "";
            lock (_lock) if (_tokenHash == null || auth.Length == 0 || !PairCrypto.Equal(Hash(auth), _tokenHash)) return (401, Err("unauthorized", "Pair this device first"));

            if (route.StartsWith("/sync/"))
            {
                var col = route[6..];
                if (col is not ("favorites" or "history" or "settings" or "accounts")) return (404, Err("not_found", "Unknown collection"));
                if (method == "GET") { var r = _syncGet(col); Count(); return (200, r); }
                if (method == "PUT") { var n = _syncPut(col, body); Count(); return (200, new JsonObject { ["applied"] = n }.ToJsonString()); }
            }
            if (route == "/state" && method == "GET") return (200, new JsonObject { ["protocol"] = 1, ["state"] = "idle" }.ToJsonString());
            if (route == "/pair" && method == "DELETE") { lock (_lock) _tokenHash = null; return (200, "{\"ok\":true}"); }
            return (404, Err("not_found", "Unknown route"));
        }
        catch (Exception e) { return (400, Err("bad_request", e.Message)); }
    }

    private void Count() { Collections++; Changed?.Invoke(); }
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private (int, string) PairStart(JsonObject b)
    {
        lock (_lock)
        {
            if (DateTime.UtcNow > _expires || _offerUsed) return (409, Err("busy", "Code expired or already used"));
            if (DateTime.UtcNow - _startsWindow > TimeSpan.FromMinutes(1)) { _startsWindow = DateTime.UtcNow; _starts = 0; }
            if (++_starts > 5) return (429, Err("rate_limited", "Too many attempts"));
            _pending = new Pending(PairCrypto.RandomHex(8), b["clientNonce"]!.GetValue<string>(), PairCrypto.RandomHex(16), b["deviceId"]!.GetValue<string>(), b["deviceName"]?.GetValue<string>() ?? "Device");
            return (200, new JsonObject { ["pairingId"] = _pending.Id, ["serverNonce"] = _pending.ServerNonce, ["expiresIn"] = 120 }.ToJsonString());
        }
    }

    private (int, string) PairConfirm(JsonObject b)
    {
        string name;
        string json;
        lock (_lock)
        {
            var p = _pending;
            if (p == null || p.Id != b["pairingId"]?.GetValue<string>() || DateTime.UtcNow > _expires || _offerUsed) return (409, Err("busy", "Pairing expired or cancelled"));
            var expected = PairCrypto.Proof(Pin, false, p.Id, p.ClientNonce, p.ServerNonce, Fingerprint);
            if (!PairCrypto.Equal(expected, (b["proof"]?.GetValue<string>() ?? "").ToLowerInvariant()))
            {
                if (++_failures >= 10) _offerUsed = true;                // trop d'essais : l'offre est annulee
                return (401, Err("unauthorized", "Wrong PIN"));
            }
            _pending = null; _offerUsed = true;                          // offre a usage unique
            var token = PairCrypto.RandomHex(32);
            _tokenHash = Hash(token);
            name = p.DeviceName;
            json = new JsonObject
            {
                ["token"] = token, ["serverId"] = _deviceId, ["serverName"] = _deviceName,
                ["serverProof"] = PairCrypto.Proof(Pin, true, p.Id, p.ClientNonce, p.ServerNonce, Fingerprint),
            }.ToJsonString();
        }
        Paired = true; PairedName = name; Changed?.Invoke();
        return (200, json);
    }

    /// <summary>Premiere adresse IPv4 privee de la machine (celle a mettre dans le QR code).</summary>
    public static string? LocalIPv4()
    {
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                     .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count))
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(ua.Address)) return ua.Address.ToString();
        return null;
    }

    public string Link(string ip) =>
        $"iptvplayer://pair?host={ip}&port={Port}&id={Uri.EscapeDataString(_deviceId)}&name={Uri.EscapeDataString(_deviceName)}&pin={Pin}&fp={Fingerprint}&plat=windows";
}
