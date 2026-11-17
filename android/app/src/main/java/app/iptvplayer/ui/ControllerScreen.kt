package app.iptvplayer.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.iptvplayer.AppViewModel
import app.iptvplayer.R
import app.iptvplayer.remote.FoundTarget
import app.iptvplayer.remote.KnownTarget
import kotlinx.coroutines.launch
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

private val Danger = Color(0xFFF85149)

/** Piloter un autre appareil : liste des appareils (appairés / détectés), puis télécommande. */
@Composable
fun ControllerScreen(vm: AppViewModel, onClose: () -> Unit) {
    val c = vm.controller
    val selected by c.selected.collectAsState()
    BackHandler { if (selected != null) c.disconnect() else onClose() }
    DisposableEffect(Unit) { c.startDiscovery(); onDispose { c.stopDiscovery(); c.disconnect() } }

    Column(Modifier.fillMaxSize().background(Palette.Bg).systemBarsPaddingCompat().verticalScroll(rememberScrollState()).padding(20.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = { if (selected != null) c.disconnect() else onClose() }) { Text("←  " + stringResource(R.string.back), color = Palette.Text) }
        }
        Text(stringResource(R.string.ctrl_title), color = Palette.Text, fontSize = 24.sp)
        val t = selected
        if (t == null) DeviceList(vm) else Remote(vm, t)
    }
}

private fun Modifier.systemBarsPaddingCompat() = this.then(Modifier.padding(top = 24.dp))

