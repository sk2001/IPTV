using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IptvPlayer.Localization;
using IptvPlayer.Models;
using IptvPlayer.Services;

namespace IptvPlayer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public const string AllId = "__all__";
    public const string FavId = "__fav__";
    public const string HistId = "__hist__";

    private readonly AccountStore _store = new();
    private readonly AppState _state = StateStore.Load();

    /// <summary>Historique de visionnage (films et séries).</summary>
    public HistoryService History { get; } = new();
    private Catalog? _catalog;
    private CancellationTokenSource? _loadCts, _filterCts, _epgCts;

    public ObservableCollection<Account> Accounts { get; }
    public AppState State => _state;

    public enum NavMode { Live, Guide, Movies, Series, Recordings }

    private EpgData? _epg;
    private MediaItem? _playing;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(60) };

    [ObservableProperty] private Account? _selectedAccount;
    [ObservableProperty] private NavMode _mode = NavMode.Live;

    // Guide TV
    [ObservableProperty] private IReadOnlyList<GuideRow> _guideRows = Array.Empty<GuideRow>();
    [ObservableProperty] private DateTime _guideStart = Floor30(DateTime.Now.AddMinutes(-30));
    [ObservableProperty] private DateTime _clockNow = DateTime.Now;
    [ObservableProperty] private bool _isEpgLoading;
    [ObservableProperty] private string _guideEmptyText = "";

    public ContentKind CurrentKind => KindOf(Mode);
    public bool IsGuide => Mode == NavMode.Guide;
    public bool IsNotGuide => Mode != NavMode.Guide;
    public bool IsRecordings => Mode == NavMode.Recordings;
    /// <summary>Guide et Enregistrements remplacent le lecteur : la vidéo (et sa superposition) est masquée.</summary>
    public bool IsPlayerHidden => Mode is NavMode.Guide or NavMode.Recordings;

    // Enregistrements
    [ObservableProperty] private IReadOnlyList<RecordingFile> _recordings = Array.Empty<RecordingFile>();
    [ObservableProperty] private string? _activeRecordingPath;
    public string RecordingsDir => string.IsNullOrWhiteSpace(_state.RecordingsDir) ? RecordingService.DefaultDir : _state.RecordingsDir!;
    public string RecordingsEmptyText => Recordings.Count == 0
        ? Loc.T("rec.empty")
        : "";
    public string GuideDateText => GuideStart.ToString("dddd d MMMM", System.Globalization.CultureInfo.CurrentCulture);

    private static ContentKind KindOf(NavMode m) => m switch
    {
        NavMode.Movies => ContentKind.Movie,
        NavMode.Series => ContentKind.Series,
        _ => ContentKind.Live,
    };

    private static DateTime Floor30(DateTime d) =>
        new(d.Year, d.Month, d.Day, d.Hour, d.Minute >= 30 ? 30 : 0, 0);
    [ObservableProperty] private IReadOnlyList<Category> _categories = Array.Empty<Category>();
    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<MediaItem> _items = Array.Empty<MediaItem>();
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _emptyText = "";
    [ObservableProperty] private string _nowSubtitle = "";

    public bool HasAccounts => Accounts.Count > 0;

    /// <summary>Nom du compte actif, ou « Aucun compte » (traduit).</summary>
    public string SelectedAccountName => SelectedAccount?.Name ?? Loc.T("main.account.none");

    public bool IsLive { get => Mode == NavMode.Live; set { if (value) Mode = NavMode.Live; } }
    public bool IsGuideNav { get => Mode == NavMode.Guide; set { if (value) Mode = NavMode.Guide; } }
    public bool IsMovies { get => Mode == NavMode.Movies; set { if (value) Mode = NavMode.Movies; } }
    public bool IsSeries { get => Mode == NavMode.Series; set { if (value) Mode = NavMode.Series; } }
    public bool IsRecordingsNav { get => Mode == NavMode.Recordings; set { if (value) Mode = NavMode.Recordings; } }

    /// <summary>Demande de lecture : (url, titre, contexte de suivi — null pour live/enregistrements).</summary>
    public event Action<string, string, WatchContext?>? PlayRequested;

    /// <summary>Délégué fourni par la vue pour choisir un épisode (épisodes, suggestion, bascule « vu / non vu »).</summary>
    public Func<MediaItem, List<Episode>, Episode?, Action<Episode, bool>, Episode?>? EpisodePicker { get; set; }

    public MainViewModel()
    {
        Accounts = new ObservableCollection<Account>(_store.Load());
        Loc.LanguageChanged += OnLanguageChanged;
        _clock.Tick += (_, _) => OnClockTick();
        _clock.Start();
    }

    /// <summary>Rafraîchit les textes calculés par le ViewModel quand la langue de l'interface change.</summary>
    private Func<string>? _statusFactory;
    private string? _statusFactoryValue;

    /// <summary>Affiche un statut traduit ; il est recalculé si la langue change (tant qu'il n'a pas été remplacé).</summary>
    private void SetStatus(Func<string> factory)
    {
        _statusFactory = factory;
        Status = _statusFactoryValue = factory();
    }

    private void OnLanguageChanged()
    {
        if (_statusFactory != null && Status == _statusFactoryValue) Status = _statusFactoryValue = _statusFactory();
        OnPropertyChanged(nameof(SelectedAccountName));
        OnPropertyChanged(nameof(GuideDateText));
        OnPropertyChanged(nameof(RecordingsEmptyText));
        if (_catalog != null) RebuildCategories(SelectedCategory?.Id);
        UpdateEmpty();
        UpdateGuideEmpty();
        foreach (var i in Items) i.RefreshEpg();
        _ = RefreshRecordingsAsync();
    }

    private void OnClockTick()
    {
        ClockNow = DateTime.Now;
        foreach (var i in Items) i.RefreshEpg();
        UpdateNowSubtitle();
    }

    public void Initialize()
    {
        var last = Accounts.FirstOrDefault(a => a.Id == _state.LastAccountId) ?? Accounts.FirstOrDefault();
        if (last != null) SelectedAccount = last;
        else UpdateEmpty();
    }

    partial void OnModeChanged(NavMode oldValue, NavMode newValue)
    {
        OnPropertyChanged(nameof(CurrentKind));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsGuideNav));
        OnPropertyChanged(nameof(IsMovies));
        OnPropertyChanged(nameof(IsSeries));
        OnPropertyChanged(nameof(IsGuide));
        OnPropertyChanged(nameof(IsNotGuide));
        OnPropertyChanged(nameof(IsRecordings));
        OnPropertyChanged(nameof(IsRecordingsNav));
        OnPropertyChanged(nameof(IsPlayerHidden));
        if (newValue == NavMode.Recordings) _ = RefreshRecordingsAsync();

        if (KindOf(oldValue) != KindOf(newValue))
        {
            SearchText = "";
            RebuildCategories(null);
        }
        if (newValue == NavMode.Guide) RebuildGuideRows();
    }

    partial void OnGuideStartChanged(DateTime value) => OnPropertyChanged(nameof(GuideDateText));

    partial void OnSelectedAccountChanged(Account? value)
    {
        OnPropertyChanged(nameof(SelectedAccountName));
        _state.LastAccountId = value?.Id;
        HistoryService.CurrentAccount = value?.Id ?? Guid.Empty;
        StateStore.Save(_state);
        _ = LoadAccountAsync(value, force: false);
    }

    partial void OnSelectedCategoryChanged(Category? value) => ApplyFilter(immediate: true);
    partial void OnSearchTextChanged(string value) => ApplyFilter(immediate: false);

    // ---------------------------------------------------------------- Chargement

    private async Task LoadAccountAsync(Account? acc, bool force)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();

        _catalog = null;
        SetEpg(null);
        Categories = Array.Empty<Category>();
        Items = Array.Empty<MediaItem>();
        if (acc == null) { Status = ""; IsLoading = false; UpdateEmpty(); return; }

        IsLoading = true;
        SetStatus(() => Loc.T("vm.loading"));
        UpdateEmpty();
        try
        {
            Catalog? cache = force ? null : await CatalogService.LoadCacheAsync(acc);
            if (cts.IsCancellationRequested) return;

            if (cache != null)
            {
                await SetCatalogAsync(acc, cache);
                SetStatus(() => Loc.T("vm.cacheFrom", cache.FetchedAt.ToLocalTime()));
            }

            var stale = force || cache == null || DateTime.UtcNow - cache.FetchedAt > TimeSpan.FromHours(12);
            if (stale)
            {
                SetStatus(() => Loc.T("vm.updating"));
                var fresh = await CatalogService.FetchAsync(acc, new Progress<string>(s => Status = s), cts.Token);
                if (cts.IsCancellationRequested) return;
                await SetCatalogAsync(acc, fresh);
                SetStatus(() => Loc.T("vm.updated", fresh.Items.Count));
            }

            if (_catalog != null && !cts.IsCancellationRequested)
                await LoadEpgAsync(acc, _catalog, force, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) SetStatus(() => Loc.T("vm.error", ex.Message));
        }
        finally
        {
            if (_loadCts == cts) { IsLoading = false; UpdateEmpty(); }
        }
    }

    private async Task SetCatalogAsync(Account acc, Catalog catalog)
    {
        var prefix = acc.Id.ToString("N");
        var favs = _state.Favorites;
        await Task.Run(() =>
        {
            foreach (var i in catalog.Items)
            {
                i.SearchKey = i.Name.ToLowerInvariant();
                i.NameKey = EpgData.Norm(i.Name);
                i.IsFavorite = favs.Contains($"{prefix}|{(int)i.Kind}|{i.Id}");
            }
        });
        var keep = SelectedCategory?.Id;
        _catalog = catalog;
        RebuildCategories(keep);
    }

    private void RebuildCategories(string? keepId)
    {
        if (_catalog == null) { Categories = Array.Empty<Category>(); Items = Array.Empty<MediaItem>(); UpdateEmpty(); return; }

        var kind = CurrentKind;
        int total = 0, favs = 0;
        foreach (var i in _catalog.Items)
        {
            if (i.Kind != kind) continue;
            total++;
            if (i.IsFavorite) favs++;
        }

        var all = new Category { Id = AllId, Name = Loc.T("cat.all"), Kind = kind, Count = total };
        var list = new List<Category>
        {
            new() { Id = FavId, Name = Loc.T("cat.favorites"), Kind = kind, Count = favs },
        };
        if (kind != ContentKind.Live)
            list.Add(new Category { Id = HistId, Name = Loc.T("cat.history"), Kind = kind, Count = History.Recent(SelectedAccount?.Id ?? Guid.Empty, kind).Count });
        list.Add(all);
        list.AddRange(_catalog.Categories.Where(c => c.Kind == kind));
        Categories = list;
        SelectedCategory = (keepId != null ? list.FirstOrDefault(c => c.Id == keepId) : null) ?? all;
    }

    private async void ApplyFilter(bool immediate)
    {
        _filterCts?.Cancel();
        var cts = _filterCts = new CancellationTokenSource();
        var catalog = _catalog;
        if (catalog == null) return;

        var kind = CurrentKind;
        var catId = SelectedCategory?.Id ?? AllId;
        var query = SearchText.Trim().ToLowerInvariant();
        var accountId = SelectedAccount?.Id ?? Guid.Empty;

        try
        {
            if (!immediate) await Task.Delay(180, cts.Token);
            var result = await Task.Run(() =>
            {
                IEnumerable<MediaItem> q = catalog.Items.Where(i => i.Kind == kind);
                if (catId == HistId)
                {
                    // Du plus récent au plus ancien, dans l'ordre de l'historique.
                    var byId = new Dictionary<string, MediaItem>();
                    foreach (var i in q) byId.TryAdd(i.Id, i);
                    q = History.Recent(accountId, kind).Select(w => byId.GetValueOrDefault(w.ItemId)).Where(i => i != null)!;
                }
                else if (catId == FavId) q = q.Where(i => i.IsFavorite);
                else if (catId != AllId) q = q.Where(i => i.CategoryId == catId);
                if (query.Length > 0) q = q.Where(i => i.SearchKey.Contains(query));
                return q.ToList();
            }, cts.Token);

            if (cts.IsCancellationRequested) return;
            Items = result;
            UpdateEmpty();
            if (IsGuide) RebuildGuideRows();
        }
        catch (OperationCanceledException) { }
    }

    // ---------------------------------------------------------------- Enregistrements

    partial void OnActiveRecordingPathChanged(string? value) => _ = RefreshRecordingsAsync();

    public async Task RefreshRecordingsAsync()
    {
        var dir = RecordingsDir;
        var active = ActiveRecordingPath;
        Recordings = await Task.Run(() => RecordingService.List(dir, active));
        OnPropertyChanged(nameof(RecordingsEmptyText));
    }

    public void SetRecordingsDir(string dir)
    {
        _state.RecordingsDir = dir;
        StateStore.Save(_state);
        OnPropertyChanged(nameof(RecordingsDir));
        _ = RefreshRecordingsAsync();
    }

    public void PlayRecording(RecordingFile file)
    {
        if (!File.Exists(file.Path)) { _ = RefreshRecordingsAsync(); return; }
        Mode = NavMode.Live;
        _playing = null;
        NowSubtitle = "";
        PlayRequested?.Invoke(file.Path, file.Name, null);
    }

    /// <summary>Supprime un enregistrement (refusé s'il est en cours). Retourne un message d'erreur ou null.</summary>
    public string? DeleteRecording(RecordingFile file)
    {
        if (file.IsActive) return Loc.T("rec.stopFirst");
        try { File.Delete(file.Path); }
        catch (Exception ex) { return ex.Message; }
        _ = RefreshRecordingsAsync();
        return null;
    }

    // ---------------------------------------------------------------- Guide TV (EPG)

    private async Task LoadEpgAsync(Account acc, Catalog catalog, bool force, CancellationToken ct)
    {
        try
        {
            if (EpgService.SourceUrl(acc, catalog) == null) { GuideEmptyText = Loc.T("guide.noSource"); return; }

            IsEpgLoading = true;
            GuideEmptyText = Loc.T("guide.loading");
            EpgData? cache = force ? null : await EpgService.LoadCacheAsync(acc);
            if (ct.IsCancellationRequested) return;
            if (cache != null) SetEpg(cache);

            var stale = force || cache == null || DateTime.UtcNow - cache.FetchedAt > TimeSpan.FromHours(6);
            if (stale)
            {
                var old = Status;
                SetStatus(() => Loc.T("guide.downloading"));
                try
                {
                    var fresh = await EpgService.FetchAsync(acc, catalog, ct);
                    if (ct.IsCancellationRequested) return;
                    if (fresh != null) SetEpg(fresh);
                    Status = fresh == null ? old : Loc.T("guide.channelsCount", fresh.ById.Count(kv => kv.Value.Count > 0));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    SetStatus(() => Loc.T("guide.unavailableStatus", ex.Message));
                    if (_epg == null) GuideEmptyText = Loc.T("guide.unavailable");
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { IsEpgLoading = false; UpdateGuideEmpty(); }
    }

    private void SetEpg(EpgData? data)
    {
        _epg = data;
        EpgData.Current = data;
        foreach (var i in Items) i.RefreshEpg();
        UpdateNowSubtitle();
        if (data == null) GuideRows = Array.Empty<GuideRow>();
        else if (IsGuide) RebuildGuideRows();
    }

    private async void RebuildGuideRows()
    {
        var epg = _epg;
        var items = Items;
        if (epg == null) { GuideRows = Array.Empty<GuideRow>(); UpdateGuideEmpty(); return; }

        var rows = await Task.Run(() =>
        {
            var list = new List<GuideRow>();
            foreach (var i in items)
            {
                if (i.Kind != ContentKind.Live) continue;
                var e = epg.Find(i);
                if (e != null) list.Add(new GuideRow(i, e));
            }
            return list;
        });

        if (!ReferenceEquals(items, Items) || !ReferenceEquals(epg, _epg)) return;   // résultat périmé
        GuideRows = rows;
        UpdateGuideEmpty();
    }

    private void UpdateGuideEmpty()
    {
        if (GuideRows.Count > 0) { GuideEmptyText = ""; return; }
        if (IsEpgLoading) { GuideEmptyText = Loc.T("guide.loading"); return; }
        if (_epg == null)
        {
            if (string.IsNullOrEmpty(GuideEmptyText) || GuideEmptyText == Loc.T("guide.loading"))
                GuideEmptyText = Loc.T("guide.none");
            return;
        }
        GuideEmptyText = Loc.T("guide.noPrograms");
    }

    [RelayCommand]
    private void GuideShift(string minutes)
    {
        var next = GuideStart.AddMinutes(int.Parse(minutes));
        var min = Floor30(DateTime.Now.AddHours(-2));
        var max = DateTime.Now.AddHours(32);
        GuideStart = next < min ? min : next > max ? max : next;
    }

    [RelayCommand]
    private void GoToNow() => GuideStart = Floor30(DateTime.Now.AddMinutes(-30));

    /// <summary>Ouvre le lecteur sur la chaîne choisie dans le guide.</summary>
    public async Task PlayFromGuideAsync(GuideRow row)
    {
        Mode = NavMode.Live;
        await PlayAsync(row.Channel);
    }

    private void UpdateNowSubtitle()
    {
        var item = _playing;
        if (item == null || item.Kind != ContentKind.Live) return;
        var list = _epg?.Find(item);
        if (list == null) return;
        var (now, next) = EpgData.NowNext(list, DateTime.Now);
        if (now == null) return;
        NowSubtitle = $"{now.Start:HH:mm}–{now.End:HH:mm}  {now.Title}" + (next != null ? $"     ›  {next.Start:HH:mm}  {next.Title}" : "");
    }

    private void UpdateEmpty()
    {
        if (!HasAccounts) EmptyText = Loc.T("empty.noAccount");
        else if (IsLoading && Items.Count == 0) EmptyText = "";
        else if (_catalog == null) EmptyText = "";
        else if (Items.Count == 0)
            EmptyText = SelectedCategory?.Id == FavId ? Loc.T("empty.noFav")
                      : SelectedCategory?.Id == HistId ? Loc.T("empty.noHistory")
                      : string.IsNullOrWhiteSpace(SearchText) ? Loc.T("empty.noContent") : Loc.T("empty.noResult");
        else EmptyText = "";
    }

    // ---------------------------------------------------------------- Actions

    [RelayCommand]
    private void Refresh()
    {
        if (SelectedAccount != null) _ = LoadAccountAsync(SelectedAccount, force: true);
    }

    public async Task PlayAsync(MediaItem? item)
    {
        var acc = SelectedAccount;
        if (item == null || acc == null) return;

        if (item.Kind == ContentKind.Series)
        {
            SetStatus(() => Loc.T("vm.loadingEpisodes"));
            try
            {
                var eps = await new XtreamClient(acc).GetEpisodesAsync(item.Id);
                if (eps.Count == 0) { SetStatus(() => Loc.T("vm.noEpisodes")); return; }
                var suggested = PrepareEpisodes(acc, item, eps);
                var ep = EpisodePicker?.Invoke(item, eps, suggested, (e, watched) => SetEpisodeWatched(acc, item, e, watched));
                if (ep == null) { Status = ""; return; }
                NowSubtitle = "";
                Status = "";
                _playing = null;
                var ctx = MakeContext(acc, item, ep, _state.ResumePlayback ? ep.ResumeMs : 0);
                PlayRequested?.Invoke(ep.Url, $"{item.Name} — {ep.Display}", ctx);
            }
            catch (Exception ex) { SetStatus(() => Loc.T("vm.error", ex.Message)); }
            return;
        }

        if (string.IsNullOrEmpty(item.StreamUrl)) return;
        NowSubtitle = "";
        _playing = item;
        WatchContext? movieCtx = null;
        if (item.Kind == ContentKind.Movie)
        {
            var w = History.Get(acc.Id, ContentKind.Movie, item.Id);
            movieCtx = MakeContext(acc, item, null, _state.ResumePlayback && w is { InProgress: true } ? w.PositionMs : 0);
        }
        PlayRequested?.Invoke(item.StreamUrl, item.Name, movieCtx);
        if (item.Kind == ContentKind.Live)
        {
            UpdateNowSubtitle();
            // Repli : petit guide par chaîne (Xtream) si le guide complet n'est pas (encore) disponible.
            if (NowSubtitle.Length == 0 && acc.Type == AccountType.Xtream)
                _ = LoadShortEpgAsync(acc, item);
        }
    }

    // ---------------------------------------------------------------- Historique de visionnage

    private static WatchContext MakeContext(Account acc, MediaItem item, Episode? ep, long resumeMs) => new()
    {
        AccountId = acc.Id,
        Kind = item.Kind,
        ItemId = item.Id,
        EpisodeId = ep?.Id,
        Season = ep?.Season ?? 0,
        Number = ep?.Number ?? 0,
        EpisodeTitle = ep?.Title,
        Name = item.Name,
        Logo = item.Logo,
        ResumeMs = resumeMs,
        Item = item,
    };

    /// <summary>Renseigne vu / avancement de chaque épisode et propose celui à regarder (en cours, sinon le suivant).</summary>
    private Episode? PrepareEpisodes(Account acc, MediaItem item, List<Episode> eps)
    {
        var entries = History.EpisodesOf(acc.Id, item.Id).Where(e => e.EpisodeId != null).ToDictionary(e => e.EpisodeId!);
        foreach (var ep in eps)
        {
            if (!entries.TryGetValue(ep.Id, out var w)) continue;
            ep.IsWatched = w.Completed;
            ep.Progress = w.InProgress ? w.Progress : 0;
            ep.ResumeMs = w.InProgress ? w.PositionMs : 0;
        }

        var last = entries.Values.OrderByDescending(e => e.LastWatched).FirstOrDefault();
        if (last == null) return eps[0];
        var idx = eps.FindIndex(e => e.Id == last.EpisodeId);
        if (idx < 0) return eps[0];
        return last.InProgress ? eps[idx] : eps[Math.Min(idx + 1, eps.Count - 1)];
    }

    private void SetEpisodeWatched(Account acc, MediaItem item, Episode ep, bool watched)
    {
        History.SetWatched(MakeContext(acc, item, ep, 0), watched);
        ep.IsWatched = watched;
        ep.Progress = 0;
        ep.ResumeMs = 0;
        History.Save();
        item.RefreshEpg();
        RefreshHistoryCount();
    }

    /// <summary>Appelé par le lecteur : met à jour la position (flush = enregistrer sur disque et rafraîchir la liste).</summary>
    public void ReportProgress(WatchContext ctx, long positionMs, long durationMs, bool flush)
    {
        if (!ctx.TracksHistory || durationMs <= 0) return;
        History.Update(ctx, positionMs, durationMs);
        ctx.Item?.RefreshEpg();
        if (!flush) return;
        History.Save();
        RefreshHistoryCount();
        if (SelectedCategory?.Id == HistId) ApplyFilter(immediate: true);
    }

    public void FlushHistory() => History.Save();

    private void RefreshHistoryCount()
    {
        var h = Categories.FirstOrDefault(c => c.Id == HistId);
        if (h != null) h.Count = History.Recent(SelectedAccount?.Id ?? Guid.Empty, CurrentKind).Count;
    }

    /// <summary>Retire un film / une série de l'historique (touche Suppr dans la catégorie Historique).</summary>
    public void RemoveFromHistory(MediaItem item)
    {
        if (SelectedAccount == null) return;
        History.RemoveItem(SelectedAccount.Id, item.Kind, item.Id);
        History.Save();
        item.RefreshEpg();
        RefreshHistoryCount();
        if (SelectedCategory?.Id == HistId) ApplyFilter(immediate: true);
    }

    public void ClearHistory()
    {
        History.Clear();
        foreach (var i in Items) i.RefreshEpg();
        RefreshHistoryCount();
        if (SelectedCategory?.Id == HistId) ApplyFilter(immediate: true);
    }

    private async Task LoadShortEpgAsync(Account acc, MediaItem item)
    {
        _epgCts?.Cancel();
        var cts = _epgCts = new CancellationTokenSource();
        try
        {
            var epg = await new XtreamClient(acc).GetEpgAsync(item.Id, cts.Token);
            if (cts.IsCancellationRequested) return;
            var now = DateTime.Now;
            var cur = epg.FirstOrDefault(e => e.Start <= now && e.End > now) ?? epg.FirstOrDefault();
            NowSubtitle = cur == null ? "" : cur.Display;
        }
        catch (OperationCanceledException) { }
    }

    public void FavoriteChanged(MediaItem item)
    {
        if (SelectedAccount == null) return;
        var key = $"{SelectedAccount.Id:N}|{(int)item.Kind}|{item.Id}";
        if (item.IsFavorite) _state.Favorites.Add(key); else _state.Favorites.Remove(key);
        StateStore.Save(_state);
        var fav = Categories.FirstOrDefault(c => c.Id == FavId);
        if (fav != null) fav.Count += item.IsFavorite ? 1 : -1;
        if (SelectedCategory?.Id == FavId) ApplyFilter(immediate: true);
    }

    public void AccountsEdited(IReadOnlySet<Guid> modified)
    {
        _store.Save(Accounts);
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(SelectedAccountName));

        if (SelectedAccount == null || !Accounts.Contains(SelectedAccount))
            SelectedAccount = Accounts.FirstOrDefault();
        else if (modified.Contains(SelectedAccount.Id))
        {
            CatalogService.DeleteCache(SelectedAccount);
            _ = LoadAccountAsync(SelectedAccount, force: true);
        }
        UpdateEmpty();
    }
}
