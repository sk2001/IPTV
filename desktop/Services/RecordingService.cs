using System.IO;
using IptvPlayer.Localization;
using IptvPlayer.Models;

namespace IptvPlayer.Services;

public static class RecordingService
{
    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "IPTV Recordings");

    /// <summary>Nouveau chemin de fichier .ts unique pour « nom - date heure ».</summary>
    public static string NewPath(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var baseName = $"{Sanitize(name)} - {DateTime.Now:yyyy-MM-dd HHmm}";
        var path = Path.Combine(dir, baseName + ".ts");
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{baseName} ({i}).ts");
        return path;
    }

    /// <summary>Garde lettres, chiffres, espace, tiret et underscore (la syntaxe de sortie VLC est sensible aux autres).</summary>
    private static string Sanitize(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_').ToArray();
        var s = new string(chars).Trim();
        if (s.Length > 60) s = s[..60].TrimEnd();
        return s.Length == 0 ? "Enregistrement" : s;
    }

    public static List<RecordingFile> List(string dir, string? activePath)
    {
        var list = new List<RecordingFile>();
        try
        {
            if (!Directory.Exists(dir)) return list;
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*.ts"))
            {
                list.Add(new RecordingFile
                {
                    Path = f.FullName,
                    Name = System.IO.Path.GetFileNameWithoutExtension(f.Name),
                    Date = f.LastWriteTime,
                    Size = f.Length,
                    IsActive = activePath != null && string.Equals(f.FullName, activePath, StringComparison.OrdinalIgnoreCase),
                });
            }
        }
        catch { }
        return list.OrderByDescending(r => r.Date).ToList();
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} {Loc.T("unit.gb")}",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} {Loc.T("unit.mb")}",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} {Loc.T("unit.kb")}",
        _ => $"{bytes} {Loc.T("unit.b")}",
    };
}
