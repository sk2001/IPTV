package app.iptvplayer.net

import app.iptvplayer.model.Account
import app.iptvplayer.model.Catalog
import app.iptvplayer.model.Category
import app.iptvplayer.model.Episode
import app.iptvplayer.model.Kind
import app.iptvplayer.model.MediaItem
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import java.net.URLEncoder

/** Client de l'API Xtream Codes (player_api.php). Équivalent Kotlin de XtreamClient.cs. */
class XtreamClient(account: Account) {
    private val base = account.url.trim().trimEnd('/').let { if (it.startsWith("http", true)) it else "http://$it" }
    private val user = account.username.trim()
    private val pass = account.password
    private val uEnc = enc(user)
    private val pEnc = enc(pass)

    val xmltvUrl: String get() = "$base/xmltv.php?username=$uEnc&password=$pEnc"

    private fun api(action: String = "", extra: String = ""): String {
        var u = "$base/player_api.php?username=$uEnc&password=$pEnc"
        if (action.isNotEmpty()) u += "&action=$action"
        return u + extra
    }

    /** Vérifie les identifiants. Retourne un message d'erreur, ou null si tout va bien. */
    suspend fun test(): String? = withContext(Dispatchers.IO) {
        try {
            val root = parse(Http.getString(api())) as? JsonObject ?: return@withContext "Unexpected server response"
            val ui = root["user_info"] as? JsonObject ?: return@withContext "Unexpected server response"
            if (str(ui, "auth") == "0") "Credentials rejected by the server" else null
        } catch (e: Exception) { e.message ?: e.javaClass.simpleName }
    }

    suspend fun fetchCatalog(onProgress: (Kind) -> Unit = {}): Catalog = coroutineScope {
        val kinds = listOf(
            Triple(Kind.LIVE, "get_live_categories", "get_live_streams"),
            Triple(Kind.MOVIE, "get_vod_categories", "get_vod_streams"),
            Triple(Kind.SERIES, "get_series_categories", "get_series"),
        )
        // Les six requêtes partent en parallèle.
        val jobs = kinds.map { (kind, catAction, itemAction) ->
            Triple(kind, async(Dispatchers.IO) { safeGet(api(catAction)) }, async(Dispatchers.IO) { safeGet(api(itemAction)) })
        }
        val categories = ArrayList<Category>()
        val items = ArrayList<MediaItem>()
        for ((kind, cats, its) in jobs) {
            val c = cats.await(); val i = its.await()
            onProgress(kind)
            categories += parseCategories(c, kind)
            items += parseItems(i, kind)
        }
        Catalog(categories = categories, items = items, epgUrl = xmltvUrl)
    }

    suspend fun episodes(seriesId: String): List<Episode> = withContext(Dispatchers.IO) {
        parseEpisodes(Http.getString(api("get_series_info", "&series_id=$seriesId")))
    }

    // ------------------------------------------------------------------ Analyse (testable sans réseau)

    fun parseCategories(json: String?, kind: Kind): List<Category> =
        (json?.let(::parse) as? JsonArray).orEmpty().mapNotNull { e ->
            (e as? JsonObject)?.let { Category(str(it, "category_id"), str(it, "category_name"), kind) }
        }

    fun parseItems(json: String?, kind: Kind): List<MediaItem> =
        (json?.let(::parse) as? JsonArray).orEmpty().mapNotNull { e -> (e as? JsonObject)?.let { parseItem(it, kind) } }

    private fun parseItem(e: JsonObject, kind: Kind): MediaItem = when (kind) {
        Kind.LIVE -> {
            val id = str(e, "stream_id")
            MediaItem(id, str(e, "name"), kind, str(e, "category_id"), nullIfEmpty(str(e, "stream_icon")),
                streamUrl = "$base/live/$uEnc/$pEnc/$id.ts", epgId = nullIfEmpty(str(e, "epg_channel_id")))
        }
        Kind.MOVIE -> {
            val id = str(e, "stream_id")
            val ext = nullIfEmpty(str(e, "container_extension")) ?: "mp4"
            MediaItem(id, str(e, "name"), kind, str(e, "category_id"), nullIfEmpty(str(e, "stream_icon")),
                streamUrl = "$base/movie/$uEnc/$pEnc/$id.$ext", rating = nullIfEmpty(str(e, "rating")))
        }
        Kind.SERIES -> MediaItem(str(e, "series_id"), str(e, "name"), kind, str(e, "category_id"),
            nullIfEmpty(str(e, "cover")), rating = nullIfEmpty(str(e, "rating")))
    }

    fun parseEpisodes(json: String): List<Episode> {
        val eps = (parse(json) as? JsonObject)?.get("episodes") ?: return emptyList()
        val all: List<JsonElement> = when (eps) {
            is JsonObject -> eps.values.flatMap { (it as? JsonArray).orEmpty() }
            is JsonArray -> eps.flatMap { (it as? JsonArray).orEmpty() }
            else -> emptyList()
        }
        return all.mapNotNull { it as? JsonObject }.map { e ->
            val id = str(e, "id")
            val ext = nullIfEmpty(str(e, "container_extension")) ?: "mp4"
            Episode(id, str(e, "season").toIntOrNull() ?: 0, str(e, "episode_num").toIntOrNull() ?: 0,
                cleanEpisodeTitle(str(e, "title")), "$base/series/$uEnc/$pEnc/$id.$ext")
        }.sortedWith(compareBy({ it.season }, { it.number }))
    }

    private suspend fun safeGet(url: String): String? = try { Http.getString(url) } catch (e: Exception) { null }

    companion object {
        private val json = Json { isLenient = true; ignoreUnknownKeys = true }
        private fun parse(s: String): JsonElement? = try { json.parseToJsonElement(s) } catch (e: Exception) { null }
        private fun enc(s: String) = URLEncoder.encode(s, "UTF-8").replace("+", "%20")
        private fun nullIfEmpty(s: String) = s.ifBlank { null }

        /** Les valeurs Xtream sont tantôt des chaînes, tantôt des nombres : on lit tout comme du texte. */
        fun str(o: JsonObject, key: String): String = (o[key] as? JsonPrimitive)?.content.orEmpty()

        private val EP_PREFIX = Regex("""^(?:.*?\s[-–]\s)?S\d{1,3}\s?E\d{1,4}\s*[-–:]?\s*""", RegexOption.IGNORE_CASE)

        /** « Série - S02E01 - Titre » → « Titre » (saison et épisode sont déjà affichés à part). */
        fun cleanEpisodeTitle(title: String): String = EP_PREFIX.replace(title, "").trim().ifEmpty { title }
    }
}
