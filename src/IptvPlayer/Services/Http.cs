using System.Net.Http;
using System.Net;

namespace IptvPlayer.Services;

public static class Http
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 16,
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
        // Certains fournisseurs bloquent les User-Agent inconnus.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("VLC/3.0.21 LibVLC/3.0.21");
        return c;
    }
}
