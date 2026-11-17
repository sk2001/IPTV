using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IptvPlayer.Services.Remote;

/// <summary>Appareil Android appairé : empreinte épinglée du certificat + jeton (chiffré DPAPI dans le fichier).</summary>
public sealed class KnownTarget
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 47231;
    public string Platform { get; set; } = "android";
    public string Fingerprint { get; set; } = "";
    public string? TokenProtected { get; set; }
    [JsonIgnore] public string Token { get; set; } = "";
    public override string ToString() => Name;
}

/// <summary>Cibles appairées et identifiant stable de ce PC, dans %AppData%\IptvPlayer\remote-targets.json.</summary>
public sealed class TargetStore
{
    private sealed class FileModel { public string DeviceId { get; set; } = ""; public List<KnownTarget> Targets { get; set; } = new(); }

    private static readonly string FilePath = Path.Combine(AccountStore.DataDir, "remote-targets.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private FileModel _m = new();

    public string DeviceId => _m.DeviceId;
    public string DeviceName => Environment.MachineName;
    public IReadOnlyList<KnownTarget> Targets => _m.Targets;

    public TargetStore()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                _m = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(FilePath)) ?? new();
                foreach (var t in _m.Targets) t.Token = Unprotect(t.TokenProtected);
            }
        }
        catch { _m = new(); }
        if (_m.DeviceId.Length == 0) { _m.DeviceId = Guid.NewGuid().ToString(); Save(); }
    }

    public void AddOrReplace(KnownTarget t)
    {
        _m.Targets.RemoveAll(x => x.Id == t.Id);
        _m.Targets.Add(t);
        Save();
    }

    public void Remove(string id) { _m.Targets.RemoveAll(x => x.Id == id); Save(); }

    private void Save()
    {
        Directory.CreateDirectory(AccountStore.DataDir);
        foreach (var t in _m.Targets)
            t.TokenProtected = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(t.Token), null, DataProtectionScope.CurrentUser));
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_m, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }

    private static string Unprotect(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}
