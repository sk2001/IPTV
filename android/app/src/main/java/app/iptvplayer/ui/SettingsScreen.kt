package app.iptvplayer.ui

import androidx.appcompat.app.AppCompatDelegate
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.os.LocaleListCompat
import app.iptvplayer.AppViewModel
import app.iptvplayer.R
import app.iptvplayer.data.Languages
import app.iptvplayer.model.Account
import org.videolan.libvlc.LibVLC

private val Danger = Color(0xFFF85149)

@Composable
private fun Section(title: String, description: String? = null, content: @Composable () -> Unit) {
    Column(Modifier.padding(bottom = 22.dp)) {
        Text(title, color = Palette.Accent, fontSize = 11.sp)
        if (description != null) Text(description, color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.padding(top = 4.dp))
        Column(Modifier.padding(top = 10.dp)) { content() }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun Chips(options: List<Pair<String, String>>, selected: String, onSelect: (String) -> Unit) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        options.forEach { (code, label) ->
            FilterChip(
                selected = code == selected, onClick = { onSelect(code) }, label = { Text(label) },
                colors = FilterChipDefaults.filterChipColors(containerColor = Palette.Surface2, labelColor = Palette.Text,
                    selectedContainerColor = Palette.Accent, selectedLabelColor = Color.White),
            )
        }
    }
}

@Composable
fun SettingsScreen(vm: AppViewModel, onEditAccount: (Account) -> Unit, onAddAccount: () -> Unit) {
    val ud by vm.userData.data.collectAsState()
    val accounts by vm.accounts.collectAsState()
    var confirmClear by remember { mutableStateOf(false) }
    var showLicenses by remember { mutableStateOf(false) }
    val auto = stringResource(R.string.option_auto)
    val none = stringResource(R.string.option_none)
    val langOptions = Languages.all.map { it.code to it.label }
    val current = AppCompatDelegate.getApplicationLocales().toLanguageTags().take(2).ifEmpty { "en" }
    val context = LocalContext.current
    val vlcVersion = remember { runCatching { LibVLC.loadLibraries(); LibVLC.version() }.getOrDefault("—") }
    val appVersion = remember { runCatching { context.packageManager.getPackageInfo(context.packageName, 0).versionName }.getOrNull() ?: "" }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp, vertical = 16.dp)) {
        Text(stringResource(R.string.settings_title), color = Palette.Text, fontSize = 24.sp)
        Text(stringResource(R.string.settings_note), color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.padding(top = 2.dp, bottom = 20.dp))

        Section(stringResource(R.string.settings_language)) {
            Chips(listOf("en" to "English", "fr" to "Français"), current) { code ->
                AppCompatDelegate.setApplicationLocales(LocaleListCompat.forLanguageTags(code))   // appliqué immédiatement
            }
        }
        Section(stringResource(R.string.settings_audio_vod)) {
            Chips(listOf("" to auto) + langOptions, ud.audioLangVod) { c -> vm.userData.update { it.copy(audioLangVod = c) } }
        }
        Section(stringResource(R.string.settings_subs)) {
            Chips(listOf("" to none) + langOptions, ud.subLangVod) { c -> vm.userData.update { it.copy(subLangVod = c) } }
        }
        Section(stringResource(R.string.settings_audio_live)) {
            Chips(listOf("" to auto) + langOptions, ud.audioLangLive) { c -> vm.userData.update { it.copy(audioLangLive = c) } }
        }
        Section(stringResource(R.string.settings_resume)) {
            Chips(listOf("1" to stringResource(R.string.option_yes), "0" to stringResource(R.string.option_no)), if (ud.resumePlayback) "1" else "0") { c ->
                vm.userData.update { it.copy(resumePlayback = c == "1") }
            }
        }
        Section(stringResource(R.string.settings_history)) {
            OutlinedButton(onClick = { confirmClear = true }) { Text(stringResource(R.string.settings_clear_history), color = Danger) }
        }
        Section(stringResource(R.string.settings_accounts)) {
            accounts.forEach { a ->
                Row(Modifier.fillMaxWidth().clickable { onEditAccount(a) }.padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(a.name, color = Palette.Text, fontSize = 15.sp, modifier = Modifier.weight(1f))
                    Icon(Icons.Filled.Edit, null, tint = Palette.Muted)
                }
            }
            OutlinedButton(onClick = onAddAccount, modifier = Modifier.padding(top = 6.dp)) { Text(stringResource(R.string.account_add_title)) }
        }
        RemoteSection(vm)
        Section(stringResource(R.string.ctrl_section)) {
            OutlinedButton(onClick = { vm.controllerOpen.value = true }) { Text(stringResource(R.string.ctrl_open)) }
        }
        Section(stringResource(R.string.settings_about)) {
            Text(stringResource(R.string.settings_about_text, appVersion, vlcVersion), color = Palette.Muted, fontSize = 13.sp)
            OutlinedButton(onClick = { showLicenses = true }, modifier = Modifier.padding(top = 6.dp)) { Text(stringResource(R.string.settings_licenses)) }
        }
    }

    if (showLicenses) {
        val text = remember {
            listOf("THIRD_PARTY_NOTICES.md", "LGPL-2.1.txt").joinToString("\n\n----------------------------------------\n\n") { n ->
                runCatching { context.assets.open(n).bufferedReader().use { it.readText() } }.getOrDefault(n)
            }
        }
        AlertDialog(
            onDismissRequest = { showLicenses = false },
            containerColor = Palette.Surface2,
            title = { Text(stringResource(R.string.settings_licenses), color = Palette.Text, fontSize = 17.sp) },
            text = { Text(text, color = Palette.Muted, fontSize = 11.sp, modifier = Modifier.verticalScroll(rememberScrollState())) },
            confirmButton = { TextButton(onClick = { showLicenses = false }) { Text(stringResource(R.string.confirm), color = Palette.Accent) } },
        )
    }

    if (confirmClear) {
        AlertDialog(
            onDismissRequest = { confirmClear = false },
            containerColor = Palette.Surface2,
            title = { Text(stringResource(R.string.settings_history_confirm), color = Palette.Text, fontSize = 16.sp) },
            confirmButton = { TextButton(onClick = { confirmClear = false; vm.userData.clearHistory() }) { Text(stringResource(R.string.confirm), color = Danger) } },
            dismissButton = { TextButton(onClick = { confirmClear = false }) { Text(stringResource(R.string.btn_cancel), color = Palette.Muted) } },
        )
    }
}


