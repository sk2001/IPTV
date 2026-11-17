package app.iptvplayer.remote

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PairUriTest {
    @Test fun roundTrip() {
        val f = PairUri.parse(PairUri.build("192.168.1.20", 47231, "abc-123", "Salon TV"))!!
        assertEquals("192.168.1.20", f.host); assertEquals(47231, f.port); assertEquals("abc-123", f.id); assertEquals("Salon TV", f.name)
    }

    @Test fun rejectsInvalidAndPublicTargets() {
        assertNull(PairUri.parse("https://example.com"))
        assertNull(PairUri.parse("iptvplayer://pair?host=93.184.216.34&port=47231&id=x"))   // adresse publique
        assertNull(PairUri.parse("iptvplayer://pair?host=evil.example.com&port=47231&id=x")) // nom de domaine
        assertNull(PairUri.parse("iptvplayer://pair?host=192.168.1.2&port=99999&id=x"))      // port invalide
        assertNull(PairUri.parse("iptvplayer://pair?port=47231"))
    }

    @Test fun localHosts() {
        assertTrue(PairUri.isLocalHost("10.0.2.2")); assertTrue(PairUri.isLocalHost("172.20.0.1")); assertTrue(PairUri.isLocalHost("192.168.0.9"))
        assertFalse(PairUri.isLocalHost("172.32.0.1")); assertFalse(PairUri.isLocalHost("8.8.8.8"))
    }
}
