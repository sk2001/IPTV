package app.iptvplayer.ui

import android.app.Activity
import android.content.pm.ActivityInfo
import android.view.WindowManager
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.AudioFile
import androidx.compose.material.icons.filled.Cast
import androidx.compose.material.icons.filled.ClosedCaption
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.foundation.focusable
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import app.iptvplayer.AppViewModel
import app.iptvplayer.R
import app.iptvplayer.model.Kind
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import org.videolan.libvlc.util.VLCVideoLayout

/** Lecteur plein écran : vidéo libVLC + commandes qui se masquent seules. */
@Composable
fun PlayerScreen(vm: AppViewModel) {
    val ps by vm.player.state.collectAsState()
    val req = ps.request ?: return
    val activity = LocalContext.current as Activity
    var controls by remember { mutableStateOf(true) }
    var touch by remember { mutableLongStateOf(0L) }
    var dragging by remember { mutableStateOf<Float?>(null) }
    var sendTo by remember { mutableStateOf(false) }
    val scope = androidx.compose.runtime.rememberCoroutineScope()

    // Paysage, barres système masquées, écran allumé pendant la lecture.
    DisposableEffect(Unit) {
        val previous = activity.requestedOrientation
        activity.requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
        val ctrl = WindowCompat.getInsetsController(activity.window, activity.window.decorView)
        ctrl.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        ctrl.hide(WindowInsetsCompat.Type.systemBars())
        activity.window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        onDispose {
            activity.requestedOrientation = previous
            ctrl.show(WindowInsetsCompat.Type.systemBars())
            activity.window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
    }

    BackHandler { vm.closePlayer() }

    LaunchedEffect(controls, touch, ps.playing) {
        if (controls && ps.playing) { delay(3500); controls = false }
    }
    LaunchedEffect(ps.toast) {
        if (ps.toast != null) { delay(2500); vm.player.consumeToast() }
    }

    // Télécommande (D-pad / touches média) : la surface du lecteur garde le focus clavier.
    val focus = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { focus.requestFocus() } }

    Box(
        Modifier.fillMaxSize().background(Color.Black)
            .focusRequester(focus).focusable()
            .onPreviewKeyEvent { e ->
                if (e.type != KeyEventType.KeyDown) return@onPreviewKeyEvent false
                val live = req.kind == Kind.LIVE
                fun seek(ms: Long) { if (ps.seekable) vm.player.seekTo((ps.positionMs + ms).coerceIn(0, maxOf(0, ps.lengthMs))) }
                touch = System.nanoTime()
                when (e.key) {
                    Key.DirectionCenter, Key.Enter, Key.NumPadEnter, Key.MediaPlayPause, Key.Spacebar -> { controls = true; vm.player.togglePause(); true }
                    Key.MediaPlay, Key.MediaPause -> { vm.player.togglePause(); true }
                    Key.DirectionLeft, Key.MediaRewind -> { if (live) false else { seek(-10_000); controls = true; true } }
                    Key.DirectionRight, Key.MediaFastForward -> { if (live) false else { seek(10_000); controls = true; true } }
                    Key.DirectionUp, Key.ChannelUp, Key.MediaNext -> { if (live) { vm.zap(-1); true } else false }
                    Key.DirectionDown, Key.ChannelDown, Key.MediaPrevious -> { if (live) { vm.zap(1); true } else false }
                    Key.Menu, Key.Info -> { controls = !controls; true }
                    else -> false
                }
            }
            .pointerInput(Unit) { detectTapGestures { controls = !controls; touch = System.nanoTime() } },
    ) {
        AndroidView(
            factory = { ctx -> VLCVideoLayout(ctx).also { vm.player.attach(it) } },
            modifier = Modifier.fillMaxSize(),
            onRelease = { vm.player.detach() },
        )

        // Mise en mémoire tampon / erreur
        when {
            ps.error -> Text(stringResource(R.string.player_error), color = Color.White, fontSize = 16.sp,
                modifier = Modifier.align(Alignment.Center).background(Color(0x99000000), RoundedCornerShape(10.dp)).padding(16.dp))
            ps.bufferingPercent < 100 && !ps.playing -> Column(Modifier.align(Alignment.Center), horizontalAlignment = Alignment.CenterHorizontally) {
                CircularProgressIndicator(color = Color.White)
                Text(stringResource(R.string.player_buffering, ps.bufferingPercent), color = Color.White, fontSize = 13.sp, modifier = Modifier.padding(top = 8.dp))
            }
        }

        // Message bref (reprise, piste audio…)
        ps.toast?.let { t ->
            val text = when {
                t.startsWith("resume:") -> stringResource(R.string.toast_resume, formatTime(t.removePrefix("resume:").toLongOrNull() ?: 0))
                t.startsWith("audio:") -> stringResource(R.string.toast_audio, t.removePrefix("audio:"))
                else -> t
            }
            Text(text, color = Color.White, fontSize = 14.sp,
                modifier = Modifier.align(Alignment.TopCenter).padding(top = 64.dp).background(Color(0xB3000000), RoundedCornerShape(16.dp)).padding(horizontal = 14.dp, vertical = 6.dp))
        }

        if (controls) {
            // Barre du haut : retour + titre
            Row(
                Modifier.align(Alignment.TopStart).fillMaxWidth()
                    .background(Brush.verticalGradient(listOf(Color(0xCC000000), Color.Transparent)))
                    .padding(horizontal = 8.dp, vertical = 12.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                IconButton(onClick = { vm.closePlayer() }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, stringResource(R.string.back), tint = Color.White) }
                Text(req.title, color = Color.White, fontSize = 17.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                IconButton(onClick = { sendTo = true; touch = System.nanoTime() }) { Icon(Icons.Filled.Cast, stringResource(R.string.send_to), tint = Color.White) }
            }

            // Barre du bas : progression + commandes
            Column(
                Modifier.align(Alignment.BottomStart).fillMaxWidth()
                    .background(Brush.verticalGradient(listOf(Color.Transparent, Color(0xD9000000))))
                    .padding(horizontal = 16.dp, vertical = 8.dp),
            ) {
                if (ps.seekable && ps.lengthMs > 0) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(formatTime(((dragging ?: ps.positionMs.toFloat())).toLong()), color = Color.White, fontSize = 12.sp)
                        Slider(
                            value = (dragging ?: ps.positionMs.toFloat()).coerceIn(0f, ps.lengthMs.toFloat()),
                            valueRange = 0f..ps.lengthMs.toFloat(),
                            onValueChange = { dragging = it; touch = System.nanoTime() },
                            onValueChangeFinished = { dragging?.let { vm.player.seekTo(it.toLong()) }; dragging = null },
                            colors = SliderDefaults.colors(thumbColor = Color.White, activeTrackColor = Color(0xFF4C8DFF), inactiveTrackColor = Color(0x55FFFFFF)),
                            modifier = Modifier.weight(1f).padding(horizontal = 8.dp),
                        )
                        Text(formatTime(ps.lengthMs), color = Color.White, fontSize = 12.sp)
                    }
                }
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                    IconButton(onClick = { vm.player.togglePause(); touch = System.nanoTime() }) {
                        Icon(if (ps.playing) Icons.Filled.Pause else Icons.Filled.PlayArrow,
                            stringResource(if (ps.playing) R.string.pause else R.string.play), tint = Color.White)
                    }
                    Row {
                        IconButton(onClick = { vm.player.cycleAudio(); touch = System.nanoTime() }) { Icon(Icons.Filled.AudioFile, stringResource(R.string.audio_track), tint = Color.White) }
                        if (req.kind != Kind.LIVE || ps.subtitleTracks.isNotEmpty()) {
                            IconButton(onClick = { vm.player.cycleSubtitles(); touch = System.nanoTime() }) { Icon(Icons.Filled.ClosedCaption, stringResource(R.string.subtitles), tint = Color.White) }
                        }
                    }
                }
            }
        }
    }

    if (sendTo) {
        val known by vm.controller.known.collectAsState()
        androidx.compose.material3.AlertDialog(
            onDismissRequest = { sendTo = false },
            containerColor = Palette.Surface2,
            title = { Text(stringResource(R.string.send_to), color = Palette.Text, fontSize = 17.sp) },
            text = {
                Column {
                    if (known.isEmpty()) Text(stringResource(R.string.ctrl_none_paired), color = Palette.Muted, fontSize = 14.sp)
                    known.forEach { k ->
                        TextButton(onClick = { sendTo = false; scope.launch { vm.controller.sendCurrentTo(k) } }) { Text(k.name, color = Palette.Text) }
                    }
                }
            },
            confirmButton = {},
            dismissButton = { TextButton(onClick = { sendTo = false }) { Text(stringResource(R.string.btn_cancel), color = Palette.Muted) } },
        )
    }
}
