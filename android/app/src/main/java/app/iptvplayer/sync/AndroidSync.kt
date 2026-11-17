package app.iptvplayer.sync

import android.app.Application
import app.iptvplayer.AppViewModel
import app.iptvplayer.data.UserData
import app.iptvplayer.model.Account
import app.iptvplayer.model.AccountType
import app.iptvplayer.model.Kind
import app.iptvplayer.model.WatchEntry
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put
import java.io.File

/** Collections synchronisées sur Android : favoris, historique, préférences de lecture et (si activé) comptes. */
object AndroidSync {
    const val ACCOUNTS = "accounts"

    fun create(app: Application, vm: AppViewModel, deviceId: String): SyncEngine {
        val adapters = mapOf(
            "favorites" to favorites(vm), "history" to history(vm), "settings" to settings(vm), ACCOUNTS to accounts(vm),
        )
        return SyncEngine(deviceId, SyncMeta(File(app.filesDir, "sync-meta.json")), adapters)
    }

    private fun favorites(vm: AppViewModel) = object : SyncAdapter {
        override fun current() = vm.userData.data.value.favorites.associateWith { JsonObject(emptyMap()) }
        override fun apply(upserts: Map<String, JsonObject>, deletes: Set<String>) {
            vm.userData.update { it.copy(favorites = it.favorites + upserts.keys - deletes) }
        }
    }

    private fun history(vm: AppViewModel) = object : SyncAdapter {
        override fun current() = vm.userData.data.value.history.associate { it.key to toJson(it) }
        override fun stamp(id: String, data: JsonObject) = data["lastWatched"]?.jsonPrimitive?.longOrNull
        override fun apply(upserts: Map<String, JsonObject>, deletes: Set<String>) {
            vm.userData.update { d ->
                val incoming = upserts.values.map(::fromJson)
                val keep = d.history.filter { it.key !in upserts && it.key !in deletes }
                d.copy(history = (keep + incoming).sortedByDescending { it.lastWatched }.take(5000))
            }
        }
        fun toJson(e: WatchEntry) = buildJsonObject {
            put("accountId", e.accountId); put("kind", e.kind.ordinal); put("itemId", e.itemId); e.episodeId?.let { put("episodeId", it) }
            put("season", e.season); put("number", e.number); put("name", e.name); e.logo?.let { put("logo", it) }
            put("positionMs", e.positionMs); put("durationMs", e.durationMs); put("completed", e.completed); put("lastWatched", e.lastWatched)
        }
        fun fromJson(o: JsonObject) = WatchEntry(
            accountId = o["accountId"]!!.jsonPrimitive.content, kind = Kind.entries[o["kind"]!!.jsonPrimitive.intOrNull ?: 0], itemId = o["itemId"]!!.jsonPrimitive.content,
            episodeId = o["episodeId"]?.jsonPrimitive?.contentOrNull, season = o["season"]?.jsonPrimitive?.intOrNull ?: 0, number = o["number"]?.jsonPrimitive?.intOrNull ?: 0,
            name = o["name"]?.jsonPrimitive?.contentOrNull.orEmpty(), logo = o["logo"]?.jsonPrimitive?.contentOrNull,
            positionMs = o["positionMs"]?.jsonPrimitive?.longOrNull ?: 0, durationMs = o["durationMs"]?.jsonPrimitive?.longOrNull ?: 0,
            completed = o["completed"]?.jsonPrimitive?.booleanOrNull ?: false, lastWatched = o["lastWatched"]?.jsonPrimitive?.longOrNull ?: 0,
        )
    }

    private fun settings(vm: AppViewModel) = object : SyncAdapter {
        private fun v(s: String) = buildJsonObject { put("value", s) }
        override fun current(): Map<String, JsonObject> = vm.userData.data.value.let {
            mapOf("audioLangVod" to v(it.audioLangVod), "audioLangLive" to v(it.audioLangLive), "subLangVod" to v(it.subLangVod), "resumePlayback" to v(it.resumePlayback.toString()))
        }
        override fun apply(upserts: Map<String, JsonObject>, deletes: Set<String>) {
            fun get(k: String) = upserts[k]?.get("value")?.jsonPrimitive?.contentOrNull
            vm.userData.update { d: UserData ->
                d.copy(
                    audioLangVod = get("audioLangVod") ?: d.audioLangVod, audioLangLive = get("audioLangLive") ?: d.audioLangLive,
                    subLangVod = get("subLangVod") ?: d.subLangVod, resumePlayback = get("resumePlayback")?.toBooleanStrictOrNull() ?: d.resumePlayback,
                )
            }
        }
    }

    private fun accounts(vm: AppViewModel) = object : SyncAdapter {
        override fun current() = vm.accounts.value.associate { a ->
            a.id to buildJsonObject {
                put("name", a.name); put("type", if (a.type == AccountType.XTREAM) "xtream" else "m3u")
                put("url", a.url); put("username", a.username); put("password", a.password)
            }
        }
        override fun apply(upserts: Map<String, JsonObject>, deletes: Set<String>) {
            vm.applySyncedAccounts(upserts.map { (id, o) ->
                Account(id, o["name"]?.jsonPrimitive?.contentOrNull.orEmpty(),
                    if (o["type"]?.jsonPrimitive?.contentOrNull == "m3u") AccountType.M3U else AccountType.XTREAM,
                    o["url"]?.jsonPrimitive?.contentOrNull.orEmpty(), o["username"]?.jsonPrimitive?.contentOrNull.orEmpty(), o["password"]?.jsonPrimitive?.contentOrNull.orEmpty())
            }, deletes)
        }
    }
}
