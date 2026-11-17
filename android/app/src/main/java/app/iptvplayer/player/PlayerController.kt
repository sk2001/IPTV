package app.iptvplayer.player

import android.app.Application
import android.net.Uri
import app.iptvplayer.data.Languages
import app.iptvplayer.data.UserData
import app.iptvplayer.model.Kind
import app.iptvplayer.model.PlayRequest
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import org.videolan.libvlc.LibVLC
import org.videolan.libvlc.Media
import org.videolan.libvlc.MediaPlayer
import org.videolan.libvlc.util.VLCVideoLayout

data class Track(val id: Int, val name: String)

data class PlayerState(
    val request: PlayRequest? = null,
    val playing: Boolean = false,
    val bufferingPercent: Int = 100,
    val positionMs: Long = 0,
    val lengthMs: Long = 0,
    val seekable: Boolean = false,
    val error: Boolean = false,
    val ended: Boolean = false,
    val audioTracks: List<Track> = emptyList(),
    val audioTrackId: Int = -1,
    val subtitleTracks: List<Track> = emptyList(),
    val subtitleTrackId: Int = -1,
    /** Message court (« Audio : English »…) affiché brièvement par l'interface. */
    val toast: String? = null,
    val volume: Int = 100,
    val muted: Boolean = false,
)

/**
 * Enveloppe de libVLC : même moteur que l'application Windows.
 * Langue préférée = option VLC (`:audio-language`) complétée par un choix de piste d'après son nom, comme sous Windows.
 */
class PlayerController(private val app: Application) {
    private var libVlc: LibVLC? = null
    private var player: MediaPlayer? = null
    private var layout: VLCVideoLayout? = null

    private val _state = MutableStateFlow(PlayerState())
    val state: StateFlow<PlayerState> = _state.asStateFlow()

    private var resumeAtMs = 0L
    private var prefs = UserData()
    private var trackPrefPending = false
    private var audioDone = false
    private var subDone = false

    private fun mediaPlayer(): MediaPlayer {
        player?.let { return it }
        val vlc = LibVLC(app, arrayListOf("--network-caching=1200", "--live-caching=1000", "--http-reconnect", "--no-video-title-show", "--no-snapshot-preview"))
        libVlc = vlc
        val mp = MediaPlayer(vlc)
        mp.setEventListener { e -> onEvent(mp, e) }
        layout?.let { mp.attachViews(it, null, true, false) }
        player = mp
        return mp
    }

    // ------------------------------------------------------------------ Vue vidéo

    fun attach(view: VLCVideoLayout) {
        val mp = player
        if (mp != null && layout != null && layout !== view) runCatching { mp.detachViews() }       // l'ancienne surface est liberee apres la nouvelle : on la detache d'abord
        layout = view
        if (mp == null) return
        try { mp.attachViews(view, null, true, false) }
        catch (e: IllegalStateException) { mp.detachViews(); mp.attachViews(view, null, true, false) }
        // Lecture deja en cours (passage plein ecran <-> mini-lecteur) : on relance la sortie video sur la nouvelle surface.
        if (_state.value.request != null) android.os.Handler(android.os.Looper.getMainLooper()).postDelayed({
            runCatching { mp.setVideoTrackEnabled(false); mp.setVideoTrackEnabled(true); mp.updateVideoSurfaces() }
        }, 400)
    }

    /** Detache la vue donnee, seulement si c'est encore la vue courante (mini-lecteur -> plein ecran : l'ancienne vue est liberee apres la nouvelle). */
    fun detach(view: VLCVideoLayout) {
        if (layout !== view) return
        player?.detachViews()
        layout = null
    }

    // ------------------------------------------------------------------ Commandes

    fun play(req: PlayRequest, userData: UserData) {
        prefs = userData
        val mp = mediaPlayer()
        val vlc = libVlc!!
        val live = req.kind == Kind.LIVE
        val media = Media(vlc, Uri.parse(req.url))
        Languages.vlcCodes(if (live) prefs.audioLangLive else prefs.audioLangVod)?.let { media.addOption(":audio-language=$it") }
        if (!live) Languages.vlcCodes(prefs.subLangVod)?.let { media.addOption(":sub-language=$it") }
        mp.media = media
        media.release()

        resumeAtMs = req.startMs
        trackPrefPending = true; audioDone = false; subDone = false
        _state.value = PlayerState(request = req, bufferingPercent = 0, volume = _state.value.volume, muted = _state.value.muted)
        mp.volume = if (_state.value.muted) 0 else _state.value.volume
        mp.play()
    }

    fun togglePause() {
        val mp = player ?: return
        if (mp.isPlaying) mp.pause() else mp.play()
    }

    fun seekTo(ms: Long) { player?.takeIf { it.isSeekable }?.setTime(ms.coerceAtLeast(0)) }
    fun seekBy(deltaMs: Long) { player?.let { seekTo(it.time + deltaMs) } }

    fun setVolume(level: Int) {
        val v = level.coerceIn(0, 100)
        player?.volume = v
        _state.update { it.copy(volume = v, muted = false) }
    }

    fun setMuted(m: Boolean) {
        val v = _state.value.volume
        player?.volume = if (m) 0 else v
        _state.update { it.copy(muted = m) }
    }

