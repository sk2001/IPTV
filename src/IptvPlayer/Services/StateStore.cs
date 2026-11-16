using System.IO;
using System.Text.Json;

namespace IptvPlayer.Services;

public sealed class AppState
{
    public Guid? LastAccountId { get; set; }
    public HashSet<string> Favorites { get; set; } = new();
    public int Volume { get; set; } = 80;
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
