using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IptvPlayer.Models;
using IptvPlayer.Services;
using IptvPlayer.ViewModels;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace IptvPlayer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private bool _hasMedia;
    private bool _seeking;
    private bool _volumeInit = true;

    private bool _fullscreen;
    private WindowState _prevState;
    private WindowStyle _prevStyle;
    private ResizeMode _prevResize;
    private GridLength _prevNav, _prevCat, _prevList;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _vm.PlayRequested += OnPlayRequested;
        _vm.EpisodePicker = (item, eps) =>
        {
            var w = new EpisodesWindow(item, eps) { Owner = this };
            return w.ShowDialog() == true ? w.Selected : null;
        };
        _vm.PropertyChanged += OnVmPropertyChanged;

        _hideTimer.Tick += (_, _) => HideControls();
        Overlay.MouseMove += (_, _) => ShowControls();
        Overlay.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) ToggleFullscreen(); };
        Overlay.PreviewKeyDown += (_, e) => HandleKey(e);
        PreviewKeyDown += (_, e) => HandleKey(e);

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.DarkTitleBar(this);
    }

    // ------------------------------------------------------------------ VLC

    private (string url, string title)? _pending;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        VolumeSlider.Value = _vm.State.Volume;
        _volumeInit = false;

        _vm.Initialize();
        _ = InitVlcAsync();   // le scan des plugins VLC (lent au 1er lancement) ne bloque plus l'UI
        if (!_vm.HasAccounts) OpenAccounts();
    }

    private async Task InitVlcAsync()
    {
        try
        {
            var lib = await Task.Run(() =>
            {
                // Extrait VLC au 1er lancement (mode exe unique), sinon utilise le dossier libvlc voisin.
                var dir = VlcBootstrap.Prepare();
                if (dir != null) Core.Initialize(dir); else Core.Initialize();
                return new LibVLC(
                    "--network-caching=1200",
                    "--live-caching=1000",
                    "--http-reconnect",
                    "--no-video-title-show",
                    "--no-snapshot-preview",
                    "--quiet");
            });

            var player = new MediaPlayer(lib)
            {
                EnableMouseInput = false,
                EnableKeyInput = false,
                Volume = _vm.State.Volume,
            };

            player.Playing += (_, _) => Ui(() => { PlayBtn.Content = ""; BufferText.Visibility = Visibility.Collapsed; });
            player.Paused += (_, _) => Ui(() => { PlayBtn.Content = ""; ShowControls(); });
            player.EndReached += (_, _) => Ui(ShowIdle);
            player.EncounteredError += (_, _) => Ui(() => { BufferText.Text = "Erreur de lecture — flux indisponible"; BufferText.Visibility = Visibility.Visible; });
            player.Buffering += (_, a) => Ui(() =>
            {
                if (a.Cache < 100) { BufferText.Text = $"Mise en mémoire tampon… {a.Cache:0}%"; BufferText.Visibility = Visibility.Visible; }
                else BufferText.Visibility = Visibility.Collapsed;
            });
            player.TimeChanged += (_, _) => Ui(UpdateSeek);
            player.LengthChanged += (_, _) => Ui(UpdateSeek);

            _libVlc = lib;
            _player = player;
            Video.MediaPlayer = player;

            if (_pending is { } p)
            {
                _pending = null;
                OnPlayRequested(p.url, p.title);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Impossible d'initialiser VLC :" + Environment.NewLine + ex.Message, "IPTV Player",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _vm.State.Volume = (int)VolumeSlider.Value;
        StateStore.Save(_vm.State);
        var p = _player; var l = _libVlc;
        Video.MediaPlayer = null;
        Task.Run(() => { try { p?.Stop(); p?.Dispose(); l?.Dispose(); } catch { } }).Wait(1500);
    }

    private void OnPlayRequested(string url, string title)
    {
        if (_player == null || _libVlc == null)
        {
            _pending = (url, title);
            _hasMedia = true;
            Idle.Visibility = Visibility.Collapsed;
            TitleText.Text = title;
            BufferText.Text = "Initialisation du lecteur…";
            BufferText.Visibility = Visibility.Visible;
            return;
        }
        try
        {
            using var media = new Media(_libVlc, new Uri(url));
            _player.Play(media);
        }
        catch (Exception ex)
        {
            BufferText.Text = "Erreur : " + ex.Message;
            BufferText.Visibility = Visibility.Visible;
            return;
        }
        _hasMedia = true;
        Idle.Visibility = Visibility.Collapsed;
        TitleText.Text = title;
        SubtitleText.Text = "";
        BufferText.Text = "Connexion…";
        BufferText.Visibility = Visibility.Visible;
        SeekRow.Visibility = Visibility.Collapsed;
        ShowControls();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.NowSubtitle))
            SubtitleText.Text = _vm.NowSubtitle;
        else if (e.PropertyName == nameof(MainViewModel.IsGuide))
            // La superposition du lecteur est une fenêtre séparée : on la masque pour dégager la grille.
            Overlay.Visibility = _vm.IsGuide ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowIdle()
    {
        _hasMedia = false;
        Idle.Visibility = Visibility.Visible;
        Controls.Visibility = Visibility.Collapsed;
        TopBar.Visibility = Visibility.Collapsed;
        BufferText.Visibility = Visibility.Collapsed;
        SeekRow.Visibility = Visibility.Collapsed;
    }

    private static void Ui(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);

    // ------------------------------------------------------------------ Contrôles du lecteur

    private void ShowControls()
    {
        if (!_hasMedia) return;
        Controls.Visibility = Visibility.Visible;
        TopBar.Visibility = Visibility.Visible;
        Overlay.Cursor = null;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideControls()
    {
        _hideTimer.Stop();
        if (Controls.IsMouseOver || _seeking) { _hideTimer.Start(); return; }
        if (_player is { IsPlaying: false } && _hasMedia && !_fullscreen) return; // reste visible en pause
        Controls.Visibility = Visibility.Collapsed;
        TopBar.Visibility = Visibility.Collapsed;
        if (_fullscreen) Overlay.Cursor = Cursors.None;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePause();

    private void TogglePause()
    {
        if (_player == null || !_hasMedia) return;
        if (_player.IsPlaying) _player.Pause(); else _player.Play();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _player?.Stop();
        ShowIdle();
    }

    private void Mute_Click(object sender, RoutedEventArgs e) => ToggleMute();

    private void ToggleMute()
    {
        if (_player == null) return;
        _player.Mute = !_player.Mute;
        MuteBtn.Content = _player.Mute ? "" : "";
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_volumeInit || _player == null) return;
        _player.Volume = (int)e.NewValue;
    }

    private void Audio_Click(object sender, RoutedEventArgs e) => CycleTrack(audio: true);
    private void Subs_Click(object sender, RoutedEventArgs e) => CycleTrack(audio: false);

    private void CycleTrack(bool audio)
    {
        if (_player == null || !_hasMedia) return;
        var tracks = audio ? _player.AudioTrackDescription : _player.SpuDescription;
        if (tracks == null || tracks.Length == 0) { Toast(audio ? "Aucune piste audio" : "Aucun sous-titre"); return; }
        var current = audio ? _player.AudioTrack : _player.Spu;
        var idx = Array.FindIndex(tracks, t => t.Id == current);
        var next = tracks[(idx + 1) % tracks.Length];
        if (audio) _player.SetAudioTrack(next.Id); else _player.SetSpu(next.Id);
        Toast((audio ? "Audio : " : "Sous-titres : ") + next.Name);
    }

    private async void Toast(string text)
    {
        SubtitleText.Text = text;
        await Task.Delay(2500);
        SubtitleText.Text = _vm.NowSubtitle;
    }

    // ------------------------------------------------------------------ Barre de progression (VOD)

    private void UpdateSeek()
    {
        if (_player == null) return;
        var len = _player.Length;
        var show = _hasMedia && len > 0 && _player.IsSeekable;
        SeekRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        DurationText.Text = Fmt(len);
        TimeText.Text = Fmt(_player.Time);
        if (!_seeking) SeekSlider.Value = (double)_player.Time / len * 1000;
    }

    private void Seek_Down(object sender, MouseButtonEventArgs e) => _seeking = true;

    private void Seek_Up(object sender, MouseButtonEventArgs e)
    {
        _seeking = false;
        if (_player is { IsSeekable: true })
            _player.Position = (float)(SeekSlider.Value / 1000.0);
    }

    private static string Fmt(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    // ------------------------------------------------------------------ Plein écran & clavier

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _prevState = WindowState; _prevStyle = WindowStyle; _prevResize = ResizeMode;
            _prevNav = NavCol.Width; _prevCat = CatCol.Width; _prevList = ListCol.Width;
            NavCol.Width = CatCol.Width = ListCol.Width = new GridLength(0);
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _fullscreen = true;
            FsBtn.Content = "";
        }
        else
        {
            WindowStyle = _prevStyle;
            ResizeMode = _prevResize;
            WindowState = _prevState;
            NavCol.Width = _prevNav; CatCol.Width = _prevCat; ListCol.Width = _prevList;
            _fullscreen = false;
            Overlay.Cursor = null;
            FsBtn.Content = "";
        }
        ShowControls();
    }

    private void HandleKey(KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || Keyboard.FocusedElement is TextBox) return;

        if (_vm.IsGuide && e.Key is Key.Left or Key.Right)
        {
            _vm.GuideShiftCommand.Execute(e.Key == Key.Left ? "-30" : "30");
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Space: TogglePause(); break;
            case Key.F:
            case Key.F11: ToggleFullscreen(); break;
            case Key.Escape: if (_fullscreen) ToggleFullscreen(); else return; break;
            case Key.M: ToggleMute(); break;
            case Key.Up: VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5); break;
            case Key.Down: VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5); break;
            case Key.Left: if (_player is { IsSeekable: true }) _player.Time -= 10_000; else return; break;
            case Key.Right: if (_player is { IsSeekable: true }) _player.Time += 10_000; else return; break;
            default: return;
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ Listes & comptes

    private void Item_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsInside<ToggleButton>(e.OriginalSource as DependencyObject)) return;
        if (sender is ListBoxItem { DataContext: MediaItem item })
        {
            e.Handled = true;
            _ = _vm.PlayAsync(item);
        }
    }

    // ---- Guide TV ----

    private void GuideRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: GuideRow row })
        {
            e.Handled = true;
            _ = _vm.PlayFromGuideAsync(row);
        }
    }

    /// <summary>Maj + molette : décale l'horaire (la molette seule fait défiler les chaînes).</summary>
    private void GuideList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return;
        e.Handled = true;
        _vm.GuideShiftCommand.Execute(e.Delta > 0 ? "-30" : "30");
    }

    private void ItemList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ItemList.SelectedItem is MediaItem item)
        {
            e.Handled = true;
            _ = _vm.PlayAsync(item);
        }
    }

    private void Star_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MediaItem item })
            _vm.FavoriteChanged(item);
    }

    private void AccountButton_Click(object sender, RoutedEventArgs e) => AccountPopup.IsOpen = !AccountPopup.IsOpen;
    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e) => AccountPopup.IsOpen = false;

    private void Accounts_Click(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = false;
        OpenAccounts();
    }

    private void OpenAccounts()
    {
        var w = new AccountsWindow(_vm.Accounts) { Owner = this };
        w.ShowDialog();
        _vm.AccountsEdited(w.Modified);
    }

    private static bool IsInside<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }
}