@Composable
private fun DeviceList(vm: AppViewModel) {
    val c = vm.controller
    val known by c.known.collectAsState()
    val found by c.found.collectAsState()
    val scope = rememberCoroutineScope()
    var pairing by remember { mutableStateOf<FoundTarget?>(null) }
    var pin by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }
    var linkTarget by remember { mutableStateOf<FoundTarget?>(null) }
    var manualCode by remember { mutableStateOf("") }
    var done by remember { mutableStateOf<String?>(null) }
    val wrongPin = stringResource(R.string.ctrl_wrong_pin)
    val badQr = stringResource(R.string.ctrl_bad_qr)
    val scanPrompt = stringResource(R.string.ctrl_scan_prompt)

    Text(stringResource(R.string.ctrl_paired), color = Palette.Accent, fontSize = 13.sp, modifier = Modifier.padding(top = 8.dp))
    if (known.isEmpty()) Text(stringResource(R.string.ctrl_none_paired), color = Palette.Muted, fontSize = 14.sp)
    known.forEach { k ->
        Row(Modifier.fillMaxWidth().background(Palette.Surface, RoundedCornerShape(10.dp)).clickable { c.connect(k) }.padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(k.name, color = Palette.Text, fontSize = 16.sp)
                Text("${k.platform} · ${k.host}", color = Palette.Muted, fontSize = 12.sp)
            }
            TextButton(onClick = { c.forget(k) }) { Text(stringResource(R.string.remote_revoke), color = Danger) }
        }
    }

    Text(stringResource(R.string.ctrl_nearby), color = Palette.Accent, fontSize = 13.sp, modifier = Modifier.padding(top = 12.dp))
    val unpaired = found.filter { f -> known.none { it.id == f.id } }
    if (unpaired.isEmpty()) Text(stringResource(R.string.ctrl_searching), color = Palette.Muted, fontSize = 14.sp)
    unpaired.forEach { f ->
        Row(Modifier.fillMaxWidth().background(Palette.Surface, RoundedCornerShape(10.dp)).clickable {
            error = null; pin = ""; busy = true
            scope.launch {
                val e = c.startPairing(f)
                busy = false
                if (e == null) pairing = f else error = e
            }
        }.padding(14.dp)) {
            Column(Modifier.weight(1f)) {
                Text(f.name, color = Palette.Text, fontSize = 16.sp)
                Text("${f.platform} · ${f.host}", color = Palette.Muted, fontSize = 12.sp)
            }
            Text(stringResource(R.string.ctrl_pair), color = Palette.Accent, fontSize = 14.sp)
        }
    }
    error?.let { Text(it, color = Danger, fontSize = 13.sp) }

    // Lecture du QR code affiché par l'autre appareil.
    val scan = androidx.activity.compose.rememberLauncherForActivityResult(com.journeyapps.barcodescanner.ScanContract()) { r ->
        val f = r.contents?.let { app.iptvplayer.remote.PairUri.parse(it) }
        if (r.contents != null && f == null) error = badQr
        if (f != null && f.pin != null) { error = null; done = null; linkTarget = f }
        else if (f != null) {
            error = null; pin = ""; busy = true
            scope.launch { val e = c.startPairing(f); busy = false; if (e == null) pairing = f else error = e }
        }
    }
    OutlinedButton(onClick = {
        scan.launch(com.journeyapps.barcodescanner.ScanOptions().setDesiredBarcodeFormats(com.journeyapps.barcodescanner.ScanOptions.QR_CODE).setPrompt(scanPrompt).setBeepEnabled(false).setOrientationLocked(false))
    }) { Text(stringResource(R.string.ctrl_scan)) }

    // Repli sans mDNS : saisie de l'adresse (hôte:port).
    var manual by remember { mutableStateOf("") }
    Text(stringResource(R.string.ctrl_manual), color = Palette.Accent, fontSize = 13.sp, modifier = Modifier.padding(top = 12.dp))
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
        OutlinedTextField(manual, { manual = it }, singleLine = true, placeholder = { Text("192.168.1.20:47231", color = Palette.Muted) }, modifier = Modifier.weight(1f))
        OutlinedButton(enabled = manual.isNotBlank() && !busy, onClick = {
            val host = manual.trim().substringBeforeLast(':', manual.trim())
            val port = manual.trim().substringAfterLast(':', "").toIntOrNull() ?: 47231
            val code = manualCode.takeIf { it.length == 6 }
            val f = FoundTarget("", host, host, port, "", code)
            error = null; pin = ""
            if (code != null) { done = null; linkTarget = f }     // code deja connu : connexion simplifiee
            else { busy = true; scope.launch { val e = c.startPairing(f); busy = false; if (e == null) pairing = f else error = e } }
        }) { Text(stringResource(R.string.ctrl_pair)) }
    }
    OutlinedTextField(manualCode, { if (it.length <= 6 && it.all(Char::isDigit)) manualCode = it }, singleLine = true,
        label = { Text(stringResource(R.string.ctrl_code_optional)) }, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword), modifier = Modifier.fillMaxWidth())
    done?.let { Text(it, color = Palette.Text, fontSize = 14.sp) }

    linkTarget?.let { f ->
        val okText = stringResource(R.string.ctrl_transfer_done)
        AlertDialog(
            onDismissRequest = { if (!busy) linkTarget = null },
            containerColor = Palette.Surface2,
            title = { Text(stringResource(R.string.ctrl_transfer_title, f.name), color = Palette.Text, fontSize = 17.sp) },
            text = { Text(stringResource(R.string.ctrl_transfer_text), color = Palette.Muted, fontSize = 14.sp) },
            confirmButton = {
                TextButton(enabled = !busy, onClick = {
                    busy = true
                    scope.launch {
                        val e = c.pairFromLink(f)
                        if (e != null) { error = e } else {
                            val k = c.known.value.firstOrNull { it.host == f.host && it.port == f.port }
                            val r = if (k == null) "Unknown device" else c.syncWith(k, forceAccounts = true)
                            if (r.startsWith("ok:")) {
                                val p = r.split(':'); done = String.format(okText, p[1], p[2])
                                if (k != null && k.platform == "windows") { c.linkBack(k); c.dropLocal(k.id) }      // le PC se lie a ce telephone puis referme son serveur
                                if (vm.accounts.value.isNotEmpty()) vm.controllerOpen.value = false
                            } else error = r
                        }
                        busy = false; linkTarget = null
                    }
                }) { Text(stringResource(R.string.ctrl_transfer_go), color = Palette.Accent) }
            },
            dismissButton = { TextButton(enabled = !busy, onClick = { linkTarget = null }) { Text(stringResource(R.string.btn_cancel), color = Palette.Muted) } },
        )
    }

    pairing?.let { f ->
        AlertDialog(
            onDismissRequest = { pairing = null },
            containerColor = Palette.Surface2,
            title = { Text(stringResource(R.string.ctrl_enter_pin, f.name), color = Palette.Text, fontSize = 17.sp) },
            text = {
                Column {
                    OutlinedTextField(pin, { if (it.length <= 6 && it.all(Char::isDigit)) pin = it }, singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword))
                    error?.let { Text(it, color = Danger, fontSize = 13.sp) }
                }
            },
            confirmButton = {
                TextButton(enabled = pin.length == 6 && !busy, onClick = {
                    busy = true
                    scope.launch {
                        val e = c.confirmPairing(pin)
                        busy = false
                        when (e) { null -> { pairing = null; error = null }; "pin" -> error = wrongPin; else -> { error = e; pairing = null } }
                    }
                }) { Text(stringResource(R.string.ctrl_pair), color = Palette.Accent) }
            },
            dismissButton = { TextButton(onClick = { pairing = null }) { Text(stringResource(R.string.btn_cancel), color = Palette.Muted) } },
        )
    }
}

