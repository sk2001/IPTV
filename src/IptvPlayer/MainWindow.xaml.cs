using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IptvPlayer.Localization;
using IptvPlayer.Models;
using IptvPlayer.Services;
using IptvPlayer.ViewModels;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
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

    // Enregistrement
    private string? _currentUrl, _currentTitle;
    private string? _recPath;
    private DateTime _recStart;
    private bool _recWarned;
    private long _resumeAtMs;
    private bool _resumeToast;

    // Historique de visionnage + langue des pistes
    private WatchContext? _watch;
    private long _lastPosMs, _lastLenMs;
    private DateTime _lastReport = DateTime.MinValue;
    private ContentKind _playKind = ContentKind.Live;
    private bool _trackPrefPending, _audioDone, _subDone;

    private readonly DispatcherTimer _recTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private bool _fullscreen;
    private WindowState _prevState;
    private WindowStyle _prevStyle;
    private ResizeMode _prevResize;
    private GridLength _prevNav, _prevCat, _prevList;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm; RemoteHub.Vm = _vm;

        Overlay.Background = Brushes.Black;   // état initial : rien en lecture
        _vm.PlayRequested += OnPlayRequested;
        _vm.EpisodePicker = (item, eps, suggested, toggleWatched) =>
        {
            var w = new EpisodesWindow(item, eps, suggested, toggleWatched) { Owner = this };
            return w.ShowDialog() == true ? w.Selected : null;
        };
        _vm.PropertyChanged += OnVmPropertyChanged;

        _hideTimer.Tick += (_, _) => HideControls();
        _recTimer.Tick += (_, _) => UpdateRecBadge();
        Overlay.MouseMove += (_, _) => ShowControls();
        Overlay.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) ToggleFullscreen(); };
        Overlay.PreviewKeyDown += (_, e) => HandleKey(e);
        PreviewKeyDown += (_, e) => HandleKey(e);

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_recPath == null) return;
        var r = MessageBox.Show(this, Loc.T("quit.rec"),
            Loc.T("quit.rec.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) e.Cancel = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.DarkTitleBar(this);
    }

    // ------------------------------------------------------------------ VLC

    private (string url, string title, WatchContext? ctx)? _pending;

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

            player.Playing += (_, _) => Ui(ResumeIfNeeded);
            player.Playing += (_, _) => Ui(() => { PlayBtn.Content = ""; BufferText.Visibility = Visibility.Collapsed; });
            player.Paused += (_, _) => Ui(() => { PlayBtn.Content = ""; ShowControls(); });
            player.EndReached += (_, _) => Ui(() =>
            {
                if (_watch != null && _lastLenMs > 0) _lastPosMs = _lastLenMs;   // fin atteinte → marqué comme vu
                ShowIdle();
            });
            player.EncounteredError += (_, _) => Ui(() => { Overlay.Background = Brushes.Black; BufferText.Text = Loc.T("player.error"); BufferText.Visibility = Visibility.Visible; });
            player.Buffering += (_, a) => Ui(() =>
            {
                if (a.Cache < 100) { BufferText.Text = Loc.T("player.buffering", a.Cache); BufferText.Visibility = Visibility.Visible; }
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
                OnPlayRequested(p.url, p.title, p.ctx);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("player.vlcFail", ex.Message), "IPTV Player",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        FlushWatch();
        _vm.FlushHistory();
        _vm.State.Volume = (int)VolumeSlider.Value;
        StateStore.Save(_vm.State);
        var p = _player; var l = _libVlc;
        Video.MediaPlayer = null;
        Task.Run(() => { try { p?.Stop(); p?.Dispose(); l?.Dispose(); } catch { } }).Wait(1500);
    }

    private void OnPlayRequested(string url, string title, WatchContext? ctx)
    {
        if (_player == null || _libVlc == null)
        {
            _pending = (url, title, ctx);
            _hasMedia = true;
            Idle.Visibility = Visibility.Collapsed;
            TitleText.Text = title;
            BufferText.Text = Loc.T("player.initializing");
            BufferText.Visibility = Visibility.Visible;
            return;
        }
        // Changer de flux ferme le fichier d'enregistrement en cours.
        if (_recPath != null) EndRecordingState();
        FlushWatch();                                  // enregistre la progression du média précédent
        _watch = ctx;
        _playKind = ctx?.Kind ?? ContentKind.Live;
        _resumeAtMs = ctx?.ResumeMs ?? 0;
        _resumeToast = _resumeAtMs > 0;
        _lastPosMs = _lastLenMs = 0;
        _lastReport = DateTime.Now;
        _trackPrefPending = true; _audioDone = _subDone = false;
        try
        {
            StartMedia(url, null);
        }
        catch (Exception ex)
        {
            BufferText.Text = Loc.T("player.errorPrefix", ex.Message);
            BufferText.Visibility = Visibility.Visible;
            return;
        }
        _currentUrl = url;
        _currentTitle = title;
        _hasMedia = true;
        Overlay.Background = OverlayPlaying;
        Idle.Visibility = Visibility.Collapsed;
        TitleText.Text = title;
        SubtitleText.Text = "";
        BufferText.Text = Loc.T("player.connecting");
        BufferText.Visibility = Visibility.Visible;
        SeekRow.Visibility = Visibility.Collapsed;
        ShowControls();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.NowSubtitle))
            SubtitleText.Text = _vm.NowSubtitle;
        else if (e.PropertyName == nameof(MainViewModel.IsPlayerHidden))
            // La superposition du lecteur est une fenêtre séparée : on la masque pour dégager le guide / la liste.
            Overlay.Visibility = _vm.IsPlayerHidden ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------------ Enregistrement

    /// <summary>Lance un média ; avec <paramref name="recordTo"/>, VLC affiche ET écrit le flux dans un fichier .ts (une seule connexion).</summary>
    private void StartMedia(string url, string? recordTo)
    {
        using var media = new Media(_libVlc!, new Uri(url));

        // Langue préférée : VLC choisit d'après les métadonnées du flux (complété par ApplyTrackPrefs).
        var audio = Languages.VlcCodes(_playKind == ContentKind.Live ? _vm.State.AudioLangLive : _vm.State.AudioLangVod);
        if (audio != null) media.AddOption(":audio-language=" + audio);
        if (_playKind != ContentKind.Live && Languages.VlcCodes(_vm.State.SubLangVod) is { } subs)
            media.AddOption(":sub-language=" + subs);

        if (recordTo != null)
            media.AddOption(":sout=#duplicate{dst=display,dst=std{access=file,mux=ts,dst=\"" + recordTo.Replace('\\', '/') + "\"}}");
        _player!.Play(media);
    }

    private void Record_Click(object sender, RoutedEventArgs e) => ToggleRecord();

    private void ToggleRecord()
    {
        if (_player == null || _libVlc == null || !_hasMedia || _currentUrl == null) return;
        if (System.IO.File.Exists(_currentUrl)) { Toast(Loc.T("rec.localNothing")); return; }

        // VLC doit relancer le flux pour (dés)activer la copie vers le fichier : on reprend ensuite à la même position.
        _resumeAtMs = _player.IsSeekable ? _player.Time : 0;
        try
        {
            if (_recPath == null)
            {
                var path = RecordingService.NewPath(_vm.RecordingsDir, _currentTitle ?? "Enregistrement");
                StartMedia(_currentUrl, path);
                BeginRecordingState(path);
            }
            else
            {
                var done = System.IO.Path.GetFileName(_recPath);
                EndRecordingState();
                StartMedia(_currentUrl, null);
                Toast(Loc.T("rec.done", done));
            }
        }
        catch (Exception ex)
        {
            EndRecordingState();
            Toast(Loc.T("rec.failed", ex.Message));
        }
    }

    private void BeginRecordingState(string path)
    {
        _recPath = path;
        _recStart = DateTime.Now;
        _recWarned = false;
        RecDot.Fill = (Brush)new BrushConverter().ConvertFromString("#F85149")!;
        RecBadge.Visibility = Visibility.Visible;
        _vm.ActiveRecordingPath = path;
        UpdateRecBadge();
        _recTimer.Start();
        Toast(Loc.T("rec.started"));
    }

    private void EndRecordingState()
    {
        _recTimer.Stop();
        _recPath = null;
        RecDot.Fill = Brushes.Transparent;
        RecBadge.Visibility = Visibility.Collapsed;
        _vm.ActiveRecordingPath = null;
    }

    private void UpdateRecBadge()
    {
        if (_recPath == null) return;
        var elapsed = DateTime.Now - _recStart;
        long size = 0;
        try { size = new System.IO.FileInfo(_recPath).Length; } catch { }
        RecText.Text = Loc.T("rec.badge", $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}", RecordingService.FormatSize(size));
        RecBlink.Opacity = elapsed.Seconds % 2 == 0 ? 1 : 0.25;

        if (!_recWarned && size == 0 && elapsed.TotalSeconds > 8)
        {
            _recWarned = true;
            Toast(Loc.T("rec.noData"));
        }
    }

    /// <summary>Après un redémarrage du flux (début/fin d'enregistrement), reprend la lecture à la position d'avant.</summary>
    private void ResumeIfNeeded()
    {
        // Réessayé à chaque mise à jour du temps tant que le flux n'est pas « seekable ».
        if (_resumeAtMs <= 0 || _player == null || !_player.IsSeekable || _player.Length <= 0) return;
        var t = _resumeAtMs;
        _resumeAtMs = 0;
        _player.Time = t;
        if (_resumeToast) { _resumeToast = false; Toast(Loc.T("resume.at", Fmt(t))); }
    }

    // ------------------------------------------------------------------ Historique & langue des pistes

    /// <summary>Enregistre la dernière position connue du média en cours (fin de lecture, changement de média, fermeture).</summary>
    private void FlushWatch()
    {
        var w = _watch;
        _watch = null;
        if (w == null || _lastLenMs <= 0 || _lastPosMs < 10_000) return;
        _vm.ReportProgress(w, _lastPosMs, _lastLenMs, flush: true);
        _lastPosMs = _lastLenMs = 0;
    }

    private void TrackProgress()
    {
        if (_watch is not { TracksHistory: true } || _player == null || _resumeAtMs > 0) return;
        var len = _player.Length;
        if (!_player.IsSeekable || len <= 0) return;

        _lastPosMs = _player.Time;
        _lastLenMs = len;
        if (_lastPosMs >= 10_000 && DateTime.Now - _lastReport > TimeSpan.FromSeconds(10))
        {
            _lastReport = DateTime.Now;
            _vm.ReportProgress(_watch, _lastPosMs, _lastLenMs, flush: false);
        }
    }

    /// <summary>
    /// Complète l'option VLC : certains fournisseurs ne renseignent pas la langue des pistes, mais leur nom
    /// (« Piste 2 - [English] ») la contient. On essaie pendant les premières secondes (les sous-titres arrivent parfois plus tard).
    /// </summary>
    private void ApplyTrackPrefs()
    {
        if (!_trackPrefPending || _player == null || !_player.IsPlaying) return;
        var t = _player.Time;
        if (t < 1200) return;
        if (t > 20_000) { _trackPrefPending = false; return; }

        var live = _playKind == ContentKind.Live;
        var audioLang = live ? _vm.State.AudioLangLive : _vm.State.AudioLangVod;
        var subLang = live ? "" : _vm.State.SubLangVod;

        if (!_audioDone)
        {
            var tracks = (_player.AudioTrackDescription ?? Array.Empty<TrackDescription>()).Where(x => x.Id >= 0).ToArray();
            if (string.IsNullOrEmpty(audioLang) || tracks.Length < 2) _audioDone = tracks.Length >= 2 || string.IsNullOrEmpty(audioLang);
            else
            {
                _audioDone = true;
                var current = tracks.FirstOrDefault(x => x.Id == _player.AudioTrack);
                if (current.Name == null || !Languages.NameMatches(audioLang, current.Name))
                {
                    var match = tracks.FirstOrDefault(x => Languages.NameMatches(audioLang, x.Name));
                    if (match.Name != null) { _player.SetAudioTrack(match.Id); Toast(Loc.T("track.audio", match.Name)); }
                }
            }
        }

        if (!_subDone)
        {
            if (string.IsNullOrEmpty(subLang)) _subDone = true;
            else
            {
                var tracks = (_player.SpuDescription ?? Array.Empty<TrackDescription>()).Where(x => x.Id >= 0).ToArray();
                var match = tracks.FirstOrDefault(x => Languages.NameMatches(subLang, x.Name));
                if (match.Name != null) { _subDone = true; _player.SetSpu(match.Id); }
            }
        }

        if (_audioDone && _subDone) _trackPrefPending = false;
    }

    // ---- Onglet Enregistrements ----

    private void OpenRecDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_vm.RecordingsDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_vm.RecordingsDir) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("rec.dir.err"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ChooseRecDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("rec.dir.dialog"), InitialDirectory = _vm.RecordingsDir };
        if (dlg.ShowDialog(this) == true) _vm.SetRecordingsDir(dlg.FolderName);
    }

    private void RecItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsInside<Button>(e.OriginalSource as DependencyObject)) return;
        if (sender is ListBoxItem { DataContext: RecordingFile f }) { e.Handled = true; _vm.PlayRecording(f); }
    }

    private void RecPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RecordingFile f }) _vm.PlayRecording(f);
    }

    private void RecDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RecordingFile f }) return;
        if (MessageBox.Show(this, Loc.T("rec.delete.confirm", f.Name, f.SizeText), Loc.T("rec.delete.title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var err = _vm.DeleteRecording(f);
        if (err != null) MessageBox.Show(this, err, Loc.T("rec.delete.err"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // Superposition quasi transparente (clics captés) pendant la lecture ; noire au repos pour masquer
    // le fond gris par défaut de la zone vidéo native.
    private static readonly Brush OverlayPlaying = Freeze(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));
    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    private void ShowIdle()
    {
        if (_recPath != null) EndRecordingState();
        FlushWatch();
        _hasMedia = false;
        Overlay.Background = Brushes.Black;
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
        if (tracks == null || tracks.Length == 0) { Toast(Loc.T(audio ? "track.noAudio" : "track.noSubs")); return; }
        var current = audio ? _player.AudioTrack : _player.Spu;
        var idx = Array.FindIndex(tracks, t => t.Id == current);
        var next = tracks[(idx + 1) % tracks.Length];
        if (audio) _player.SetAudioTrack(next.Id); else _player.SetSpu(next.Id);
        Toast(Loc.T(audio ? "track.audio" : "track.subs", next.Name));
    }

    private async void Toast(string text)
    {
        SubtitleText.Text = text;
        ShowControls();
        await Task.Delay(2500);
        SubtitleText.Text = _vm.NowSubtitle;
    }

    // ------------------------------------------------------------------ Barre de progression (VOD)

    private void UpdateSeek()
    {
        if (_player == null) return;
        ResumeIfNeeded();
        TrackProgress();
        ApplyTrackPrefs();
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
            case Key.R: ToggleRecord(); break;
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
        // Suppr dans la catégorie Historique : retire le film / la série de l'historique.
        if (e.Key == Key.Delete && _vm.SelectedCategory?.Id == MainViewModel.HistId && ItemList.SelectedItem is MediaItem h)
        {
            e.Handled = true;
            _vm.RemoveFromHistory(h);
            return;
        }
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

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow(_vm) { Owner = this };
        w.ShowDialog();
        if (w.OpenAccountsRequested) OpenAccounts();
    }

    /// <summary>« Envoyer vers » : menu des appareils appairés ; la lecture continue sur l'appareil choisi.</summary>
    private void SendTo_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        if (_currentUrl != null && !System.IO.File.Exists(_currentUrl))
        {
            foreach (var t in RemoteHub.Store.Targets.ToList())
            {
                var item = new MenuItem { Header = t.Name };
                item.Click += async (_, _) =>
                {
                    var kind = _playKind switch { ContentKind.Movie => "movie", ContentKind.Series => "series", _ => "live" };
                    var pos = _player is { IsSeekable: true } ? _player.Time : 0;
                    var err = await RemoteWindow.SendPlayAsync(t, _currentUrl!, _currentTitle ?? _currentUrl!, kind, pos);
                    if (err == null) Stop_Click(this, new RoutedEventArgs()); else Toast(err);
                };
                menu.Items.Add(item);
            }
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        var open = new MenuItem { Header = Loc.T("rem.open") };
        open.Click += (_, _) => new RemoteWindow { Owner = this }.ShowDialog();
        menu.Items.Add(open);
        menu.IsOpen = true;
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
