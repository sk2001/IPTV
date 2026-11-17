package app.iptvplayer.sync

import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

private class MapAdapter(val items: MutableMap<String, JsonObject> = mutableMapOf(), val stamps: Map<String, Long> = emptyMap()) : SyncAdapter {
    override fun current(): Map<String, JsonObject> = items.toMap()
    override fun apply(upserts: Map<String, JsonObject>, deletes: Set<String>) { items.putAll(upserts); deletes.forEach(items::remove) }
    override fun stamp(id: String, data: JsonObject) = stamps[id]
}

private fun obj(v: String) = buildJsonObject { put("v", v) }

class SyncEngineTest {
    private var now = 1_000L
    private fun engine(id: String, a: MapAdapter) = SyncEngine(id, SyncMeta(null), mapOf("c" to a)) { now }

    /** Une synchronisation complète A -> B -> A, comme le fait un contrôleur. */
    private fun sync(local: SyncEngine, remote: SyncEngine) {
        val remoteSnap = remote.snapshot("c")
        local.merge("c", remoteSnap.values)
        val push = local.missingOn("c", remoteSnap)
        remote.merge("c", push)
    }

    @Test fun addsPropagateBothWays() {
        val a = MapAdapter(mutableMapOf("x" to obj("1"))); val b = MapAdapter(mutableMapOf("y" to obj("2")))
        sync(engine("A", a), engine("B", b))
        assertEquals(setOf("x", "y"), a.items.keys); assertEquals(setOf("x", "y"), b.items.keys)
    }

    @Test fun newerEditWins() {
        val a = MapAdapter(mutableMapOf("x" to obj("old"))); val b = MapAdapter(mutableMapOf("x" to obj("old")))
        val ea = engine("A", a); val eb = engine("B", b)
        sync(ea, eb)                                   // les deux connaissent « old »
        now = 2_000; a.items["x"] = obj("new-A"); ea.snapshot("c")
        now = 3_000; b.items["x"] = obj("new-B"); eb.snapshot("c")
        sync(ea, eb)
        assertEquals("new-B", a.items["x"]!!["v"].toString().trim('"')); assertEquals("new-B", b.items["x"]!!["v"].toString().trim('"'))
    }

    @Test fun deletionPropagatesAndDoesNotResurrect() {
        val a = MapAdapter(mutableMapOf("x" to obj("1"))); val b = MapAdapter(mutableMapOf("x" to obj("1")))
        val ea = engine("A", a); val eb = engine("B", b)
        sync(ea, eb)
        now = 2_000; a.items.remove("x")
        sync(ea, eb)
        assertTrue(a.items.isEmpty()); assertTrue(b.items.isEmpty())
        now = 3_000; sync(ea, eb)                      // une seconde synchro ne ressuscite rien
        assertTrue(a.items.isEmpty()); assertTrue(b.items.isEmpty())
    }

    @Test fun secondSyncIsQuiet() {
        val a = MapAdapter(mutableMapOf("x" to obj("1"))); val b = MapAdapter()
        val ea = engine("A", a); val eb = engine("B", b)
        sync(ea, eb); now = 5_000
        val remote = eb.snapshot("c")
        assertEquals(0, ea.merge("c", remote.values)); assertTrue(ea.missingOn("c", remote).isEmpty())
    }

    @Test fun naturalStampDecidesHistory() {
        val a = MapAdapter(mutableMapOf("h" to obj("A")), mapOf("h" to 9_000L)); val b = MapAdapter(mutableMapOf("h" to obj("B")), mapOf("h" to 5_000L))
        sync(engine("A", a), engine("B", b))
        assertEquals("A", b.items["h"]!!["v"].toString().trim('"'))
    }

    @Test fun jsonRoundTripAndCanonicalHash() {
        val r = SyncRecord("k", 1_700_000_000_123, false, buildJsonObject { put("b", "1"); put("a", "2") }, "dev")
        val back = SyncEngine.fromJson(SyncEngine.toJson(listOf(r))).single()
        assertEquals(r.updatedAt, back.updatedAt)
        assertEquals(SyncEngine.hash(r.data!!), SyncEngine.hash(buildJsonObject { put("a", "2"); put("b", "1") }))   // l'ordre des clés ne compte pas
    }
}
