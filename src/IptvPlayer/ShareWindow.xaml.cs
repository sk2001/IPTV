using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using IptvPlayer.Localization;
using IptvPlayer.Services.Remote;
using IptvPlayer.ViewModels;
using QRCoder;

namespace IptvPlayer;

/// <summary>
/// Partager la configuration (comptes, réglages, favoris, progression) avec un nouvel appareil Android :
/// QR code + adresse + code à usage unique. Le serveur n'existe que tant que cette fenêtre est ouverte.
/// </summary>
public partial class ShareWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ShareServer _server;
    private readonly SyncEngine _engine;

    public ShareWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        var store = RemoteHub.Store;
        _engine = PcSync.CreateEngine(vm, store);
        // Les adaptateurs touchent des collections liées à l'interface : tout passe par le thread principal.
        _server = new ShareServer(store.DeviceId, store.DeviceName,
            col => Dispatcher.Invoke(() => SyncEngine.ToJson(_engine.Snapshot(col).Values)),
            (col, json) => Dispatcher.Invoke(() => { var n = _engine.Merge(col, SyncEngine.FromJson(json)); _vm.AfterSync(); return n; }));
        _server.Changed += () => Dispatcher.BeginInvoke(UpdateStatus);

        Loaded += (_, _) => Start();
        Closed += (_, _) => _server.Dispose();                      // le PC cesse d'ecouter
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.DarkTitleBar(this);
    }

    private void Start()
    {
        var ip = ShareServer.LocalIPv4();
        if (ip == null) { StatusText.Text = Loc.T("share.noNetwork"); return; }
        try { _server.Start(); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }

        var data = new QRCodeGenerator().CreateQrCode(_server.Link(ip), QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);
        var bmp = new BitmapImage();
        bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = new MemoryStream(png); bmp.EndInit();
        QrImage.Source = bmp;
        AddressText.Text = $"{ip}:{_server.Port}";
        PinText.Text = Loc.T("share.code", _server.Pin);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText.Text = !_server.Paired ? Loc.T("share.waiting")
            : _server.Collections == 0 ? Loc.T("share.connected", _server.PairedName ?? "")
            : Loc.T("share.done", _server.PairedName ?? "");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