@Composable
private fun Remote(vm: AppViewModel, t: KnownTarget) {
    val c = vm.controller
    val st by c.remoteState.collectAsState()
    val msg by c.message.collectAsState()
    val s: JsonObject? = st
    fun str(k: String) = s?.get(k)?.jsonPrimitive?.contentOrNull
    val title = s?.get("media")?.jsonObject?.get("title")?.jsonPrimitive?.contentOrNull
    val playing = str("state") == "playing"
    val live = s?.get("media")?.jsonObject?.get("kind")?.jsonPrimitive?.contentOrNull == "live"

    Text(t.name, color = Palette.Text, fontSize = 20.sp)
    Text(
        when { msg != null -> msg!!; s == null -> stringResource(R.string.ctrl_connecting); title == null -> stringResource(R.string.ctrl_idle); else -> "$title  ·  ${str("state")}" },
        color = if (msg != null) Danger else Palette.Muted, fontSize = 14.sp,
    )
    if (s != null && !live && title != null) {
        val pos = s["positionMs"]?.jsonPrimitive?.contentOrNull?.toLongOrNull() ?: 0
        val dur = s["durationMs"]?.jsonPrimitive?.contentOrNull?.toLongOrNull() ?: 0
        Text("${formatTime(pos)} / ${formatTime(dur)}", color = Palette.Muted, fontSize = 13.sp)
    }
    val btn = ButtonDefaults.buttonColors(containerColor = Palette.Surface2, contentColor = Palette.Text)
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(top = 8.dp)) {
        Button(colors = btn, onClick = { c.command("prev") }) { Text("⏮") }
        Button(colors = btn, onClick = { c.command("seekBy", "deltaMs" to -10_000) }) { Text("−10 s") }
        Button(colors = ButtonDefaults.buttonColors(containerColor = Palette.Accent), onClick = { c.command("toggle") }) { Text(if (playing) "⏸" else "▶") }
        Button(colors = btn, onClick = { c.command("seekBy", "deltaMs" to 10_000) }) { Text("+10 s") }
        Button(colors = btn, onClick = { c.command("next") }) { Text("⏭") }
    }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Button(colors = btn, onClick = { c.command("volumeBy", "delta" to -10) }) { Text("🔉 −") }
        Button(colors = btn, onClick = { c.command("volumeBy", "delta" to 10) }) { Text("🔊 +") }
        Button(colors = btn, onClick = { c.command("mute") }) { Text(stringResource(R.string.ctrl_mute)) }
        Button(colors = btn, onClick = { c.command("stop") }) { Text("⏹") }
    }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        OutlinedButton(onClick = { c.command("audioTrack", "id" to "next") }) { Text(stringResource(R.string.audio_track)) }
        OutlinedButton(onClick = { c.command("subtitleTrack", "id" to "next") }) { Text(stringResource(R.string.subtitles)) }
    }
    val statuses by c.syncStatus.collectAsState()
    statuses[t.id]?.let { st ->
        Text(if (st.error == null) stringResource(R.string.ctrl_autosync_ok, formatClock(st.at)) else stringResource(R.string.ctrl_autosync_err, st.error),
            color = if (st.error == null) Palette.Muted else Danger, fontSize = 13.sp)
    }
    var syncMsg by remember { mutableStateOf<String?>(null) }
    var syncing by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    val syncDone = stringResource(R.string.ctrl_sync_done)
    val syncSkipped = stringResource(R.string.ctrl_sync_skipped)
    OutlinedButton(enabled = !syncing, onClick = {
        syncing = true; syncMsg = null
        scope.launch {
            val r = c.syncWith(t)
            syncing = false
            syncMsg = if (r.startsWith("ok:")) {
                val p = r.split(':'); String.format(syncDone, p[1], p[2]) + if (r.endsWith(":skipped")) " " + syncSkipped else ""
            } else r
        }
    }) { Text(stringResource(R.string.ctrl_sync)) }
    syncMsg?.let { Text(it, color = Palette.Muted, fontSize = 13.sp) }
    s?.get("volume")?.jsonPrimitive?.intOrNull?.let { Text(stringResource(R.string.ctrl_volume, it), color = Palette.Muted, fontSize = 13.sp) }
}
