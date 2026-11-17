package app.iptvplayer.remote

import app.iptvplayer.AppViewModel
import app.iptvplayer.model.Kind
import app.iptvplayer.model.PlayRequest
import app.iptvplayer.player.PlayerState
import app.iptvplayer.sync.SyncEngine
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put
import kotlinx.serialization.json.putJsonArray
import kotlinx.serialization.json.putJsonObject
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone

/** Représentation JSON de l'état du lecteur (protocole v1, §6). Pur : testable sur le JVM. */
fun playerStateJson(ps: PlayerState, now: Long = System.currentTimeMillis()): JsonObject = buildJsonObject {
    val r = ps.request
    put("protocol", PROTOCOL)
    put("state", when {
        r == null -> "idle"; ps.error -> "error"; ps.playing -> "playing"
        ps.bufferingPercent < 100 && ps.positionMs == 0L -> "buffering"; else -> "paused"
    })
    if (r != null) putJsonObject("media") {
        put("title", r.title)
        put("kind", when (r.kind) { Kind.LIVE -> "live"; Kind.MOVIE -> "movie"; Kind.SERIES -> "series" })
        putJsonObject("ref") { put("accountId", r.accountId); put("itemId", r.itemId); r.episodeId?.let { put("episodeId", it) } }
        r.logo?.let { put("logo", it) }
    }
    put("positionMs", ps.positionMs); put("durationMs", ps.lengthMs); put("seekable", ps.seekable)
    put("volume", ps.volume); put("muted", ps.muted)
    putJsonArray("audioTracks") { ps.audioTracks.forEach { add(buildJsonObject { put("id", it.id); put("name", it.name) }) } }
    put("audioTrackId", ps.audioTrackId)
    putJsonArray("subtitleTracks") { ps.subtitleTracks.forEach { add(buildJsonObject { put("id", it.id); put("name", it.name) }) } }
    put("subtitleTrackId", ps.subtitleTrackId)
    putJsonObject("recording") { put("active", false); put("elapsedMs", 0); put("bytes", 0) }
    put("fullscreen", r != null)
    put("canNext", r?.kind == Kind.LIVE); put("canPrev", r?.kind == Kind.LIVE)
    put("updatedAt", SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.US).apply { timeZone = TimeZone.getTimeZone("UTC") }.format(Date(now)))
}

/** Pont entre le protocole de contrôle et l'application ; les commandes s'exécutent sur le thread principal. */
class RemoteTarget(
    private val vm: AppViewModel,
    override val deviceId: String,
    override val deviceName: String,
    override val platform: String,
    override val appVersion: String,
    private val syncAccounts: (String) -> Boolean = { false },
) : ControlTarget {

    override fun sync(deviceId: String, method: String, collection: String, body: String): Pair<Int, String>? {
        val engine = vm.sync
        if (collection == "manifest" && method == "GET") {
            val o = buildJsonObject {
                engine.collections.filter { it != "accounts" || syncAccounts(deviceId) }.forEach { c ->
                    val recs = engine.snapshot(c).values
                    putJsonObject(c) { put("count", recs.size); put("latest", SyncEngine.iso(recs.maxOfOrNull { it.updatedAt } ?: 0)) }
                }
            }
            return 200 to o.toString()
        }
        if (collection !in engine.collections) return null
        if (collection == "accounts" && !syncAccounts(deviceId)) return 403 to """{"error":{"code":"forbidden","message":"Account sync is disabled on this device"}}"""
        return when (method) {
            "GET" -> 200 to SyncEngine.toJson(engine.snapshot(collection).values)
            "PUT" -> 200 to """{"applied":${engine.merge(collection, SyncEngine.fromJson(body))}}"""
            else -> null
        }
    }

    override fun stateJson(): JsonObject = playerStateJson(vm.player.state.value)

    override fun command(cmd: JsonObject): String? = runBlocking(Dispatchers.Main.immediate) { run(cmd) }

    private fun JsonObject.str(k: String) = this[k]?.jsonPrimitive?.contentOrNull
    private fun JsonObject.int(k: String) = this[k]?.jsonPrimitive?.intOrNull
    private fun JsonObject.long(k: String) = this[k]?.jsonPrimitive?.longOrNull

    private fun kindOf(s: String?) = when (s) { "live" -> Kind.LIVE; "series" -> Kind.SERIES; else -> Kind.MOVIE }

    private fun run(c: JsonObject): String? {
        val p = vm.player
        val st = p.state.value
        when (c.str("type")) {
            "play" -> {
                val src = c["source"]?.jsonObject ?: return "source required"
                val url = src.str("url")?.takeIf { it.startsWith("http", true) || it.startsWith("rtsp", true) || it.startsWith("rtmp", true) || it.startsWith("udp", true) } ?: return "valid url required"
                val ref = src["ref"] as? JsonObject
                vm.startPlayback(PlayRequest(url, src.str("title") ?: url, kindOf(src.str("kind")), ref?.str("accountId").orEmpty(), ref?.str("itemId").orEmpty(),
                    ref?.str("episodeId"), logo = src.str("logo"), startMs = c.long("startMs") ?: 0))
            }
            "playRef" -> {
                val ref = c["ref"] as? JsonObject ?: return "ref required"
                if (ref.str("episodeId") != null) return "episodes are not supported by playRef"
                if (vm.selectedAccount.value?.id != ref.str("accountId")) return "unknown account"
                val item = vm.catalog.value?.items?.firstOrNull { it.id == ref.str("itemId") && it.kind != Kind.SERIES } ?: return "item not found"
                vm.play(item)
            }
            "pause" -> if (st.playing) p.togglePause()
            "resume" -> if (!st.playing && st.request != null) p.togglePause()
            "toggle" -> p.togglePause()
            "stop" -> vm.closePlayer()
            "seek" -> p.seekTo(c.long("positionMs") ?: return "positionMs required")
            "seekBy" -> p.seekBy(c.long("deltaMs") ?: return "deltaMs required")
            "volume" -> p.setVolume(c.int("level") ?: return "level required")
            "volumeBy" -> p.setVolume(st.volume + (c.int("delta") ?: return "delta required"))
            "mute" -> p.setMuted(c["muted"]?.jsonPrimitive?.contentOrNull?.toBooleanStrictOrNull() ?: !st.muted)
            "next", "prev" -> {
                if (st.request?.kind != Kind.LIVE) return "only available on live channels"
                vm.zap(if (c.str("type") == "next") 1 else -1)
            }
            "audioTrack" -> if (c.str("id") == "next") p.cycleAudio() else p.setAudio(c.int("id") ?: return "id required")
            "subtitleTrack" -> when (val v = c.str("id")) {
                "next" -> p.cycleSubtitles(); "off" -> p.setSubtitle(-1)
                else -> p.setSubtitle(v?.toIntOrNull() ?: return "id required")
            }
            "fullscreen" -> Unit
            else -> return "not supported on this device"
        }
        return null
    }
}
