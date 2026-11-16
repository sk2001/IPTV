using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IptvPlayer.Models;
using IptvPlayer.Services;

namespace IptvPlayer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public const string AllId = "__all__";
    public const string FavId = "__fav__";

    private readonly AccountStore _store = new();
    private readonly AppState _state = StateStore.Load();
    private Catalog? _catalog;
    private CancellationTokenSource? _loadCts, _filterCts, _epgCts;

    public ObservableCollection<Account> Accounts { get; }
    public AppState State => _state;

    [ObservableProperty] private Account? _selectedAccount;
    [ObservableProperty] private ContentKind _currentKind = ContentKind.Live;
    [ObservableProperty] private IReadOnlyList<Category> _categories = Array.Empty<Category>();
    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<MediaItem> _items = Array.Empty<MediaItem>();
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _emptyText = "";
    [ObservableProperty] private string _nowSubtitle = "";

    public bool HasAccounts => Accounts.Count > 0;

    public bool IsLive { get => CurrentKind == ContentKind.Live; set { if (value) CurrentKind = ContentKind.Live; } }
    public bool IsMovies { get => CurrentKind == ContentKind.Movie; set { if (value) CurrentKind = ContentKind.Movie; } }
    public bool IsSeries { get => CurrentKind == ContentKind.Series; set { if (value) CurrentKind = ContentKind.Series; } }

    /// <summary>Demande de lecture : (url, titre).</summary>
    public event Action<string, string>? PlayRequested;

    /// <summary>Délégué fourni par la vue pour choisir un épisode.</summary>
    public Func<MediaItem, List<Episode>, Episode?>? EpisodePicker { get; set; }

    public MainViewModel()
    {
        Accounts = new ObservableCollection<Account>(_store.Load());
    }

    public void Initialize()
    {
        var last = Accounts.FirstOrDefault(a => a.Id == _state.LastAccountId) ?? Accounts.FirstOrDefault();
        if (last != null) SelectedAccount = last;
        else UpdateEmpty();
    }

    partial void OnCurrentKindChanged(ContentKind value)
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsMovies));
        OnPropertyChanged(nameof(IsSeries));
        SearchText = "";
        RebuildCategories(null);
    }

    partial void OnSelectedAccountChanged(Account? value)
    {
        _state.LastAccountId = value?.Id;
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
        Categories = Array.Empty<Category>();
        Items = Array.Empty<MediaItem>();
        if (acc == null) { Status = ""; IsLoading = false; UpdateEmpty(); return; }

        IsLoading = true;
        Status = "Chargement…";
        UpdateEmpty();
        try
        {
            Catalog? cache = force ? null : await CatalogService.LoadCacheAsync(acc);
            if (cts.IsCancellationRequested) return;

            if (cache != null)
            {
                await SetCatalogAsync(acc, cache);
                Status = $"Cache du {cache.FetchedAt.ToLocalTime():g}";
            }

            var stale = force || cache == null || DateTime.UtcNow - cache.FetchedAt > TimeSpan.FromHours(12);
            if (stale)
            {
                Status = "Mise à jour…";
                var fresh = await CatalogService.FetchAsync(acc, new Progress<string>(s => Status = s), cts.Token);
                if (cts.IsCancellationRequested) return;
                await SetCatalogAsync(acc, fresh);
                Status = $"{fresh.Items.Count:N0} éléments · mis à jour";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) Status = "Erreur : " + ex.Message;
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

        var all = new Category { Id = AllId, Name = "Tout", Kind = kind, Count = total };
        var list = new List<Category>
        {
            new() { Id = FavId, Name = "★  Favoris", Kind = kind, Count = favs },
            all,
        };
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

        try
        {
            if (!immediate) await Task.Delay(180, cts.Token);
            var result = await Task.Run(() =>
            {
                IEnumerable<MediaItem> q = catalog.Items.Where(i => i.Kind == kind);
                if (catId == FavId) q = q.Where(i => i.IsFavorite);
                else if (catId != AllId) q = q.Where(i => i.CategoryId == catId);
                if (query.Length > 0) q = q.Where(i => i.SearchKey.Contains(query));
                return q.ToList();
            }, cts.Token);

            if (cts.IsCancellationRequested) return;
            Items = result;
            UpdateEmpty();
        }
        catch (OperationCanceledException) { }
    }

    private void UpdateEmpty()
    {
        if (!HasAccounts) EmptyText = "Ajoutez un compte pour commencer.";
        else if (IsLoading && Items.Count == 0) EmptyText = "";
        else if (_catalog == null) EmptyText = "";
        else if (Items.Count == 0)
            EmptyText = SelectedCategory?.Id == FavId ? "Aucun favori pour l'instant."
                      : string.IsNullOrWhiteSpace(SearchText) ? "Aucun contenu dans cette section." : "Aucun résultat.";
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
            Status = "Chargement des épisodes…";
            try
            {
                var eps = await new XtreamClient(acc).GetEpisodesAsync(item.Id);
                if (eps.Count == 0) { Status = "Aucun épisode trouvé."; return; }
                var ep = EpisodePicker?.Invoke(item, eps);
                if (ep == null) { Status = ""; return; }
                NowSubtitle = "";
                Status = "";
                PlayRequested?.Invoke(ep.Url, $"{item.Name} — {ep.Display}");
            }
            catch (Exception ex) { Status = "Erreur : " + ex.Message; }
            return;
        }

        if (string.IsNullOrEmpty(item.StreamUrl)) return;
        NowSubtitle = "";
        PlayRequested?.Invoke(item.StreamUrl, item.Name);
        if (item.Kind == ContentKind.Live && acc.Type == AccountType.Xtream)
            _ = LoadEpgAsync(acc, item);
    }

    private async Task LoadEpgAsync(Account acc, MediaItem item)
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
