using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace IptvPlayer;

/// <summary>
/// Quand l'exe est publié en mode « tout-en-un », le moteur VLC est embarqué sous forme de zip.
/// Il est extrait une seule fois dans %LocalAppData%\IptvPlayer\libvlc-&lt;version&gt;.
/// </summary>
internal static class VlcBootstrap
{
    /// <summary>Retourne le dossier à passer à <c>Core.Initialize</c>, ou null si VLC n'est pas embarqué (libvlc à côté de l'exe).</summary>
    public static string? Prepare()
    {
        // Remplacement de libVLC par l'utilisateur (exigence de la LGPL) : un dossier « libvlc » à côté de
        // l'exe (contenant libvlc.dll, libvlccore.dll et plugins\) prend le pas sur le moteur embarqué.
        foreach (var candidate in new[] { Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64"), Path.Combine(AppContext.BaseDirectory, "libvlc") })
            if (File.Exists(Path.Combine(candidate, "libvlc.dll"))) return candidate;

        var asm = typeof(VlcBootstrap).Assembly;
        using var res = asm.GetManifestResourceStream("libvlc.zip");
        if (res == null) return null;

        var stamp = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
                       .FirstOrDefault(a => a.Key == "VlcStamp")?.Value ?? "0";
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IptvPlayer");
        var target = Path.Combine(root, "libvlc-" + stamp);
        var okFile = Path.Combine(target, ".ok");

        if (File.Exists(okFile))
        {
            CleanOldVersionsInBackground(root, target);   // à chaque lancement, au cas où une ancienne version traîne
            return target;
        }

        Directory.CreateDirectory(root);
        if (Directory.Exists(target)) TryDelete(target);   // extraction précédente interrompue

        var tmp = target + ".tmp-" + Environment.ProcessId;
        try
        {
            using (var zip = new ZipArchive(res, ZipArchiveMode.Read))
                zip.ExtractToDirectory(tmp, overwriteFiles: true);
            File.WriteAllText(Path.Combine(tmp, ".ok"), stamp);

            try { Directory.Move(tmp, target); }
            catch (IOException) when (File.Exists(okFile)) { TryDelete(tmp); }   // une autre instance a fini avant nous
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }

        CleanOldVersionsInBackground(root, target);
        return target;
    }

    /// <summary>
    /// Supprime en arrière-plan (sans retarder le démarrage) les dossiers <c>libvlc-*</c> des anciennes versions :
    /// jamais la version courante, jamais un dossier qu'une autre instance utilise encore, et les extractions
    /// interrompues (<c>.tmp-</c>) seulement quand elles ont plus d'une heure (une extraction en cours dure quelques secondes).
    /// </summary>
    private static void CleanOldVersionsInBackground(string root, string current) => Task.Run(() =>
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "libvlc-*").ToList())
            {
                if (string.Equals(dir, current, StringComparison.OrdinalIgnoreCase)) continue;
                if (dir.Contains(".tmp-"))
                {
                    if (Directory.GetLastWriteTime(dir) < DateTime.Now.AddHours(-1)) TryDelete(dir);
                    continue;
                }
                if (!InUse(dir)) TryDelete(dir);
            }
        }
        catch { /* nettoyage au mieux : ne doit jamais gêner l'application */ }
    });

    /// <summary>Une DLL chargée par un processus ne peut pas être ouverte en écriture exclusive : sert à détecter l'usage.</summary>
    private static bool InUse(string dir)
    {
        var dll = Path.Combine(dir, "libvlc.dll");
        if (!File.Exists(dll)) return false;
        try
        {
            using var fs = File.Open(dll, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch { return true; }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}
