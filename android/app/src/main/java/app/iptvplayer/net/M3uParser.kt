package app.iptvplayer.net

import app.iptvplayer.model.Catalog
import app.iptvplayer.model.Category
import app.iptvplayer.model.Kind
import app.iptvplayer.model.MediaItem
import java.io.File

/** Analyse d'une playlist M3U / M3U8. Équivalent Kotlin de M3uParser.cs. */
object M3uParser {
    private val ATTR = Regex("""([\w-]+)="([^"]*)"""")

    fun load(urlOrPath: String): Catalog {
        val text = if (File(urlOrPath).isFile) File(urlOrPath).readText() else Http.getString(urlOrPath)
        return parse(text)
    }

    fun parse(text: String, uncategorized: String = "Uncategorized"): Catalog {
        val groups = LinkedHashMap<String, Category>()
        val items = ArrayList<MediaItem>()
        var epgUrl: String? = null
        var name: String? = null
        var logo: String? = null
        var group: String? = null
        var tvgId: String? = null
        var n = 0

        for (raw in text.lineSequence()) {
            val line = raw.trim()
            if (line.isEmpty()) continue
            when {
                line.startsWith("#EXTM3U", true) -> for (m in ATTR.findAll(line)) {
                    val k = m.groupValues[1].lowercase()
                    if ((k == "url-tvg" || k == "x-tvg-url") && epgUrl == null)
                        epgUrl = m.groupValues[2].split(',').map { it.trim() }.firstOrNull { it.isNotEmpty() }
                }
                line.startsWith("#EXTINF", true) -> {
                    logo = null; group = null; tvgId = null
                    for (m in ATTR.findAll(line)) when (m.groupValues[1].lowercase()) {
                        "tvg-logo" -> logo = m.groupValues[2]
                        "group-title" -> group = m.groupValues[2]
                        "tvg-id" -> tvgId = m.groupValues[2]
                    }
                    name = line.substringAfterLast(',', "").trim().ifEmpty { "Unnamed" }
                }
                !line.startsWith("#") && name != null -> {
                    val g = group?.ifBlank { null } ?: uncategorized
                    groups.getOrPut(g) { Category(g, g, Kind.LIVE) }
                    items += MediaItem(
                        id = (++n).toString(), name = name!!, kind = Kind.LIVE, categoryId = g,
                        logo = logo?.ifBlank { null }, streamUrl = line, epgId = tvgId?.ifBlank { null },
                    )
                    name = null
                }
            }
        }
        return Catalog(categories = groups.values.toList(), items = items, epgUrl = epgUrl)
    }
}
