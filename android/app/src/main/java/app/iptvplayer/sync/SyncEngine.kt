package app.iptvplayer.sync

import kotlinx.serialization.Serializable
import kotlinx.serialization.builtins.MapSerializer
import kotlinx.serialization.builtins.serializer
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.io.File
import java.security.MessageDigest
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone

/** Un enregistrement synchronisé (protocole v1, §8) : `data` est null pour une suppression (tombstone). */
data class SyncRecord(val id: String, val updatedAt: Long, val deleted: Boolean, val data: JsonObject?, val by: String)

/** Lien entre le moteur et le stockage d'une collection (favoris, historique…). */
interface SyncAdapter {
    /** identifiant -> données, pour l'état actuel de l'appareil. */
    fun current(): Map<String, JsonObject>
    /** Applique des changements venus du pair (upserts) et des suppressions. */
    fun apply(upserts: Map<String, JsonObject>, deletes: Set<String>)
    /** Horodatage naturel d'un enregistrement (ex. dernière lecture), null si l'on ne sait pas. */
    fun stamp(id: String, data: JsonObject): Long? = null
}

@Serializable
data class MetaEntry(val updatedAt: Long, val deleted: Boolean, val hash: String, val by: String)

/** Dates de modification connues, par collection et enregistrement (les changements locaux sont détectés à la synchro). */
class SyncMeta(private val file: File?) {
    private val ser = MapSerializer(String.serializer(), MapSerializer(String.serializer(), MetaEntry.serializer()))
    private val json = Json { ignoreUnknownKeys = true }
    private val data: MutableMap<String, MutableMap<String, MetaEntry>> =
        file?.takeIf { it.exists() }?.let { f -> runCatching { json.decodeFromString(ser, f.readText()).mapValues { it.value.toMutableMap() }.toMutableMap() }.getOrNull() } ?: mutableMapOf()

    fun of(collection: String): MutableMap<String, MetaEntry> = data.getOrPut(collection) { mutableMapOf() }

    fun save() { file?.let { f -> runCatching { f.writeText(json.encodeToString(ser, data)) } } }
}

class SyncEngine(
    private val deviceId: String,
    private val meta: SyncMeta,
    private val adapters: Map<String, SyncAdapter>,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    val collections: Set<String> get() = adapters.keys

    /** Enregistrements locaux (y compris suppressions récentes), après détection des changements locaux. */
    @Synchronized
    fun snapshot(collection: String): Map<String, SyncRecord> {
        val adapter = adapters[collection] ?: return emptyMap()
        val m = meta.of(collection)
        val now = clock()
        val cur = adapter.current()
        val out = LinkedHashMap<String, SyncRecord>()
        for ((id, data) in cur) {
            val h = hash(data)
            var e = m[id]
            if (e == null || e.deleted || e.hash != h) {
                e = MetaEntry(adapter.stamp(id, data) ?: now, false, h, deviceId)
                m[id] = e
            }
            out[id] = SyncRecord(id, e.updatedAt, false, data, e.by)
        }
        for ((id, e) in m.toList()) {
            if (id in cur) continue
            val t = if (e.deleted) e else MetaEntry(now, true, "", deviceId).also { m[id] = it }
            if (now - t.updatedAt > RETENTION_MS) m.remove(id) else out[id] = SyncRecord(id, t.updatedAt, true, null, t.by)
        }
        meta.save()
        return out
    }

    /** Fusionne les enregistrements du pair (la dernière écriture gagne ; à égalité, l'identifiant d'appareil le plus grand). Retourne le nombre appliqué. */
    @Synchronized
    fun merge(collection: String, incoming: Collection<SyncRecord>): Int {
        val adapter = adapters[collection] ?: return 0
        val local = snapshot(collection)
        val upserts = LinkedHashMap<String, SyncRecord>()
        val deletes = LinkedHashMap<String, SyncRecord>()
        for (r in incoming) {
            val l = local[r.id]
            if (l != null) {
                val same = l.deleted == r.deleted && (r.deleted || (l.data != null && r.data != null && hash(l.data) == hash(r.data)))
                if (same) continue
                val wins = r.updatedAt > l.updatedAt || (r.updatedAt == l.updatedAt && r.by > l.by)
                if (!wins) continue
            } else if (r.deleted) continue
            if (r.deleted) deletes[r.id] = r else if (r.data != null) upserts[r.id] = r
        }
        if (upserts.isEmpty() && deletes.isEmpty()) return 0
        adapter.apply(upserts.mapValues { it.value.data!! }, deletes.keys)
        val m = meta.of(collection)
        val after = adapter.current()
        for ((id, r) in upserts) m[id] = MetaEntry(r.updatedAt, false, hash(after[id] ?: r.data!!), r.by)   // empreinte de la forme locale : évite un faux « changement local »
        for ((id, r) in deletes) m[id] = MetaEntry(r.updatedAt, true, "", r.by)
        meta.save()
        return upserts.size + deletes.size
    }

    /** Ce que le pair n'a pas ou a en version plus ancienne. */
    fun missingOn(collection: String, remote: Map<String, SyncRecord>): List<SyncRecord> =
        snapshot(collection).values.filter { l ->
            val r = remote[l.id]
            when {
                r == null -> !l.deleted
                l.deleted != r.deleted || (!l.deleted && hash(l.data!!) != hash(r.data!!)) ->
                    l.updatedAt > r.updatedAt || (l.updatedAt == r.updatedAt && l.by > r.by)
                else -> false
            }
        }

    companion object {
        const val RETENTION_MS = 90L * 24 * 3600 * 1000

        /** Représentation canonique (clés triées) : deux appareils obtiennent la même empreinte pour le même contenu. */
        fun canon(e: JsonElement): String = when (e) {
            is JsonObject -> e.entries.sortedBy { it.key }.joinToString(",", "{", "}") { "\"${it.key}\":${canon(it.value)}" }
            is JsonArray -> e.joinToString(",", "[", "]") { canon(it) }
            is JsonNull -> "null"
            is JsonPrimitive -> e.toString()
        }

        fun hash(o: JsonObject): String = MessageDigest.getInstance("SHA-256").digest(canon(o).toByteArray()).joinToString("") { "%02x".format(it) }

        private fun fmt() = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.US).apply { timeZone = TimeZone.getTimeZone("UTC") }
        fun iso(ms: Long): String = fmt().format(Date(ms))
        fun parseIso(s: String): Long = fmt().parse(s)?.time ?: 0

        fun toJson(records: Collection<SyncRecord>): String = JsonArray(records.map { r ->
            buildJsonObject {
                put("id", r.id); put("updatedAt", iso(r.updatedAt)); put("deleted", r.deleted); put("by", r.by)
                put("data", r.data ?: JsonObject(emptyMap()))
            }
        }).toString()

        fun fromJson(text: String): List<SyncRecord> = Json.parseToJsonElement(text).let { it as JsonArray }.map { e ->
            val o = e.jsonObject
            val deleted = o["deleted"]?.jsonPrimitive?.boolean ?: false
            SyncRecord(o["id"]!!.jsonPrimitive.content, parseIso(o["updatedAt"]!!.jsonPrimitive.content), deleted,
                if (deleted) null else o["data"]?.jsonObject, o["by"]?.jsonPrimitive?.contentOrNull.orEmpty())
        }
    }
}
