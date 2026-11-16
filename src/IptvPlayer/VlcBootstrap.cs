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
        var asm = typeof(VlcBootstrap).Assembly;
        using var res = asm.GetManifestResourceStream("libvlc.zip");
        if (res == null) return null;

        var stamp = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
                       .FirstOrDefault(a => a.Key == "VlcStamp")?.Value ?? "0";
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IptvPlayer");
        var target = Path.Combine(root, "libvlc-" + stamp);
        var okFile = Path.Combine(target, ".ok");

        if (File.Exists(okFile)) return target;

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

        // Nettoyage des anciennes versions.
        foreach (var dir in Directory.EnumerateDirectories(root, "libvlc-*"))
            if (!string.Equals(dir, target, StringComparison.OrdinalIgnoreCase) && !dir.Contains(".tmp-"))
                TryDelete(dir);

        return target;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}
