using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using IptvPlayer.Services;

namespace IptvPlayer;

/// <summary>
/// Propriété attachée <c>ImageLoader.Url</c> pour <see cref="Image"/> : télécharge en arrière-plan (8 requêtes max),
/// réduit à 96 px, met en cache (mémoire + disque en miniatures) et annule le chargement quand la ligne est recyclée.
/// </summary>
public static class ImageLoader
{
    public static readonly DependencyProperty UrlProperty = DependencyProperty.RegisterAttached(
        "Url", typeof(string), typeof(ImageLoader), new PropertyMetadata(null, OnUrlChanged));

    public static string? GetUrl(DependencyObject d) => (string?)d.GetValue(UrlProperty);
    public static void SetUrl(DependencyObject d, string? v) => d.SetValue(UrlProperty, v);

    private static readonly DependencyProperty CtsProperty =
        DependencyProperty.RegisterAttached("Cts", typeof(CancellationTokenSource), typeof(ImageLoader));

    private static readonly SemaphoreSlim Gate = new(8);
    private static readonly ConcurrentDictionary<string, BitmapSource> Memory = new();
    private static readonly ConcurrentDictionary<string, byte> Failed = new();
    private static readonly string DiskDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IptvPlayer", "img");

    private static async void OnUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image img) return;

        // Annule le chargement précédent de cette image (ligne recyclée par la virtualisation).
        (d.GetValue(CtsProperty) as CancellationTokenSource)?.Cancel();
        img.Source = null;

        var url = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute)) return;

        if (Memory.TryGetValue(url, out var hit)) { img.Source = hit; return; }
        if (Failed.ContainsKey(url)) return;

        var cts = new CancellationTokenSource();
        d.SetValue(CtsProperty, cts);
        try
        {
            var bmp = await LoadAsync(url, cts.Token);
            if (bmp != null && !cts.IsCancellationRequested) img.Source = bmp;
        }
        catch (OperationCanceledException) { }
    }

    private static async Task<BitmapSource?> LoadAsync(string url, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)));
            var file = Path.Combine(DiskDir, key + ".png");

            var bmp = await Task.Run(async () =>
            {
                try
                {
                    if (File.Exists(file)) return Decode(await File.ReadAllBytesAsync(file, ct), 0);

                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(12));
                    var bytes = await Http.Client.GetByteArrayAsync(url, timeout.Token).ConfigureAwait(false);
                    var small = Decode(bytes, 96);
                    SaveThumb(file, small);
                    return small;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { return null; }
            }, ct);

            if (bmp == null) { Failed.TryAdd(url, 0); return null; }
            if (Memory.Count > 4000) Memory.Clear();
            Memory[url] = bmp;
            return bmp;
        }
        finally { Gate.Release(); }
    }

    private static BitmapImage Decode(byte[] bytes, int decodeWidth)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(bytes);
        if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static void SaveThumb(string file, BitmapSource bmp)
    {
        try
        {
            Directory.CreateDirectory(DiskDir);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            File.WriteAllBytes(file, ms.ToArray());
        }
        catch { /* cache best-effort */ }
    }
}
