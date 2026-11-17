package app.iptvplayer.remote

import java.io.InputStream
import java.io.OutputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.URLDecoder
import java.security.MessageDigest
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.Executors
import javax.net.ssl.SSLSocket
import kotlin.io.encoding.Base64
import kotlin.io.encoding.ExperimentalEncodingApi

/** Serveur HTTP(S) 1.1 minimal + WebSocket (RFC 6455) : aucune dépendance externe, un thread par connexion. */
@OptIn(ExperimentalEncodingApi::class)
class ControlServer(private val api: ControlApi, private val socketFactory: () -> ServerSocket) {
    private var server: ServerSocket? = null
    private val pool = Executors.newCachedThreadPool()
    private val clients = CopyOnWriteArrayList<WsClient>()
    @Volatile private var running = false

    val port: Int get() = server?.localPort ?: 0

    fun start() {
        val s = socketFactory(); server = s; running = true
        pool.execute {
            while (running) {
                val c = try { s.accept() } catch (e: Exception) { break }
                pool.execute { serve(c) }
            }
        }
    }

    fun stop() {
        running = false
        runCatching { server?.close() }
        clients.forEach { it.close() }; clients.clear()
        pool.shutdownNow()
    }

    /** Diffuse un message texte JSON à tous les contrôleurs connectés en WebSocket. */
    fun broadcast(text: String) { clients.forEach { if (!it.send(text)) clients.remove(it) } }

    fun disconnect(deviceId: String) { clients.filter { it.deviceId == deviceId }.forEach { it.close(); clients.remove(it) } }

    private fun serve(sock: Socket) {
        try {
            sock.soTimeout = 15_000
            if (sock is SSLSocket) sock.startHandshake()
            val input = sock.getInputStream().buffered()
            val out = sock.getOutputStream()
            val req = readRequest(input, sock.inetAddress) ?: return
            if (req.path == "/api/v1/ws" && req.headers["upgrade"].equals("websocket", true)) { websocket(sock, input, out, req); return }
            val r = api.handle(req)
            val body = r.body.toByteArray()
            out.write("HTTP/1.1 ${r.status} ${reason(r.status)}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: ${body.size}\r\nConnection: close\r\n\r\n".toByteArray())
            out.write(body); out.flush()
        } catch (_: Exception) {
        } finally { runCatching { sock.close() } }
    }

    private fun readLine(i: InputStream): String? {
        val sb = StringBuilder()
        while (sb.length < 8192) {
            val c = i.read(); if (c < 0) return null
            if (c == '\n'.code) return sb.toString().trimEnd('\r')
            sb.append(c.toChar())
        }
        return null
    }

    private fun readRequest(i: InputStream, remote: InetAddress): HttpRequest? {
        val line = readLine(i) ?: return null
        val parts = line.split(' ')
        if (parts.size < 2) return null
        val headers = HashMap<String, String>()
        while (true) {
            val h = readLine(i) ?: return null
            if (h.isEmpty()) break
            val k = h.indexOf(':'); if (k > 0) headers[h.substring(0, k).trim().lowercase()] = h.substring(k + 1).trim()
            if (headers.size > 50) return null
        }
        val len = headers["content-length"]?.toIntOrNull() ?: 0
        if (len < 0 || len > 16_000_000) return null
        val bytes = ByteArray(len)
        var n = 0
        while (n < len) { val r = i.read(bytes, n, len - n); if (r < 0) return null; n += r }
        val target = parts[1]
        val query = target.substringAfter('?', "").split('&').filter { it.contains('=') }
            .associate { URLDecoder.decode(it.substringBefore('='), "UTF-8") to URLDecoder.decode(it.substringAfter('='), "UTF-8") }
        return HttpRequest(parts[0], target.substringBefore('?'), query, headers, bytes.toString(Charsets.UTF_8), remote)
    }

    private fun reason(code: Int) = when (code) {
        200 -> "OK"; 400 -> "Bad Request"; 401 -> "Unauthorized"; 403 -> "Forbidden"; 404 -> "Not Found"
        409 -> "Conflict"; 422 -> "Unprocessable Entity"; 429 -> "Too Many Requests"; else -> "Error"
    }

    // ------------------------------------------------------------------ WebSocket

    private fun websocket(sock: Socket, input: InputStream, out: OutputStream, req: HttpRequest) {
        val dev = if (ControlApi.isPrivate(req.remote)) api.authenticate(req) else null
        val key = req.headers["sec-websocket-key"]
        if (dev == null || key == null) { out.write("HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\n\r\n".toByteArray()); return }
        val accept = Base64.Default.encode(MessageDigest.getInstance("SHA-1").digest((key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").toByteArray()))
        out.write("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: $accept\r\n\r\n".toByteArray()); out.flush()
        sock.soTimeout = 0
        val client = WsClient(sock, out, dev.id)
        clients.add(client)
        val state = api.handle(HttpRequest("GET", "/api/v1/state", emptyMap(), req.headers, "", req.remote)).body
        client.send("""{"type":"state","data":$state}""")
        try {
            // Lecture : seuls ping et close sont traités, les commandes passent par POST /command.
            while (true) {
                val b0 = input.read(); val b1 = input.read(); if (b0 < 0 || b1 < 0) break
                val opcode = b0 and 0x0F
                var len = (b1 and 0x7F).toLong()
                if (len == 126L) len = ((input.read() shl 8) or input.read()).toLong()
                else if (len == 127L) { len = 0; repeat(8) { len = (len shl 8) or input.read().toLong() } }
                if (len > 65_536) break
                val mask = if (b1 and 0x80 != 0) ByteArray(4).also { m -> for (k in 0..3) m[k] = input.read().toByte() } else null
                val payload = ByteArray(len.toInt()); var n = 0
                while (n < payload.size) { val r = input.read(payload, n, payload.size - n); if (r < 0) throw java.io.EOFException(); n += r }
                if (mask != null) for (k in payload.indices) payload[k] = (payload[k].toInt() xor mask[k % 4].toInt()).toByte()
                when (opcode) { 0x8 -> break; 0x9 -> client.frame(0xA, payload) }
            }
        } catch (_: Exception) {
        } finally { clients.remove(client); client.close() }
    }

    private class WsClient(val sock: Socket, val out: OutputStream, val deviceId: String) {
        fun send(text: String): Boolean = frame(0x1, text.toByteArray())

        fun frame(opcode: Int, p: ByteArray): Boolean = synchronized(this) {
            try {
                out.write(0x80 or opcode)
                when {
                    p.size < 126 -> out.write(p.size)
                    p.size < 65_536 -> { out.write(126); out.write(p.size shr 8); out.write(p.size and 0xFF) }
                    else -> { out.write(127); for (s in 56 downTo 0 step 8) out.write((p.size.toLong() shr s).toInt() and 0xFF) }
                }
                out.write(p); out.flush(); true
            } catch (_: Exception) { false }
        }

        fun close() { runCatching { sock.close() } }
    }
}
