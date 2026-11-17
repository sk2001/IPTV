package app.iptvplayer

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import app.iptvplayer.data.AccountStore
import app.iptvplayer.data.CatalogRepository
import app.iptvplayer.data.UserData
import app.iptvplayer.data.UserDataRepository
import app.iptvplayer.model.Account
import app.iptvplayer.model.AccountType
import app.iptvplayer.model.Catalog
import app.iptvplayer.model.Category
import app.iptvplayer.model.Episode
import app.iptvplayer.model.Kind
import app.iptvplayer.model.MediaItem
import app.iptvplayer.model.PlayRequest
import app.iptvplayer.model.WatchEntry
import app.iptvplayer.net.Http
import app.iptvplayer.net.M3uParser
import app.iptvplayer.net.Programme
import app.iptvplayer.net.Xmltv
import app.iptvplayer.net.XtreamClient
import app.iptvplayer.player.PlayerController
import app.iptvplayer.remote.ControllerManager
import app.iptvplayer.remote.RemoteController
import app.iptvplayer.sync.AndroidSync
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.util.UUID

sealed interface Status {
    data object None : Status
    data object Loading : Status
    data object Updating : Status
    data class Cached(val fetchedAt: Long) : Status
    data class Updated(val count: Int) : Status
    data class Error(val message: String) : Status
}

/** Filtre courant de la liste : type de contenu, catégorie (ou Favoris / Historique / Tout) et recherche. */
data class BrowseFilter(val kind: Kind = Kind.LIVE, val categoryId: String = ALL, val query: String = "")

data class CategoryUi(val id: String, val name: String?, val count: Int)

data class EpisodesUi(val series: MediaItem, val episodes: List<Episode> = emptyList(), val loading: Boolean = true, val error: String? = null)

const val ALL = "__all__"
const val FAV = "__fav__"
const val HIST = "__hist__"

@OptIn(FlowPreview::class)
class AppViewModel(app: Application) : AndroidViewModel(app) {
    private val accountStore = AccountStore(app)
    private val catalogRepo = CatalogRepository(app)
    val userData = UserDataRepository(app, viewModelScope)
    val player = PlayerController(app)
    val controllerOpen = MutableStateFlow(false)
    val sync by lazy { AndroidSync.create(app, this, remote.deviceId) }
    val controller by lazy { ControllerManager(app, this, viewModelScope) }
    val remote by lazy { RemoteController(app, this, viewModelScope) }

    // ------------------------------------------------------------------ Comptes et catalogue

    val accounts = MutableStateFlow(accountStore.load())
    private val selectedAccountId = MutableStateFlow(userData.data.value.lastAccountId)
    val selectedAccount: StateFlow<Account?> = combine(accounts, selectedAccountId) { list, id ->
        list.firstOrNull { it.id == id } ?: list.firstOrNull()
    }.stateIn(viewModelScope, SharingStarted.Eagerly, null)

    val catalog = MutableStateFlow<Catalog?>(null)
    val status = MutableStateFlow<Status>(Status.None)
    private var loadJob: Job? = null

    /** Guide des programmes (chaînes du catalogue, de -2 h à +36 h), chargé en arrière-plan. */
    val epg = MutableStateFlow<Map<String, List<Programme>>>(emptyMap())
    private var epgJob: Job? = null

    init {
        viewModelScope.launch {
            catalog.map { it?.epgUrl }.distinctUntilChanged().collect { url -> loadEpg(url) }
        }
        loadAccount(force = false)
        remote   // démarre le contrôle à distance s'il est activé
        controller.startAutoSync()                                  // appareils liés toujours à jour
        viewModelScope.launch { userData.data.drop(1).collect { controller.requestSync() } }
        viewModelScope.launch { accounts.drop(1).collect { controller.requestSync() } }
        // Enregistre la progression des films/épisodes toutes les 10 s.
        viewModelScope.launch { while (true) { delay(10_000); reportProgress() } }
    }

    fun selectAccount(id: String) {
        selectedAccountId.value = id
        userData.update { it.copy(lastAccountId = id) }
        filter.value = filter.value.copy(categoryId = ALL, query = "")
        loadAccount(force = false)
    }

    fun refresh() = loadAccount(force = true)

