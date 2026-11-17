using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace IptvPlayer.Services.Remote;

/// <summary>Appareil annoncé sur le réseau (service mDNS <c>_iptvplayer._tcp</c>).</summary>
public sealed record FoundTarget(string Id, string Name, string Host, int Port, string Platform);

/// <summary>
/// Découverte mDNS minimale (interrogation ponctuelle, pas d'annonce : le PC n'écoute rien).
/// Envoie une requête PTR multicast sur chaque interface IPv4 puis lit les réponses unicast.
/// </summary>
public static class MdnsBrowser
{
    private const string Service = "_iptvplayer._tcp.local";
    private static readonly IPEndPoint Group = new(IPAddress.Parse("224.0.0.251"), 5353);

    public static async Task<List<FoundTarget>> BrowseAsync(TimeSpan duration, CancellationToken ct = default)
    {
        var sockets = new List<UdpClient>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || !nic.SupportsMulticast || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                try
                {
                    var c = new UdpClient(new IPEndPoint(ua.Address, 0));
                    c.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, ua.Address.GetAddressBytes());
                    c.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                    sockets.Add(c);
                }
                catch { /* interface inutilisable */ }
            }
        }

        var query = BuildQuery();
        var packets = new List<(byte[] Data, IPAddress From)>();
        var gate = new object();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(duration);

        var readers = sockets.Select(s => Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var r = await s.ReceiveAsync(cts.Token);
                    lock (gate) packets.Add((r.Buffer, r.RemoteEndPoint.Address));
                }
            }
            catch { }
        })).ToList();

        // Deux envois (certains répondeurs ratent le premier).
        for (var i = 0; i < 2 && !cts.IsCancellationRequested; i++)
        {
            foreach (var s in sockets) { try { await s.SendAsync(query, query.Length, Group); } catch { } }
            try { await Task.Delay(900, cts.Token); } catch { }
        }
        try { await Task.Delay(Timeout.Infinite, cts.Token); } catch { }
        foreach (var s in sockets) s.Dispose();
        await Task.WhenAll(readers);

        lock (gate) return Parse(packets);
    }

    // ------------------------------------------------------------------ Paquets DNS

    private static byte[] BuildQuery()
    {
        var ms = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };            // en-tête : 1 question
        foreach (var label in Service.Split('.')) { var b = Encoding.ASCII.GetBytes(label); ms.Add((byte)b.Length); ms.AddRange(b); }
        ms.Add(0); ms.AddRange(new byte[] { 0, 12, 0, 1 });                           // type PTR, classe IN
        return ms.ToArray();
    }

    private static string ReadName(byte[] d, ref int pos)
    {
        var sb = new StringBuilder(); var p = pos; var jumped = false; var guard = 0;
        while (p < d.Length && guard++ < 64)
        {
            int len = d[p];
            if (len == 0) { p++; break; }
            if ((len & 0xC0) == 0xC0) { var ptr = ((len & 0x3F) << 8) | d[p + 1]; if (!jumped) pos = p + 2; jumped = true; p = ptr; continue; }
            p++; sb.Append(Encoding.UTF8.GetString(d, p, len)).Append('.'); p += len;
        }
        if (!jumped) pos = p;
        return sb.ToString().TrimEnd('.');
    }

    private static List<FoundTarget> Parse(List<(byte[] Data, IPAddress From)> packets)
    {
        var result = new Dictionary<string, FoundTarget>();
        foreach (var (d, from) in packets)
        {
            try
            {
                if (d.Length < 12) continue;
                int qd = d[4] << 8 | d[5], an = (d[6] << 8 | d[7]) + (d[8] << 8 | d[9]) + (d[10] << 8 | d[11]);
                var pos = 12;
                for (var i = 0; i < qd; i++) { ReadName(d, ref pos); pos += 4; }
                var ptr = new List<string>(); var srv = new Dictionary<string, (string Target, int Port)>();
                var txt = new Dictionary<string, Dictionary<string, string>>(); var a = new Dictionary<string, IPAddress>();
                for (var i = 0; i < an && pos < d.Length; i++)
                {
                    var name = ReadName(d, ref pos);
                    int type = d[pos] << 8 | d[pos + 1], rdlen = d[pos + 8] << 8 | d[pos + 9];
                    var rd = pos + 10; pos = rd + rdlen;
                    switch (type)
                    {
                        case 12 when name.Equals(Service, StringComparison.OrdinalIgnoreCase): { var p = rd; ptr.Add(ReadName(d, ref p)); break; }
                        case 33: { var p = rd + 6; srv[name] = (ReadName(d, ref p), d[rd + 4] << 8 | d[rd + 5]); break; }
                        case 16:
                        {
                            var map = new Dictionary<string, string>(); var p = rd;
                            while (p < rd + rdlen) { int l = d[p++]; var s = Encoding.UTF8.GetString(d, p, l); p += l; var eq = s.IndexOf('='); if (eq > 0) map[s[..eq]] = s[(eq + 1)..]; }
                            txt[name] = map; break;
                        }
                        case 1 when rdlen == 4: a[name] = new IPAddress(new ReadOnlySpan<byte>(d, rd, 4)); break;
                    }
                }
                foreach (var inst in ptr)
                {
                    if (!srv.TryGetValue(inst, out var s) || !txt.TryGetValue(inst, out var t) || !t.TryGetValue("id", out var id)) continue;
                    var host = a.TryGetValue(s.Target, out var ip) ? ip : from;
                    result[id] = new FoundTarget(id, t.GetValueOrDefault("name") ?? inst.Split('.')[0], host.ToString(), s.Port, t.GetValueOrDefault("platform") ?? "android");
                }
            }
            catch { /* paquet étranger ou mal formé */ }
        }
        return result.Values.ToList();
    }
}
