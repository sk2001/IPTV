using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IptvPlayer.Models;

namespace IptvPlayer.Services;

/// <summary>Persistance des comptes dans %AppData%\IptvPlayer\accounts.json (mot de passe chiffré DPAPI).</summary>
public sealed class AccountStore
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IptvPlayer");

    private static readonly string FilePath = Path.Combine(DataDir, "accounts.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public List<Account> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var list = JsonSerializer.Deserialize<List<Account>>(File.ReadAllText(FilePath)) ?? new();
            foreach (var a in list)
                a.Password = Unprotect(a.PasswordProtected);
            return list;
        }
        catch { return new(); }
    }

    public void Save(IEnumerable<Account> accounts)
    {
        Directory.CreateDirectory(DataDir);
        var list = accounts.ToList();
        foreach (var a in list) a.PasswordProtected = Protect(a.Password);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }

    private static string? Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    private static string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return "";
        try
        {
            var data = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch { return ""; }
    }
}
