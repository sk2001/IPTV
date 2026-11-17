package app.iptvplayer.remote

import android.app.Application
import android.app.UiModeManager
import android.content.Context
import android.content.res.Configuration
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.os.Build
import app.iptvplayer.AppViewModel
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.io.File
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.util.UUID
import javax.net.ssl.SSLServerSocket

@Serializable
private data class RemoteSettings(val enabled: Boolean = false, val deviceId: String = "", val deviceName: String = "", val syncAccounts: Boolean = false)

/**
 * Cette appli comme cible : serveur de contrôle HTTPS (port 47231), annonce mDNS, appairage par PIN.
 * Désactivé par défaut. Actif tant que le processus de l'appli vit (pas de service d'arrière-plan pour l'instant).
 */
class RemoteController(private val app: Application, private val vm: AppViewModel, private val scope: CoroutineScope) {
    private val json = Json { ignoreUnknownKeys = true }
    private val settingsFile = File(app.filesDir, "remote.json")
    private var settings = runCatching { json.decodeFromString<RemoteSettings>(settingsFile.readText()) }.getOrDefault(RemoteSettings())
        .let { if (it.deviceId.isEmpty()) it.copy(deviceId = UUID.randomUUID().toString(), deviceName = Build.MODEL ?: "Android") else it }
    private val registry = DeviceRegistry(File(app.filesDir, "remote-devices.json"))
    private val pairing = PairingManager(registry)

    private val _enabled = MutableStateFlow(settings.enabled)
    val enabled: StateFlow<Boolean> = _enabled.asStateFlow()
    private val _pin = MutableStateFlow<String?>(null)
    /** PIN à afficher sur cet appareil pendant un appairage. */
    val pin: StateFlow<String?> = _pin.asStateFlow()
    private val _devices = MutableStateFlow(registry.all())
    val devices: StateFlow<List<PairedDevice>> = _devices.asStateFlow()
    private val _port = MutableStateFlow(0)
    val port: StateFlow<Int> = _port.asStateFlow()
    private val _error = MutableStateFlow<String?>(null)
    val error: StateFlow<String?> = _error.asStateFlow()
    val deviceName: String get() = settings.deviceName
    private val _syncAccounts = MutableStateFlow(settings.syncAccounts)
    /** Synchroniser aussi les comptes (mots de passe compris) : doit etre active sur les deux appareils. */
    val syncAccounts: StateFlow<Boolean> = _syncAccounts.asStateFlow()
    fun setSyncAccounts(on: Boolean) { _syncAccounts.value = on; save() }
    val deviceId: String get() = settings.deviceId

    /** Offre en cours : lien de QR code + adresse + PIN affichables a l'ecran. */
    data class OfferInfo(val link: String, val host: String, val port: Int, val pin: String)
    private val _offer = MutableStateFlow<OfferInfo?>(null)
    val offer: StateFlow<OfferInfo?> = _offer.asStateFlow()
    private var tlsFingerprint = ""

    /** Active le controle si besoin et ouvre une offre de 5 minutes (PIN a usage unique dans le QR code). */
    fun openOffer(): Boolean {
        if (!_enabled.value) setEnabled(true)
        if (server == null) return false
        val ip = PairUri.localIpv4() ?: return false
        val pin = pairing.openOffer()
        val platform = if (isTv()) "androidtv" else "android"
        _offer.value = OfferInfo(PairUri.build(ip, _port.value, settings.deviceId, settings.deviceName, pin, tlsFingerprint, platform), ip, _port.value, pin)
        return true
    }

    fun closeOffer() { pairing.closeOffer(); _offer.value = null }

    private var server: ControlServer? = null
    private var nsd: NsdManager? = null
    private var nsdListener: NsdManager.RegistrationListener? = null
    private var jobs = emptyList<Job>()

    init {
        pairing.onChange = { refresh() }
        if (settings.enabled) start()
    }

