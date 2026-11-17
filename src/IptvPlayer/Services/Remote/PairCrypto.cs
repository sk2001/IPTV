using System.Security.Cryptography;
using System.Text;

namespace IptvPlayer.Services.Remote;

/// <summary>Cryptographie de l'appairage (protocole v1, §4) — identique à l'implémentation Android (PairCrypto.proof).</summary>
public static class PairCrypto
{
    public static string Proof(string pin, bool server, string pairingId, string clientNonce, string serverNonce, string fingerprint)
    {
        var label = server ? "iptvplayer-pair-v1-server" : "iptvplayer-pair-v1";
        var msg = string.Join("\n", label, pairingId, clientNonce, serverNonce, fingerprint.ToLowerInvariant());
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(pin));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(msg))).ToLowerInvariant();
    }

    public static string RandomHex(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    public static bool Equal(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