    fun setAudio(id: Int) { player?.let { it.setAudioTrack(id); refreshTracks(it) } }
    fun setSubtitle(id: Int) { player?.let { it.setSpuTrack(id); refreshTracks(it) } }   // -1 = désactivés

    fun cycleAudio() = cycle(audio = true)
    fun cycleSubtitles() = cycle(audio = false)

    private fun cycle(audio: Boolean) {
        val mp = player ?: return
        val tracks = if (audio) _state.value.audioTracks else _state.value.subtitleTracks
        if (tracks.isEmpty()) { toast(null); return }
        val cur = if (audio) mp.audioTrack else mp.spuTrack
        val idx = tracks.indexOfFirst { it.id == cur }
        val next = tracks[(idx + 1) % tracks.size]
        if (audio) mp.setAudioTrack(next.id) else mp.setSpuTrack(next.id)
        refreshTracks(mp)
    }

    /** Arrête la lecture ; la dernière position est conservée dans [state] pour l'historique. */
    fun stop() {
        player?.stop()
        _state.update { it.copy(playing = false) }
    }

    fun clearRequest() { _state.value = PlayerState(volume = _state.value.volume, muted = _state.value.muted) }

    fun consumeToast() { _state.update { it.copy(toast = null) } }
    private fun toast(msg: String?) { _state.update { it.copy(toast = msg) } }

    fun release() {
        player?.release(); libVlc?.release()
        player = null; libVlc = null
    }

    // ------------------------------------------------------------------ Événements

    private fun onEvent(mp: MediaPlayer, e: MediaPlayer.Event) {
        when (e.type) {
            MediaPlayer.Event.Playing -> _state.update { it.copy(playing = true, error = false, ended = false) }
            MediaPlayer.Event.Paused -> _state.update { it.copy(playing = false) }
            MediaPlayer.Event.Stopped -> _state.update { it.copy(playing = false) }
            MediaPlayer.Event.EndReached -> _state.update { it.copy(playing = false, ended = true, positionMs = it.lengthMs) }
            MediaPlayer.Event.EncounteredError -> _state.update { it.copy(playing = false, error = true) }
            MediaPlayer.Event.Buffering -> _state.update { it.copy(bufferingPercent = e.buffering.toInt()) }
            MediaPlayer.Event.SeekableChanged -> { _state.update { it.copy(seekable = e.seekable) }; resumeIfNeeded(mp) }
            MediaPlayer.Event.LengthChanged -> { _state.update { it.copy(lengthMs = e.lengthChanged) }; resumeIfNeeded(mp) }
            MediaPlayer.Event.TimeChanged -> {
                resumeIfNeeded(mp)
                _state.update { it.copy(positionMs = e.timeChanged) }
                applyTrackPrefs(mp, e.timeChanged)
            }
            MediaPlayer.Event.ESAdded -> refreshTracks(mp)
        }
    }

    /** Reprend à la position mémorisée dès que le flux est « seekable » (réessayé tant que ce n'est pas le cas). */
    private fun resumeIfNeeded(mp: MediaPlayer) {
        if (resumeAtMs <= 0 || !mp.isSeekable || mp.length <= 0) return
        val t = resumeAtMs
        resumeAtMs = 0
        mp.setTime(t)
        toast("resume:$t")
    }

    private fun refreshTracks(mp: MediaPlayer) {
        val audio = mp.audioTracks.orEmpty().filter { it.id >= 0 }.map { Track(it.id, it.name.orEmpty()) }
        val subs = mp.spuTracks.orEmpty().filter { it.id >= 0 }.map { Track(it.id, it.name.orEmpty()) }
        _state.update { it.copy(audioTracks = audio, audioTrackId = mp.audioTrack, subtitleTracks = subs, subtitleTrackId = mp.spuTrack) }
    }

    /**
     * Complète l'option VLC : certains fournisseurs ne renseignent pas la langue des pistes, mais leur nom
     * (« English - [English] ») la contient. Essayé pendant les premières secondes (les sous-titres arrivent parfois plus tard).
     */
    private fun applyTrackPrefs(mp: MediaPlayer, timeMs: Long) {
        if (!trackPrefPending || timeMs < 1200) return
        if (timeMs > 20_000) { trackPrefPending = false; return }
        val req = _state.value.request ?: return
        val live = req.kind == Kind.LIVE
        val audioLang = if (live) prefs.audioLangLive else prefs.audioLangVod
        val subLang = if (live) "" else prefs.subLangVod

        if (!audioDone) {
            val tracks = mp.audioTracks.orEmpty().filter { it.id >= 0 }
            if (audioLang.isEmpty()) audioDone = true
            else if (tracks.size >= 2) {
                audioDone = true
                val current = tracks.firstOrNull { it.id == mp.audioTrack }
                if (!Languages.nameMatches(audioLang, current?.name)) {
                    tracks.firstOrNull { Languages.nameMatches(audioLang, it.name) }?.let {
                        mp.setAudioTrack(it.id); toast("audio:${it.name}")
                    }
                }
            }
        }
        if (!subDone) {
            if (subLang.isEmpty()) subDone = true
            else mp.spuTracks.orEmpty().filter { it.id >= 0 }.firstOrNull { Languages.nameMatches(subLang, it.name) }?.let {
                subDone = true; mp.setSpuTrack(it.id)
            }
        }
        if (audioDone && subDone) trackPrefPending = false
        refreshTracks(mp)
    }
}
