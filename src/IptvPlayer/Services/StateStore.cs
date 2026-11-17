using System.IO;
using System.Text.Json;

namespace IptvPlayer.Services;

public sealed class AppState
{
    public Guid? LastAccountId { get; set; }
    public HashSet<string> Favorites { get; set; } = new();
    public int Volume { get; set; } = 80;
    public string? RecordingsDir { get; set; }

    // Préférences de lecture (codes de Languages.All ; vide = automatique)
    public string AudioLangVod { get; set; } = "en";      // films & séries
    public string AudioLangLive { get; set; } = "";       // chaînes live
    public string SubLangVod { get; set; } = "";          // sous-titres films & séries ("" = aucun)
    public bool ResumePlayback { get; set; } = true;      // reprendre là où on s'est arrêté
}

public static class StateStore
{
    private static readonly string FilePath = Path.Combine(AccountStore.DataDir, "state.json");

    public static AppState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public static void Save(AppState state)
    {
        try
        {
            Directory.CreateDirectory(AccountStore.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
        }
        catch { }
    }
}
