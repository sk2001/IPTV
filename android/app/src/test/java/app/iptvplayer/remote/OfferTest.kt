package app.iptvplayer.remote

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class OfferTest {
    private var now = 1_000_000L
    private val registry = DeviceRegistry(null)
    private val pairing = PairingManager(registry) { now }
    private val fp = "a".repeat(64)

    private fun pairWith(pin: String): PairResult {
        val s = pairing.start("dev", "Phone", "android", "cn")!!
        return pairing.confirm(s.pairingId, PairCrypto.proof(pin, false, s.pairingId, "cn", s.serverNonce, fp), fp)
    }

    @Test fun offerPinIsReusedByPairingAndIsSingleUse() {
        val pin = pairing.openOffer()
        assertEquals(pin, pairing.currentPin)
        assertTrue(pairWith(pin) is PairResult.Ok)
        assertFalse(pairing.offerActive)                      // usage unique
        assertTrue(pairing.accountsAllowed("dev"))            // l'appareil configure peut echanger les comptes
        assertFalse(pairing.accountsAllowed("someone-else"))
    }

    @Test fun offerExpiresAfterFiveMinutes() {
        pairing.openOffer(); now += 301_000
        assertFalse(pairing.offerActive); assertNull(pairing.currentPin)
    }

    @Test fun tooManyWrongCodesCancelTheOffer() {
        val pin = pairing.openOffer()
        val wrong = if (pin == "000000") "000001" else "000000"
        repeat(10) { now += 13_000; pairWith(wrong) }          // espaces pour respecter la limite de 5 demarrages par minute
        assertFalse(pairing.offerActive)
        now += 13_000
        assertFalse(pairWith(pin) is PairResult.Ok)           // meme le bon code ne marche plus
    }

    @Test fun normalPairingDoesNotGrantAccountAccess() {
        val s = pairing.start("dev", "Phone", "android", "cn")!!
        val pin = pairing.currentPin!!
        assertTrue(pairing.confirm(s.pairingId, PairCrypto.proof(pin, false, s.pairingId, "cn", s.serverNonce, fp), fp) is PairResult.Ok)
        assertFalse(pairing.accountsAllowed("dev"))
    }

    @Test fun linkCarriesPinAndFingerprint() {
        val f = PairUri.parse(PairUri.build("192.168.1.9", 47231, "id1", "TV", "123456", fp, "androidtv"))!!
        assertEquals("123456", f.pin); assertEquals(fp, f.fingerprint); assertEquals("androidtv", f.platform)
        assertNull(PairUri.parse("iptvplayer://pair?host=192.168.1.9&port=1&pin=12ab56")!!.pin)       // PIN invalide ignore
        assertNull(PairUri.parse("iptvplayer://pair?host=192.168.1.9&port=1&fp=zz")!!.fingerprint)    // empreinte invalide ignoree
    }
}
