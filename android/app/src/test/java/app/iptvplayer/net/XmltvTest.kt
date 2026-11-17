package app.iptvplayer.net

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayOutputStream
import java.util.zip.GZIPOutputStream

class XmltvTest {
    private val xml = """<?xml version="1.0" encoding="UTF-8"?><tv>
        <channel id="a"><display-name>A</display-name></channel>
        <programme start="20240101100000 +0100" stop="20240101110000 +0100" channel="a"><title lang="fr">Journal</title><desc>x</desc></programme>
        <programme start="20240101110000 +0100" stop="20240101120000 +0100" channel="a"><title>Film</title></programme>
        <programme start="20240101100000 +0000" stop="20240101110000 +0000" channel="b"><title>Autre</title></programme>
        <programme start="20230101100000 +0000" stop="20230101110000 +0000" channel="a"><title>Trop ancien</title></programme>
        </tv>"""

    @Test fun timeWithOffset() {
        assertEquals(Xmltv.parseTime("20240101090000 +0000"), Xmltv.parseTime("20240101100000 +0100"))
        assertEquals(0L, Xmltv.parseTime("n/a"))
    }

    @Test fun filtersChannelsAndWindow() {
        val from = Xmltv.parseTime("20240101090000"); val to = Xmltv.parseTime("20240102000000")
        val r = Xmltv.parse(xml.byteInputStream(), setOf("a"), from, to)
        assertEquals(listOf("Journal", "Film"), r["a"]!!.map { it.title })
        assertTrue("b" !in r)
    }

    @Test fun readsGzip() {
        val bos = ByteArrayOutputStream(); GZIPOutputStream(bos).use { it.write(xml.toByteArray()) }
        val r = Xmltv.parse(bos.toByteArray().inputStream(), emptySet(), 0, Long.MAX_VALUE)
        assertEquals(setOf("a", "b"), r.keys)
    }
}
