package app.iptvplayer.data

import android.content.Context
import app.iptvplayer.model.Kind
import app.iptvplayer.model.WatchEntry
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.io.File

/** Préférences, favoris et historique de visionnage (un seul fichier JSON dans le stockage privé de l'application). */
@Serializable
data class UserData(
    val audioLangVod: String = "en",      // films & séries
    val audioLangLive: String = "",       // chaînes live (« » = automatique)
    val subLangVod: String = "",          // sous-titres films & séries (« » = aucun)
    val resumePlayback: Boolean = true,
    val lastAccountId: String? = null,
    val favorites: Set<String> = emptySet(),
    val history: List<WatchEntry> = emptyList(),
)

class UserDataRepository(context: Context, private val scope: CoroutineScope) {
    private val file = File(context.filesDir, "userdata.json")
    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true }
    private val _data = MutableStateFlow(load())
    val data: StateFlow<UserData> = _data.asStateFlow()
    private var saveJob: Job? = null

    private fun load(): UserData = try {
        if (file.exists()) json.decodeFromString(UserData.serializer(), file.readText()) else UserData()
    } catch (e: Exception) { UserData() }

    fun update(transform: (UserData) -> UserData) {
        _data.update(transform)
        saveJob?.cancel()
        saveJob = scope.launch(Dispatchers.IO) { delay(400); saveNow() }      // regroupe les écritures rapprochées
    }

    fun saveNow() {
        try {
            val tmp = File(file.parentFile, "userdata.json.tmp")
            tmp.writeText(json.encodeToString(UserData.serializer(), _data.value))
            if (!tmp.renameTo(file)) { file.delete(); tmp.renameTo(file) }
        } catch (_: Exception) { }
    }

    // ------------------------------------------------------------------ Favoris

    fun favoriteKey(accountId: String, kind: Kind, itemId: String) = "$accountId|${kind.ordinal}|$itemId"

    fun toggleFavorite(accountId: String, kind: Kind, itemId: String) = update { d ->
        val k = favoriteKey(accountId, kind, itemId)
        d.copy(favorites = if (k in d.favorites) d.favorites - k else d.favorites + k)
    }

    // ------------------------------------------------------------------ Historique

    /** Met à jour la position ; terminé à 92 % (ou à moins d'une minute de la fin). */
    fun recordProgress(base: WatchEntry, positionMs: Long, durationMs: Long) = update { d ->
        val done = durationMs > 0 && (positionMs >= durationMs * 0.92 || durationMs - positionMs < 60_000)
        val entry = base.copy(positionMs = if (done) 0 else positionMs, durationMs = durationMs, completed = done, lastWatched = System.currentTimeMillis())
        d.copy(history = (d.history.filterNot { it.key == entry.key } + entry).sortedByDescending { it.lastWatched }.take(MAX_HISTORY))
    }

    fun setWatched(base: WatchEntry, watched: Boolean) = update { d ->
        val rest = d.history.filterNot { it.key == base.key }
        d.copy(history = if (!watched) rest else (rest + base.copy(completed = true, positionMs = 0, lastWatched = System.currentTimeMillis())).take(MAX_HISTORY))
    }

    fun removeItem(accountId: String, kind: Kind, itemId: String) = update { d ->
        d.copy(history = d.history.filterNot { it.accountId == accountId && it.kind == kind && it.itemId == itemId })
    }

    fun clearHistory() = update { it.copy(history = emptyList()) }

    private companion object { const val MAX_HISTORY = 5000 }
}
