package app.iptvplayer.net

import app.iptvplayer.model.Account
import app.iptvplayer.model.AccountType
import app.iptvplayer.model.Kind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ParsersTest {
    private val client = XtreamClient(Account("a1", "Test", AccountType.XTREAM, "demo.example:8080", "us er", "p@ss"))

    @Test fun xtreamCategories_acceptNumbersAndStrings() {
        val cats = client.parseCategories("""[{"category_id":"12","category_name":"Sports"},{"category_id":7,"category_name":"News"}]""", Kind.LIVE)
        assertEquals(listOf("12" to "Sports", "7" to "News"), cats.map { it.id to it.name })
    }

    @Test fun xtreamLive_buildsStreamUrlWithEncodedCredentials() {
        val items = client.parseItems("""[{"stream_id":101,"name":"CANAL D","category_id":"3","stream_icon":"http://x/l.png","epg_channel_id":"canald.ca"}]""", Kind.LIVE)
        val it = items.single()
        assertEquals("http://demo.example:8080/live/us%20er/p%40ss/101.ts", it.streamUrl)
        assertEquals("canald.ca", it.epgId)
        assertEquals("http://x/l.png", it.logo)
    }

    @Test fun xtreamMovie_usesContainerExtensionAndRating() {
        val it = client.parseItems("""[{"stream_id":"9","name":"Film","category_id":"1","container_extension":"mkv","rating":5.7}]""", Kind.MOVIE).single()
        assertEquals("http://demo.example:8080/movie/us%20er/p%40ss/9.mkv", it.streamUrl)
        assertEquals("5.7", it.rating)
    }

    @Test fun xtreamSeries_hasNoStreamUrl() {
        val it = client.parseItems("""[{"series_id":55,"name":"Serie","category_id":"2","cover":""}]""", Kind.SERIES).single()
        assertNull(it.streamUrl); assertNull(it.logo); assertEquals("55", it.id)
    }

    @Test fun xtreamEpisodes_groupedBySeason_titleCleaned_sorted() {
        val json = """{"episodes":{"2":[{"id":"22","season":2,"episode_num":2,"title":"Le Mécanisme - S02E02 - Del Este"},
            {"id":"21","season":2,"episode_num":"1","title":"Le Mécanisme - S02E01 - D'où vient la boue ?","container_extension":"mp4"}]}}"""
        val eps = client.parseEpisodes(json)
        assertEquals(listOf(1, 2), eps.map { it.number })
        assertEquals("D'où vient la boue ?", eps[0].title)
        assertTrue(eps[0].url.endsWith("/series/us%20er/p%40ss/21.mp4"))
    }

    @Test fun m3u_parsesGroupsLogoTvgIdAndHeaderEpg() {
        val text = """#EXTM3U url-tvg="http://epg.example/guide.xml.gz, http://other"
#EXTINF:-1 tvg-id="mux.test" tvg-logo="http://l/1.png" group-title="Test",Mux HLS Test
https://example.com/a.m3u8
#EXTINF:-1,Sans groupe
https://example.com/b.m3u8
"""
        val cat = M3uParser.parse(text)
        assertEquals("http://epg.example/guide.xml.gz", cat.epgUrl)
        assertEquals(2, cat.items.size)
        assertEquals("mux.test", cat.items[0].epgId)
        assertEquals(listOf("Test", "Uncategorized"), cat.categories.map { it.name })
        assertEquals("Mux HLS Test", cat.items[0].name)
    }
}
