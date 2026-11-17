package app.iptvplayer.model

import kotlinx.serialization.Serializable

@Serializable
enum class Kind { LIVE, MOVIE, SERIES }

@Serializable
enum class AccountType { XTREAM, M3U }

/** Compte IPTV. Le mot de passe est chiffré avec le Keystore Android au repos (voir AccountStore). */
@Serializable
data class Account(
    val id: String,
    val name: String,
    val type: AccountType,
    /** Xtream : http://hote:port — M3U : URL de la playlist. */
    val url: String,
    val username: String = "",
    val password: String = "",
)

@Serializable
data class Category(val id: String, val name: String, val kind: Kind)

@Serializable
data class MediaItem(
    val id: String,
    val name: String,
    val kind: Kind,
    val categoryId: String,
    val logo: String? = null,
    /** URL de lecture (live, film, M3U). Absente pour les séries (résolue à la demande). */
    val streamUrl: String? = null,
    val rating: String? = null,
    /** Identifiant de chaîne dans le guide XMLTV. */
    val epgId: String? = null,
)

@Serializable
data class Catalog(
    val version: Int = CURRENT_VERSION,
    val fetchedAt: Long = System.currentTimeMillis(),
    val categories: List<Category> = emptyList(),
    val items: List<MediaItem> = emptyList(),
    val epgUrl: String? = null,
) {
    companion object { const val CURRENT_VERSION = 1 }
}

@Serializable
data class Episode(
    val id: String,
    val season: Int,
    val number: Int,
    val title: String,
    val url: String,
) {
    val display: String get() = "S%02dE%02d · %s".format(season, number, title)
}

/** Historique de visionnage d'un film ou d'un épisode (mêmes clés que l'application Windows). */
@Serializable
data class WatchEntry(
    val accountId: String,
    val kind: Kind,
    val itemId: String,
    val episodeId: String? = null,
    val season: Int = 0,
    val number: Int = 0,
    val name: String = "",
    val logo: String? = null,
    val positionMs: Long = 0,
    val durationMs: Long = 0,
    val completed: Boolean = false,
    val lastWatched: Long = 0,
) {
    val key: String get() = key(accountId, kind, itemId, episodeId)
    val progress: Double get() = if (durationMs > 0) (positionMs.toDouble() / durationMs).coerceIn(0.0, 1.0) else 0.0
    /** Entamé mais pas terminé (assez avancé pour proposer de reprendre). */
    val inProgress: Boolean get() = !completed && positionMs >= 30_000 && progress < 0.97

    companion object {
        fun key(account: String, kind: Kind, itemId: String, episodeId: String?) =
            "$account|${kind.ordinal}|$itemId|${episodeId ?: ""}"
    }
}

/** Ce que le lecteur doit connaître d'un média pour suivre la progression et reprendre. */
data class PlayRequest(
    val url: String,
    val title: String,
    val kind: Kind,
    val accountId: String,
    val itemId: String,
    val episodeId: String? = null,
    val season: Int = 0,
    val number: Int = 0,
    val logo: String? = null,
    val startMs: Long = 0,
)
