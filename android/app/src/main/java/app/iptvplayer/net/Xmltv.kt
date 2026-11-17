package app.iptvplayer.net

import org.xml.sax.Attributes
import org.xml.sax.helpers.DefaultHandler
import java.io.BufferedInputStream
import java.io.InputStream
import java.util.zip.GZIPInputStream
import javax.xml.parsers.SAXParserFactory

data class Programme(val start: Long, val stop: Long, val title: String)

/** Guide XMLTV : lecture en flux (SAX), fenêtre de temps limitée et chaînes demandées uniquement. */
object Xmltv {
    /** Garde les programmes qui se terminent après [from] et commencent avant [to] (millisecondes epoch). */
    fun parse(input: InputStream, wanted: Set<String>, from: Long, to: Long): Map<String, List<Programme>> {
        val result = HashMap<String, MutableList<Programme>>()
        val buffered = BufferedInputStream(input)
        buffered.mark(2)
        val b0 = buffered.read(); val b1 = buffered.read()
        buffered.reset()
        val stream = if (b0 == 0x1f && b1 == 0x8b) GZIPInputStream(buffered) else buffered   // certains serveurs envoient du gzip brut

        val factory = SAXParserFactory.newInstance()
        runCatching { factory.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true) }
        factory.newSAXParser().parse(stream, object : DefaultHandler() {
            var channel: String? = null; var start = 0L; var stop = 0L; var inTitle = false; var title: StringBuilder? = null

            override fun startElement(uri: String?, local: String?, qName: String, a: Attributes) {
                when (qName) {
                    "programme" -> {
                        channel = a.getValue("channel")?.takeIf { wanted.isEmpty() || it in wanted }
                        start = parseTime(a.getValue("start")); stop = parseTime(a.getValue("stop")); title = null
                    }
                    "title" -> if (channel != null && title == null) { inTitle = true; title = StringBuilder() }
                }
            }
            override fun characters(ch: CharArray, s: Int, l: Int) { if (inTitle) title?.append(ch, s, l) }
            override fun endElement(uri: String?, local: String?, qName: String) {
                if (qName == "title") inTitle = false
                if (qName == "programme") {
                    val c = channel
                    if (c != null && start > 0 && stop > start && stop > from && start < to) {
                        result.getOrPut(c) { ArrayList() }.add(Programme(start, stop, title?.toString()?.trim().orEmpty()))
                    }
                    channel = null
                }
            }
        })
        return result.mapValues { (_, v) -> v.sortedBy { it.start } }
    }

    /** "20241116203000 +0100" (fuseau optionnel, UTC par défaut) -> millisecondes epoch, 0 si invalide. */
    fun parseTime(s: String?): Long {
        if (s == null) return 0
        val t = s.trim()
        if (t.length < 14 || !t.take(14).all { it.isDigit() }) return 0
        val y = t.substring(0, 4).toInt(); val mo = t.substring(4, 6).toInt(); val d = t.substring(6, 8).toInt()
        val h = t.substring(8, 10).toInt(); val mi = t.substring(10, 12).toInt(); val se = t.substring(12, 14).toInt()
        val cal = java.util.Calendar.getInstance(java.util.TimeZone.getTimeZone("UTC"))
        cal.clear(); cal.set(y, mo - 1, d, h, mi, se)
        var ms = cal.timeInMillis
        val tz = t.substring(14).trim()
        if (tz.length >= 5 && (tz[0] == '+' || tz[0] == '-')) {
            val off = (tz.substring(1, 3).toIntOrNull() ?: 0) * 60 + (tz.substring(3, 5).toIntOrNull() ?: 0)
            ms -= (if (tz[0] == '+') 1 else -1) * off * 60_000L
        }
        return ms
    }
}