    private fun loadAccount(force: Boolean) {
        loadJob?.cancel()
        val acc = selectedAccount.value
        if (acc == null) { catalog.value = null; status.value = Status.None; return }
        loadJob = viewModelScope.launch {
            catalog.value = null
            status.value = Status.Loading
            try {
                val cache = if (force) null else catalogRepo.loadCache(acc)
                if (cache != null) { catalog.value = cache; status.value = Status.Cached(cache.fetchedAt) }
                if (force || cache == null || catalogRepo.isStale(cache)) {
                    status.value = Status.Updating
                    val fresh = catalogRepo.fetch(acc)
                    catalog.value = fresh
                    status.value = Status.Updated(fresh.items.size)
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) { status.value = Status.Error(e.message ?: e.javaClass.simpleName) }
        }
    }

    private fun loadEpg(url: String?) {
        epgJob?.cancel(); epg.value = emptyMap()
        val ids = catalog.value?.items?.filter { it.kind == Kind.LIVE }?.mapNotNull { it.epgId }?.toSet().orEmpty()
        if (url == null || ids.isEmpty()) return
        epgJob = viewModelScope.launch(Dispatchers.IO) {
            // Trois essais : une erreur reseau passagere ne doit pas priver du guide jusqu'au prochain lancement.
            for (attempt in 1..3) {
                try {
                    val now = System.currentTimeMillis()
                    epg.value = Http.open(url, 120_000).use { Xmltv.parse(it, ids, now - 2 * 3_600_000L, now + 36 * 3_600_000L) }
                    android.util.Log.i("EPG", "guide charge : ${epg.value.size} chaines")
                    return@launch
                } catch (e: CancellationException) { throw e }
                catch (e: Exception) { android.util.Log.w("EPG", "guide indisponible (essai $attempt) : ${e.javaClass.simpleName}") }
                delay(4_000L * attempt)
            }
        }
    }

    /** Émission en cours sur une chaîne (null si inconnue). */
    fun nowOn(item: MediaItem, guide: Map<String, List<Programme>>): Programme? {
        val now = System.currentTimeMillis()
        return item.epgId?.let { guide[it] }?.firstOrNull { now in it.start until it.stop }
    }

    /** Teste un compte avant enregistrement. Retourne un message d'erreur ou null si tout va bien. */
    suspend fun testAccount(a: Account): String? = withContext(Dispatchers.IO) {
        if (a.type == AccountType.XTREAM) XtreamClient(a).test()
        else try { if (M3uParser.load(a.url).items.isEmpty()) "Empty or invalid playlist" else null } catch (e: Exception) { e.message ?: "Error" }
    }

    fun saveAccount(a: Account): Account {
        val acc = if (a.id.isEmpty()) a.copy(id = UUID.randomUUID().toString()) else a
        val list = accounts.value
        accounts.value = if (list.any { it.id == acc.id }) list.map { if (it.id == acc.id) acc else it } else list + acc
        accountStore.save(accounts.value)
        catalogRepo.delete(acc)
        selectAccount(acc.id)
        return acc
    }

    /** Comptes reçus d'un autre appareil (synchronisation) : ajout, mise a jour, suppression. */
    fun applySyncedAccounts(upserts: List<Account>, deletes: Set<String>) {
        val before = selectedAccount.value            // a lire avant la modification : selectedAccount se met a jour tout de suite apres
        val ids = upserts.map { it.id }.toSet()
        accounts.value = accounts.value.filter { it.id !in ids && it.id !in deletes } + upserts
        accountStore.save(accounts.value)
        if (before == null || before.id in deletes) {
            selectedAccountId.value = accounts.value.firstOrNull()?.id
            viewModelScope.launch { if (accounts.value.isNotEmpty()) selectedAccount.filterNotNull().first(); loadAccount(force = false) }
        }
    }

    fun deleteAccount(id: String) {
        accounts.value.firstOrNull { it.id == id }?.let { catalogRepo.delete(it) }
        accounts.value = accounts.value.filterNot { it.id == id }
        accountStore.save(accounts.value)
        selectedAccountId.value = accounts.value.firstOrNull()?.id
        loadAccount(force = false)
    }

    // ------------------------------------------------------------------ Liste filtrée

    val filter = MutableStateFlow(BrowseFilter())

    fun setKind(kind: Kind) { filter.value = BrowseFilter(kind, ALL, "") }
    fun setCategory(id: String) { filter.value = filter.value.copy(categoryId = id) }
    fun setQuery(q: String) { filter.value = filter.value.copy(query = q) }

    private fun itemFavKey(accountId: String, i: MediaItem) = userData.favoriteKey(accountId, i.kind, i.id)

    /** Catégories du type courant, avec leur nombre d'éléments (Favoris, Historique, Tout, puis celles du fournisseur). */
    val categories: StateFlow<List<CategoryUi>> = combine(catalog, filter.map { it.kind }, userData.data, selectedAccount) { cat, kind, ud, acc ->
        if (cat == null || acc == null) return@combine emptyList()
        val items = cat.items.filter { it.kind == kind }
        val perCategory = items.groupingBy { it.categoryId }.eachCount()
        val favs = items.count { itemFavKey(acc.id, it) in ud.favorites }
        buildList {
            add(CategoryUi(FAV, null, favs))
            if (kind != Kind.LIVE) add(CategoryUi(HIST, null, ud.history.filter { it.accountId == acc.id && it.kind == kind }.map { it.itemId }.distinct().size))
            add(CategoryUi(ALL, null, items.size))
            cat.categories.filter { it.kind == kind }.forEach { add(CategoryUi(it.id, it.name.ifBlank { "—" }, perCategory[it.id] ?: 0)) }
        }
    }.flowOn(Dispatchers.Default).stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val items: StateFlow<List<MediaItem>> = combine(catalog, filter.debounce(120), userData.data, selectedAccount) { cat, f, ud, acc ->
        if (cat == null || acc == null) return@combine emptyList()
        var list: List<MediaItem> = cat.items.filter { it.kind == f.kind }
        when (f.categoryId) {
            FAV -> list = list.filter { itemFavKey(acc.id, it) in ud.favorites }
            HIST -> {
                val byId = HashMap<String, MediaItem>().also { m -> list.forEach { m.putIfAbsent(it.id, it) } }
                list = ud.history.filter { it.accountId == acc.id && it.kind == f.kind }
                    .distinctBy { it.itemId }.mapNotNull { byId[it.itemId] }        // déjà trié du plus récent au plus ancien
            }
            ALL -> Unit
            else -> list = list.filter { it.categoryId == f.categoryId }
        }
        val q = f.query.trim().lowercase()
        if (q.isNotEmpty()) list = list.filter { it.name.lowercase().contains(q) }
        list
    }.flowOn(Dispatchers.Default).stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    fun isFavorite(i: MediaItem, ud: UserData): Boolean = selectedAccount.value?.let { itemFavKey(it.id, i) in ud.favorites } ?: false
    fun toggleFavorite(i: MediaItem) { selectedAccount.value?.let { userData.toggleFavorite(it.id, i.kind, i.id) } }

    /** Entrée d'historique d'un film, ou du dernier épisode regardé d'une série. */
    fun watchOf(i: MediaItem, ud: UserData): WatchEntry? {
        val acc = selectedAccount.value ?: return null
        return when (i.kind) {
            Kind.MOVIE -> ud.history.firstOrNull { it.accountId == acc.id && it.kind == Kind.MOVIE && it.itemId == i.id }
            Kind.SERIES -> ud.history.firstOrNull { it.accountId == acc.id && it.kind == Kind.SERIES && it.itemId == i.id }
            Kind.LIVE -> null
        }
    }

    fun removeFromHistory(i: MediaItem) { selectedAccount.value?.let { userData.removeItem(it.id, i.kind, i.id) } }

    // ------------------------------------------------------------------ Lecture

    // ------------------------------------------------------------------ TV en direct (guide, mini-lecteur, derniere chaine)

    /** Lecture en plein ecran (true) ou reduite dans le guide (false). */
    val playerFull = MutableStateFlow(true)
    val previousChannel = MutableStateFlow<MediaItem?>(null)
    private var currentLive: MediaItem? = null

    data class GuideSel(val itemId: String? = null, val progStart: Long? = null)
    val guideSel = MutableStateFlow(GuideSel())

    fun selectGuide(item: MediaItem, p: Programme?) { guideSel.value = GuideSel(item.id, p?.start) }
    /** Retour au guide : la lecture continue dans le mini-lecteur. */
    fun minimize() { playerFull.value = false }
    fun expand() { playerFull.value = true }
    fun playPrevious() { previousChannel.value?.let { play(it) } }

    fun play(item: MediaItem) {
        val acc = selectedAccount.value ?: return
        if (item.kind == Kind.LIVE) {
            currentLive?.takeIf { it.id != item.id }?.let { previousChannel.value = it }
            currentLive = item
        }
        when (item.kind) {
            Kind.SERIES -> openEpisodes(item)
            else -> {
                val url = item.streamUrl ?: return
                val entry = userData.data.value.history.firstOrNull { it.accountId == acc.id && it.kind == item.kind && it.itemId == item.id }
                startPlayback(PlayRequest(url, item.name, item.kind, acc.id, item.id, logo = item.logo,
                    startMs = resumePosition(entry)))
            }
        }
    }

    /** Chaîne suivante / précédente dans la liste affichée (zapping, direct uniquement). */
    fun zap(delta: Int) {
        val cur = player.state.value.request ?: return
        if (cur.kind != Kind.LIVE) return
        val list = items.value.filter { it.kind == Kind.LIVE }
        if (list.isEmpty()) return
        val i = list.indexOfFirst { it.id == cur.itemId }
        val next = list[((if (i < 0) 0 else i + delta) % list.size + list.size) % list.size]
        play(next)
    }

    fun playEpisode(series: MediaItem, ep: Episode) {
        val acc = selectedAccount.value ?: return
        val entry = userData.data.value.history.firstOrNull { it.accountId == acc.id && it.kind == Kind.SERIES && it.itemId == series.id && it.episodeId == ep.id }
        episodes.value = null
        startPlayback(PlayRequest(ep.url, "${series.name} — ${ep.display}", Kind.SERIES, acc.id, series.id, ep.id, ep.season, ep.number,
            series.logo, resumePosition(entry)))
    }

    private fun resumePosition(e: WatchEntry?): Long = if (userData.data.value.resumePlayback && e?.inProgress == true) e.positionMs else 0

    fun startPlayback(req: PlayRequest) {
        playerFull.value = true
        reportProgress(flush = true)
        player.play(req, userData.data.value)
    }

    fun closePlayer() {
        reportProgress(flush = true)
        player.stop()
        player.clearRequest()
        playerFull.value = true
        currentLive = null
    }

    private fun entryOf(r: PlayRequest) = WatchEntry(r.accountId, r.kind, r.itemId, r.episodeId, r.season, r.number, r.title, r.logo)

    /** Enregistre la position du film / épisode en cours (au plus une fois par appel ; fin atteinte = vu). */
    fun reportProgress(flush: Boolean = false) {
        val s = player.state.value
        val r = s.request ?: return
        if (r.kind == Kind.LIVE || s.lengthMs <= 0 || r.accountId.isEmpty()) return
        val pos = if (s.ended) s.lengthMs else s.positionMs
        if (pos < 10_000) return
        userData.recordProgress(entryOf(r), pos, s.lengthMs)
        if (flush) userData.saveNow()
    }

    // ------------------------------------------------------------------ Épisodes

    val episodes = MutableStateFlow<EpisodesUi?>(null)

    fun openEpisodes(series: MediaItem) {
        val acc = selectedAccount.value ?: return
        episodes.value = EpisodesUi(series)
        viewModelScope.launch {
            try {
                val list = XtreamClient(acc).episodes(series.id)
                episodes.value = EpisodesUi(series, list, loading = false, error = if (list.isEmpty()) "empty" else null)
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) { episodes.value = EpisodesUi(series, loading = false, error = e.message ?: "error") }
        }
    }

    fun closeEpisodes() { episodes.value = null }

    fun episodeEntry(series: MediaItem, ep: Episode, ud: UserData): WatchEntry? {
        val acc = selectedAccount.value ?: return null
        return ud.history.firstOrNull { it.accountId == acc.id && it.kind == Kind.SERIES && it.itemId == series.id && it.episodeId == ep.id }
    }

    fun setEpisodeWatched(series: MediaItem, ep: Episode, watched: Boolean) {
        val acc = selectedAccount.value ?: return
        userData.setWatched(WatchEntry(acc.id, Kind.SERIES, series.id, ep.id, ep.season, ep.number, series.name, series.logo), watched)
    }

    override fun onCleared() {
        reportProgress(flush = true)
        userData.saveNow()
        player.release()
        super.onCleared()
    }
}
