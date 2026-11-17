using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IptvPlayer.Localization;
using IptvPlayer.Services;
using IptvPlayer.Services.Remote;

namespace IptvPlayer;

/// <summary>Piloter un appareil Android appairé (téléphone / TV) : appairage par PIN, puis télécommande.</summary>
public partial class RemoteWindow : Window
{
    private static TargetStore Store => RemoteHub.Store;

    private KnownTarget? _selected;
    private RemoteClient? _client;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _polling;
    private bool _muted;

    // Appairage en cours
    private RemoteClient? _pairClient;
    private PairingSession? _session;
    private (string Host, int Port, string Name, string Platform)? _pairTarget;

    public RemoteWindow()
    {
        InitializeComponent();
        _poll.Tick += async (_, _) => await PollAsync();
        Loaded += async (_, _) => { RefreshPaired(); await ScanAsync(); };
        Closed += (_, _) => { _poll.Stop(); _client?.Dispose(); _pairClient?.Dispose(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.DarkTitleBar(this);
    }

    // ------------------------------------------------------------------ Listes

    private void RefreshPaired()
    {
        PairedList.Children.Clear();
        if (Store.Targets.Count == 0)
            PairedList.Children.Add(new TextBlock { Text = Loc.T("rem.nonePaired"), Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush") });
        foreach (var t in Store.Targets.ToList())
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var forget = new Button { Content = Loc.T("rem.forget"), Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush"), Margin = new Thickness(8, 0, 0, 0) };
            forget.Click += async (_, _) => await ForgetAsync(t);
            DockPanel.SetDock(forget, Dock.Right);
            var open = new Button { Content = $"{t.Name}   ({t.Platform} · {t.Host})", HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8, 12, 8) };
            open.Click += (_, _) => Connect(t);
            row.Children.Add(forget); row.Children.Add(open);
            PairedList.Children.Add(row);
        }
    }

    private async Task ScanAsync()
    {
        SearchText.Text = Loc.T("rem.searching");
        NearbyList.Children.Clear();
        List<FoundTarget> found;
        try { found = await MdnsBrowser.BrowseAsync(TimeSpan.FromSeconds(3)); } catch { found = new(); }
        found = found.Where(f => Store.Targets.All(k => k.Id != f.Id)).ToList();
        SearchText.Text = found.Count == 0 ? Loc.T("rem.noneFound") : "";
        foreach (var f in found)
        {
            var b = new Button { Content = $"{f.Name}   ({f.Platform} · {f.Host})", HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6) };
            b.Click += async (_, _) => await StartPairingAsync(f.Host, f.Port, f.Name, f.Platform);
            NearbyList.Children.Add(b);
        }
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    // ------------------------------------------------------------------ Appairage

    private async void ManualPair_Click(object sender, RoutedEventArgs e)
    {
        var text = ManualBox.Text.Trim();
        if (text.Length == 0) return;
        if (text.StartsWith("iptvplayer://pair?", StringComparison.OrdinalIgnoreCase))     // lien lu dans le QR code de l'appareil
        {
            var q = System.Web.HttpUtility.ParseQueryString(text[(text.IndexOf('?') + 1)..]);
            if (q["host"] is { Length: > 0 } h && int.TryParse(q["port"], out var pt) && System.Net.IPAddress.TryParse(h, out var ip) && IsPrivate(ip))
                await StartPairingAsync(h, pt, q["name"] ?? h, "android");
            else Message.Text = Loc.T("rem.badLink");
            return;
        }
        var i = text.LastIndexOf(':');
        var host = i > 0 && !text.EndsWith(']') && text.Count(c => c == ':') == 1 ? text[..i] : text;
        var port = i > 0 && text.Count(c => c == ':') == 1 && int.TryParse(text[(i + 1)..], out var p) ? p : 47231;
        await StartPairingAsync(host, port, host, "android");
    }

    private static bool IsPrivate(System.Net.IPAddress a)
    {
        if (System.Net.IPAddress.IsLoopback(a)) return true;
        var b = a.GetAddressBytes();
        if (b.Length != 4) return a.IsIPv6LinkLocal || a.IsIPv6UniqueLocal;
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    private async Task StartPairingAsync(string host, int port, string name, string platform)
    {
        Message.Text = "";
        try
        {
            _pairClient?.Dispose();
            _pairClient = new RemoteClient(host, port, null, null);
            _session = await _pairClient.PairStartAsync(Store.DeviceId, Store.DeviceName, "windows");
            _pairTarget = (host, port, name, platform);
            PinPrompt.Text = Loc.T("rem.enterPin", name);
            PinBox.Text = ""; PinPanel.Visibility = Visibility.Visible; PinBox.Focus();
        }
        catch (Exception ex) { Message.Text = ex.Message; }
    }

    private async void PinOk_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _pairClient == null || _pairTarget == null) return;
        var pin = PinBox.Text.Trim();
        if (pin.Length != 6) return;
        try
        {
            var (token, id, name) = await _pairClient.PairConfirmAsync(_session, pin);
            var t = _pairTarget.Value;
            Store.AddOrReplace(new KnownTarget { Id = id, Name = name, Host = t.Host, Port = t.Port, Platform = t.Platform, Fingerprint = _session.Fingerprint, Token = token });
            _session = null; PinPanel.Visibility = Visibility.Collapsed; Message.Text = "";
            RefreshPaired(); await ScanAsync();
        }
        catch (WrongPinException) { Message.Text = Loc.T("rem.wrongPin"); }
        catch (Exception ex) { Message.Text = ex.Message; _session = null; PinPanel.Visibility = Visibility.Collapsed; }
    }

    private async Task ForgetAsync(KnownTarget t)
    {
        if (MessageBox.Show(this, Loc.T("rem.forget.confirm", t.Name), Loc.T("rem.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (_selected?.Id == t.Id) Disconnect_Click(this, new RoutedEventArgs());
        try { using var c = new RemoteClient(t.Host, t.Port, t.Fingerprint, t.Token); await c.UnpairAsync(); } catch { /* au mieux */ }
        Store.Remove(t.Id);
        RefreshPaired(); await ScanAsync();
    }

    // ------------------------------------------------------------------ Télécommande

    private void Connect(KnownTarget t)
    {
        _selected = t; _client?.Dispose(); _client = new RemoteClient(t.Host, t.Port, t.Fingerprint, t.Token);
        SyncAccountsBox.IsChecked = RemoteHub.Vm?.State.SyncAccounts ?? false; SyncText.Text = "";
        TargetName.Text = t.Name; TargetStatus.Text = Loc.T("rem.connecting"); Message.Text = "";
        ControlPanel.Visibility = Visibility.Visible; ListsPanel.Visibility = Visibility.Collapsed;
        _ = PollAsync(); _poll.Start();
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        _poll.Stop(); _client?.Dispose(); _client = null; _selected = null;
        ControlPanel.Visibility = Visibility.Collapsed; ListsPanel.Visibility = Visibility.Visible;
    }

    private async Task PollAsync()
    {
        if (_polling || _client == null) return;
        _polling = true;
        try
        {
            var (code, o) = await _client.StateAsync();
            if (code == 401) { TargetStatus.Text = Loc.T("rem.unauthorized"); _poll.Stop(); return; }
            var title = o["media"]?["title"]?.GetValue<string>();
            var state = o["state"]?.GetValue<string>() ?? "";
            _muted = o["muted"]?.GetValue<bool>() ?? false;
            var vol = o["volume"]?.GetValue<int>() ?? 0;
            TargetStatus.Text = title == null ? Loc.T("rem.idle") : $"{title} — {state}   ·   {Loc.T("rem.volume", vol)}";
        }
        catch (Exception ex) { TargetStatus.Text = ex.Message; }
        finally { _polling = false; }
    }

    private async Task SendAsync(string type, params (string Key, JsonNode? Value)[] extra)
    {
        if (_client == null) return;
        var cmd = new JsonObject { ["type"] = type };
        foreach (var (k, v) in extra) cmd[k] = v;
        try { Message.Text = await _client.CommandAsync(cmd) ?? ""; } catch (Exception ex) { Message.Text = ex.Message; }
    }

    private async void Sync_Click(object s, RoutedEventArgs e)
    {
        if (_selected == null || RemoteHub.Vm == null) return;
        SyncText.Text = Loc.T("rem.syncing");
        try
        {
            var (rcv, sent, skipped) = await PcSync.RunAsync(RemoteHub.Vm, _selected, Store);
            SyncText.Text = Loc.T("rem.syncDone", rcv, sent) + (skipped ? " " + Loc.T("rem.syncSkipped") : "");
        }
        catch (Exception ex) { SyncText.Text = ex.Message; }
    }

    private void SyncAccounts_Changed(object s, RoutedEventArgs e)
    {
        if (RemoteHub.Vm == null) return;
        RemoteHub.Vm.State.SyncAccounts = SyncAccountsBox.IsChecked == true;
        StateStore.Save(RemoteHub.Vm.State);
    }

    private async void Prev_Click(object s, RoutedEventArgs e) => await SendAsync("prev");
    private async void Next_Click(object s, RoutedEventArgs e) => await SendAsync("next");
    private async void Toggle_Click(object s, RoutedEventArgs e) => await SendAsync("toggle");
    private async void StopRemote_Click(object s, RoutedEventArgs e) => await SendAsync("stop");
    private async void Back10_Click(object s, RoutedEventArgs e) => await SendAsync("seekBy", ("deltaMs", -10000));
    private async void Fwd10_Click(object s, RoutedEventArgs e) => await SendAsync("seekBy", ("deltaMs", 10000));
    private async void VolDown_Click(object s, RoutedEventArgs e) => await SendAsync("volumeBy", ("delta", -10));
    private async void VolUp_Click(object s, RoutedEventArgs e) => await SendAsync("volumeBy", ("delta", 10));
    private async void Mute_Click(object s, RoutedEventArgs e) => await SendAsync("mute", ("muted", !_muted));
    private async void Audio_Click(object s, RoutedEventArgs e) => await SendAsync("audioTrack", ("id", "next"));
    private async void Subs_Click(object s, RoutedEventArgs e) => await SendAsync("subtitleTrack", ("id", "next"));

    /// <summary>« Envoyer vers » : lit une URL sur la cible (appelé par la fenêtre principale). Retourne un message d'erreur ou null.</summary>
    public static async Task<string?> SendPlayAsync(KnownTarget t, string url, string title, string kind, long startMs)
    {
        try
        {
            using var c = new RemoteClient(t.Host, t.Port, t.Fingerprint, t.Token);
            var source = new JsonObject { ["url"] = url, ["title"] = title, ["kind"] = kind };
            var cmd = new JsonObject { ["type"] = "play", ["source"] = source };
            if (startMs > 10_000) cmd["startMs"] = startMs;
            return await c.CommandAsync(cmd);
        }
        catch (Exception ex) { return ex.Message; }
    }
}

/// <summary>Cibles appairées, partagées par toutes les fenêtres.</summary>
public static class RemoteHub
{
    public static TargetStore Store { get; } = new();

    /// <summary>Modèle de vue principal (donne accès aux données à synchroniser).</summary>
    public static IptvPlayer.ViewModels.MainViewModel? Vm { get; set; }
}
