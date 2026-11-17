package app.iptvplayer.data

import android.content.Context
import app.iptvplayer.model.Account
import app.iptvplayer.model.AccountType
import app.iptvplayer.model.Catalog
import app.iptvplayer.model.Kind
import app.iptvplayer.net.M3uParser
import app.iptvplayer.net.XtreamClient
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import java.io.File
import java.util.zip.GZIPInputStream
import java.util.zip.GZIPOutputStream

/** Catalogue d'un compte : cache disque compressé (affichage instantané) puis rafraîchissement réseau. */
class CatalogRepository(context: Context) {
    private val dir = File(context.filesDir, "cache").apply { mkdirs() }
    private val json = Json { ignoreUnknownKeys = true }

    private fun file(a: Account) = File(dir, "${a.id}.json.gz")

    suspend fun loadCache(a: Account): Catalog? = withContext(Dispatchers.IO) {
        try {
            val f = file(a)
            if (!f.exists()) return@withContext null
            val c = GZIPInputStream(f.inputStream().buffered()).use { json.decodeFromString(Catalog.serializer(), it.readBytes().toString(Charsets.UTF_8)) }
            c.takeIf { it.version == Catalog.CURRENT_VERSION }          // ancien format : on re-télécharge
        } catch (e: Exception) { null }
    }

    suspend fun fetch(a: Account, onProgress: (Kind) -> Unit = {}): Catalog {
        val c = if (a.type == AccountType.XTREAM) XtreamClient(a).fetchCatalog(onProgress)
        else withContext(Dispatchers.IO) { M3uParser.load(a.url) }
        val fresh = c.copy(version = Catalog.CURRENT_VERSION, fetchedAt = System.currentTimeMillis())
        withContext(Dispatchers.IO) { save(a, fresh) }
        return fresh
    }

    private fun save(a: Account, c: Catalog) {
        try {
            val tmp = File(dir, "${a.id}.tmp")
            GZIPOutputStream(tmp.outputStream().buffered()).use { it.write(json.encodeToString(Catalog.serializer(), c).toByteArray(Charsets.UTF_8)) }
            if (!tmp.renameTo(file(a))) { file(a).delete(); tmp.renameTo(file(a)) }
        } catch (_: Exception) { /* cache au mieux */ }
    }

    fun delete(a: Account) { file(a).delete() }

    fun isStale(c: Catalog) = System.currentTimeMillis() - c.fetchedAt > STALE_AFTER_MS

    private companion object { const val STALE_AFTER_MS = 12L * 60 * 60 * 1000 }
}
