using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.IO;

namespace IptvPlayer.Services.Remote;

public sealed class WrongPinException : Exception { public WrongPinException() : base("Wrong PIN") { } }

/// <summary>L'empreinte du certificat de l'appareil ne correspond plus a celle memorisee (appli reinstallee ou appareil remplace).</summary>
public sealed class CertificateChangedException : Exception
{
    public CertificateChangedException() : base(IptvPlayer.Localization.Loc.T("rem.certChanged")) { }
}

public sealed record PairingSession(string PairingId, string ClientNonce, string ServerNonce, string Fingerprint);

/// <summary>
/// Client du protocole de contrôle v1 : HTTPS avec <b>épinglage de l'empreinte</b> du certificat de la cible
/// (aucune autorité de confiance n'est utilisée). Le PC est contrôleur seulement : il n'écoute sur aucun port.
/// </summary>
public sealed class RemoteClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string? _expectedFp;
    private readonly string? _token;
    private readonly HttpClient _http;
    private readonly HttpClient _rawHttp;

    public string? SeenFingerprint { get; private set; }
    private bool _fingerprintMismatch;

    public RemoteClient(string host, int port, string? expectedFingerprint, string? token)
    {
        _host = host; _port = port; _expectedFp = expectedFingerprint?.ToLowerInvariant(); _token = token;
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            SslOptions = new SslClientAuthenticationOptions
            {
                // L'identité de la cible est garantie par l'empreinte épinglée, pas par un nom d'hôte ni une autorité.
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                {
                    if (cert == null) return false;
                    var fp = Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())).ToLowerInvariant();
                    SeenFingerprint = fp;
                    var ok = _expectedFp == null || PairCrypto.Equal(fp, _expectedFp);
                    if (!ok) _fingerprintMismatch = true;
                    return ok;
                },
            },
        };
        _http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(8) };
        _rawHttp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
    }

    public async Task<(int Status, JsonObject Body)> CallAsync(HttpMethod method, string path, JsonObject? body = null, CancellationToken ct = default)
    {
        var host = _host.Contains(':') ? $"[{_host}]" : _host;
        using var req = new HttpRequestMessage(method, $"https://{host}:{_port}/api/v1{path}");
        if (_token != null) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
        if (body != null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await SendAsync(_http, req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        JsonObject obj;
        try { obj = JsonNode.Parse(text) as JsonObject ?? new JsonObject(); } catch { obj = new JsonObject(); }
        return ((int)resp.StatusCode, obj);
    }

    /// <summary>Variante brute (tableaux JSON de la synchronisation) : retourne (code HTTP, texte).</summary>
    public async Task<(int Status, string Text)> CallRawAsync(HttpMethod method, string path, string? body = null, CancellationToken ct = default)
    {
        var host = _host.Contains(':') ? $"[{_host}]" : _host;
        using var req = new HttpRequestMessage(method, $"https://{host}:{_port}/api/v1{path}");
        if (_token != null) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
        if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await SendAsync(_rawHttp, req, ct);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage req, CancellationToken ct)
    {
        try { return await http.SendAsync(req, ct); }
        catch (HttpRequestException) when (_fingerprintMismatch) { throw new CertificateChangedException(); }
    }

    private static string Error(JsonObject o, int status) => o["error"]?["message"]?.GetValue<string>() ?? $"HTTP {status}";

    /// <summary>Étape 1 : la cible affiche un PIN à l'écran.</summary>
    public async Task<PairingSession> PairStartAsync(string deviceId, string deviceName, string platform)
    {
        var nonce = PairCrypto.RandomHex(16);
        var (code, o) = await CallAsync(HttpMethod.Post, "/pair/start", new JsonObject
        {
            ["deviceId"] = deviceId, ["deviceName"] = deviceName, ["platform"] = platform, ["clientNonce"] = nonce,
        });
        if (code != 200) throw new IOException(Error(o, code));
        return new PairingSession(o["pairingId"]!.GetValue<string>(), nonce, o["serverNonce"]!.GetValue<string>(),
            SeenFingerprint ?? throw new IOException("No TLS"));
    }

    /// <summary>Étape 2 : preuve du PIN saisi ; vérifie aussi la preuve du serveur.</summary>
    public async Task<(string Token, string ServerId, string ServerName)> PairConfirmAsync(PairingSession s, string pin)
    {
        var proof = PairCrypto.Proof(pin, false, s.PairingId, s.ClientNonce, s.ServerNonce, s.Fingerprint);
        var (code, o) = await CallAsync(HttpMethod.Post, "/pair/confirm", new JsonObject { ["pairingId"] = s.PairingId, ["proof"] = proof });
        if (code == 401) throw new WrongPinException();
        if (code != 200) throw new IOException(Error(o, code));
        var expected = PairCrypto.Proof(pin, true, s.PairingId, s.ClientNonce, s.ServerNonce, s.Fingerprint);
        if (!PairCrypto.Equal(expected, o["serverProof"]!.GetValue<string>())) throw new IOException("Server proof mismatch");
        return (o["token"]!.GetValue<string>(), o["serverId"]!.GetValue<string>(), o["serverName"]!.GetValue<string>());
    }

    public Task<(int Status, JsonObject Body)> StateAsync(CancellationToken ct = default) => CallAsync(HttpMethod.Get, "/state", null, ct);

    /// <summary>Envoie une commande ; retourne null si acceptée, sinon le message d'erreur.</summary>
    public async Task<string?> CommandAsync(JsonObject cmd)
    {
        var (code, o) = await CallAsync(HttpMethod.Post, "/command", cmd);
        return code == 200 ? null : Error(o, code);
    }

    public Task UnpairAsync() => CallAsync(HttpMethod.Delete, "/pair");

    public void Dispose() { _http.Dispose(); _rawHttp.Dispose(); }
}
