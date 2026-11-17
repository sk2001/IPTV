using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using IptvPlayer.ViewModels;

namespace IptvPlayer.Services.Remote;

/// <summary>
/// Garde le PC synchronisé en permanence avec les appareils Android liés : toutes les 30 secondes et peu après un changement local.
/// Le PC initie toujours (il n'écoute rien) ; une empreinte rapide évite tout échange quand rien n'a changé.
/// </summary>
public sealed class AutoSync
{
    private readonly MainViewModel _vm;
    private readonly TargetStore _store;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Dictionary<string, string> _signatures = new();
    private bool _running;

    /// <summary>Dernier résultat par appareil : date et erreur éventuelle.</summary>
    public Dictionary<string, (DateTime At, string? Error)> Status { get; } = new();
    public event Action? StatusChanged;

    public AutoSync(MainViewModel vm, TargetStore store)
    {
        _vm = vm; _store = store;
        _timer.Tick += async (_, _) => await RunAsync();
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await RunAsync(); };
    }

    public void Start()
    {
        _timer.Start();
        _ = Task.Delay(8000).ContinueWith(_ => Application.Current?.Dispatcher.BeginInvoke(async () => await RunAsync()));
    }

    /// <summary>Un changement local vient d'avoir lieu : synchroniser sous peu (les demandes rapprochées sont regroupées).</summary>
    public void Request() { if (!_debounce.IsEnabled) _debounce.Start(); }

    public async Task RunAsync()
    {
        if (_running || !_vm.State.AutoSync) return;
        _running = true;
        try { foreach (var t in _store.Targets.ToList()) await OneAsync(t); }
        finally { _running = false; }
    }

    private void Set(KnownTarget t, string? error) { Status[t.Id] = (DateTime.Now, error); StatusChanged?.Invoke(); }

    private async Task OneAsync(KnownTarget t)
    {
        try
        {
            using var client = new RemoteClient(t.Host, t.Port, t.Fingerprint, t.Token);
            var (code, manifest) = await client.CallRawAsync(HttpMethod.Get, "/sync/manifest");
            if (code == 401) { Set(t, "unauthorized"); return; }
            if (code != 200) { Set(t, $"HTTP {code}"); return; }

            var withAccounts = _vm.State.SyncAccounts || t.Accounts;
            var sig = manifest + "|" + PcSync.CreateEngine(_vm, _store).Signature(withAccounts);
            if (_signatures.TryGetValue(t.Id, out var old) && old == sig) { Set(t, null); return; }      // rien de nouveau

            await PcSync.RunAsync(_vm, t, _store);
            var (_, after) = await client.CallRawAsync(HttpMethod.Get, "/sync/manifest");
            _signatures[t.Id] = after + "|" + PcSync.CreateEngine(_vm, _store).Signature(withAccounts);
            Set(t, null);
        }
        catch (Exception ex) { Set(t, ex.Message); }       // appareil éteint ou hors réseau : on réessaiera au prochain tour
    }
}