    private fun refresh() {
        _pin.value = pairing.currentPin; _devices.value = registry.all()
        if (_offer.value != null && !pairing.offerActive) _offer.value = null      // utilisee ou expiree
    }

    private fun save() { runCatching { settingsFile.writeText(json.encodeToString(settings.copy(enabled = _enabled.value, syncAccounts = _syncAccounts.value))) } }

    fun setEnabled(on: Boolean) {
        if (on == _enabled.value) return
        _enabled.value = on; save()
        if (on) start() else stop()
    }

    fun revoke(id: String) { registry.remove(id); server?.disconnect(id); refresh() }
    fun cancelPairing() = pairing.cancel()

    private fun isTv(): Boolean = (app.getSystemService(Context.UI_MODE_SERVICE) as? UiModeManager)?.currentModeType == Configuration.UI_MODE_TYPE_TELEVISION

    private fun start() {
        if (server != null) return
        try {
            val tls = DeviceCert.load()
            tlsFingerprint = tls.fingerprint
            val platform = if (isTv()) "androidtv" else "android"
            val target = RemoteTarget(vm, settings.deviceId, settings.deviceName, platform, "0.1.0", { id -> _syncAccounts.value || pairing.accountsAllowed(id) })
            val api = ControlApi(target, registry, pairing, { tls.fingerprint }, { _enabled.value })
            val factory = {
                val s = tls.context.serverSocketFactory.createServerSocket() as SSLServerSocket
                s.reuseAddress = true
                try { s.bind(InetSocketAddress(PORT)) } catch (_: Exception) { s.bind(InetSocketAddress(0)) }
                s
            }
            val srv = ControlServer(api, factory).also { it.start() }
            server = srv; _port.value = srv.port; _error.value = null
            registerNsd(srv.port, platform, tls.fingerprint)

            var last = 0L; var lastKey: Any? = null
            jobs = listOf(
                // État diffusé à chaque changement, la position au plus une fois par seconde.
                scope.launch {
                    vm.player.state.collect { ps ->
                        val key = ps.copy(positionMs = 0, toast = null)
                        val now = System.currentTimeMillis()
                        if (key != lastKey || now - last >= 1000) {
                            lastKey = key; last = now
                            srv.broadcast("""{"type":"state","data":${playerStateJson(ps)}}""")
                        }
                    }
                },
                // Expiration du PIN et dernière utilisation des appareils.
                scope.launch { while (true) { delay(1000); refresh() } },
            )
        } catch (e: Exception) {
            _error.value = e.message ?: e.javaClass.simpleName
            stop(keepEnabled = true)
        }
    }

    private fun registerNsd(port: Int, platform: String, fp: String) {
        val mgr = app.getSystemService(Context.NSD_SERVICE) as? NsdManager ?: return
        val info = NsdServiceInfo().apply {
            serviceName = settings.deviceName; serviceType = "_iptvplayer._tcp"; setPort(port)
            setAttribute("v", PROTOCOL.toString()); setAttribute("id", settings.deviceId); setAttribute("name", settings.deviceName)
            setAttribute("platform", platform); setAttribute("fp", fp.take(16))
        }
        val l = object : NsdManager.RegistrationListener {
            override fun onServiceRegistered(i: NsdServiceInfo) {}
            override fun onRegistrationFailed(i: NsdServiceInfo, code: Int) {}
            override fun onServiceUnregistered(i: NsdServiceInfo) {}
            override fun onUnregistrationFailed(i: NsdServiceInfo, code: Int) {}
        }
        runCatching { mgr.registerService(info, NsdManager.PROTOCOL_DNS_SD, l); nsd = mgr; nsdListener = l }
    }

    private fun stop(keepEnabled: Boolean = false) {
        jobs.forEach { it.cancel() }; jobs = emptyList()
        nsdListener?.let { l -> runCatching { nsd?.unregisterService(l) } }; nsdListener = null; nsd = null
        server?.stop(); server = null
        _port.value = 0; pairing.cancel()
        if (!keepEnabled) _error.value = null
    }

    companion object { const val PORT = 47231 }
}