@Composable
private fun RemoteSection(vm: AppViewModel) {
    val rc = vm.remote
    val on by rc.enabled.collectAsState()
    val pin by rc.pin.collectAsState()
    val devices by rc.devices.collectAsState()
    val port by rc.port.collectAsState()
    val error by rc.error.collectAsState()
    var showOffer by remember { mutableStateOf(false) }
    if (showOffer) OfferDialog(vm) { showOffer = false }
    Section(stringResource(R.string.settings_remote), stringResource(R.string.settings_remote_note)) {
        OutlinedButton(onClick = { showOffer = true }) { Text(stringResource(R.string.offer_show)) }
        Chips(
            listOf("on" to stringResource(R.string.remote_on), "off" to stringResource(R.string.remote_off)),
            if (on) "on" else "off",
        ) { rc.setEnabled(it == "on") }
        if (on) {
            Text(stringResource(R.string.remote_status, rc.deviceName, port), color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.padding(top = 6.dp))
            val auto by rc.autoSync.collectAsState()
            Text(stringResource(R.string.settings_autosync), color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.padding(top = 10.dp))
            Chips(listOf("on" to stringResource(R.string.remote_on), "off" to stringResource(R.string.remote_off)), if (auto) "on" else "off") { rc.setAutoSync(it == "on") }
            val syncAcc by rc.syncAccounts.collectAsState()
            Text(stringResource(R.string.settings_sync_accounts), color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.padding(top = 10.dp))
            Chips(listOf("on" to stringResource(R.string.remote_on), "off" to stringResource(R.string.remote_off)), if (syncAcc) "on" else "off") { rc.setSyncAccounts(it == "on") }
            error?.let { Text(it, color = Danger, fontSize = 13.sp) }
            if (port > 0) {
                val ip = remember(port) { app.iptvplayer.remote.PairUri.localIpv4() }
                if (ip != null) {
                    val link = remember(ip, port) { app.iptvplayer.remote.PairUri.build(ip, port, rc.deviceId, rc.deviceName) }
                    val qr = remember(link) { app.iptvplayer.remote.qrBitmap(link).asImageBitmap() }
                    Text(stringResource(R.string.remote_qr, ip, port), color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.padding(top = 8.dp))
                    androidx.compose.foundation.Image(qr, stringResource(R.string.remote_qr_desc), Modifier.padding(vertical = 6.dp).size(180.dp))
                }
            }
            pin?.let { Text(stringResource(R.string.remote_pin, it), color = Palette.Text, fontSize = 28.sp, modifier = Modifier.padding(vertical = 8.dp)) }
            devices.forEach { d ->
                Row(Modifier.fillMaxWidth().padding(vertical = 2.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("${d.name} (${d.platform})", color = Palette.Text, fontSize = 14.sp, modifier = Modifier.weight(1f))
                    TextButton(onClick = { rc.revoke(d.id) }) { Text(stringResource(R.string.remote_revoke), color = Danger) }
                }
            }
        }
    }
}

@Composable
private fun OfferDialog(vm: AppViewModel, onClose: () -> Unit) {
    AlertDialog(
        onDismissRequest = onClose,
        containerColor = Palette.Surface2,
        title = { Text(stringResource(R.string.offer_title), color = Palette.Text, fontSize = 17.sp) },
        text = { Column { Text(stringResource(R.string.offer_text), color = Palette.Muted, fontSize = 13.sp); OfferPanel(vm, 200.dp) } },
        confirmButton = { TextButton(onClick = onClose) { Text(stringResource(R.string.confirm), color = Palette.Accent) } },
    )
}
