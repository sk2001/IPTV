package app.iptvplayer.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.iptvplayer.AppViewModel
import app.iptvplayer.R
import app.iptvplayer.model.Account
import app.iptvplayer.model.AccountType
import kotlinx.coroutines.launch
import java.net.URI

@Composable
private fun fieldColors() = OutlinedTextFieldDefaults.colors(
    focusedTextColor = Palette.Text, unfocusedTextColor = Palette.Text,
    focusedContainerColor = Palette.Surface2, unfocusedContainerColor = Palette.Surface2,
    focusedBorderColor = Palette.Accent, unfocusedBorderColor = Palette.Line,
    focusedLabelColor = Palette.Accent, unfocusedLabelColor = Palette.Muted, cursorColor = Palette.Text,
)

/** Ajout / modification d'un compte (Xtream ou playlist M3U), avec test de connexion. */
@Composable
fun AccountEditorScreen(initial: Account, canCancel: Boolean, vm: AppViewModel, onDone: () -> Unit) {
    var name by remember { mutableStateOf(initial.name) }
    var type by remember { mutableStateOf(initial.type) }
    var url by remember { mutableStateOf(initial.url) }
    var user by remember { mutableStateOf(initial.username) }
    var pass by remember { mutableStateOf(initial.password) }
    var message by remember { mutableStateOf<String?>(null) }
    var isError by remember { mutableStateOf(false) }
    var busy by remember { mutableStateOf(false) }
    var confirmDelete by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    val errUrl = stringResource(R.string.err_url)
    val errCreds = stringResource(R.string.err_creds)
    val okText = stringResource(R.string.test_ok)
    val testing = stringResource(R.string.test_running)
    val defaultName = stringResource(R.string.default_account_name)

    /** Construit le compte depuis le formulaire (extrait les identifiants d'une URL get.php collée). */
    fun build(): Account? {
        var u = url.trim(); var us = user.trim(); var pw = pass
        if (u.isEmpty()) { message = errUrl; isError = true; return null }
        if (type == AccountType.XTREAM) {
            if (!u.startsWith("http", true)) u = "http://$u"
            runCatching {
                val uri = URI(u)
                val q = (uri.rawQuery ?: "").split('&').mapNotNull { it.split('=', limit = 2).takeIf { p -> p.size == 2 }?.let { p -> p[0] to java.net.URLDecoder.decode(p[1], "UTF-8") } }.toMap()
                if (us.isEmpty()) us = q["username"].orEmpty()
                if (pw.isEmpty()) pw = q["password"].orEmpty()
                u = "${uri.scheme}://${uri.authority}"
            }
            if (us.isEmpty() || pw.isEmpty()) { message = errCreds; isError = true; return null }
        }
        val label = name.trim().ifEmpty { runCatching { URI(u).host }.getOrNull() ?: defaultName }
        return initial.copy(name = label, type = type, url = u, username = us, password = pw)
    }

    Column(Modifier.fillMaxSize().systemBarsPadding().imePadding().verticalScroll(rememberScrollState()).padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text(stringResource(if (initial.id.isEmpty()) R.string.account_add_title else R.string.account_edit_title), color = Palette.Text, fontSize = 24.sp)
        Text(stringResource(R.string.account_intro), color = Palette.Muted, fontSize = 14.sp)
        if (!canCancel) OutlinedButton(onClick = { vm.controllerOpen.value = true }) { Text(stringResource(R.string.login_other_device)) }

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf(AccountType.XTREAM to R.string.type_xtream, AccountType.M3U to R.string.type_m3u).forEach { (t, label) ->
                FilterChip(
                    selected = type == t, onClick = { type = t }, label = { Text(stringResource(label)) },
                    colors = FilterChipDefaults.filterChipColors(containerColor = Palette.Surface2, labelColor = Palette.Text, selectedContainerColor = Palette.Accent, selectedLabelColor = Color.White),
                )
            }
        }
        OutlinedTextField(name, { name = it }, label = { Text(stringResource(R.string.field_name)) }, singleLine = true, shape = RoundedCornerShape(10.dp), colors = fieldColors(), modifier = Modifier.fillMaxWidth())
        OutlinedTextField(
            url, { url = it }, singleLine = true, shape = RoundedCornerShape(10.dp), colors = fieldColors(), modifier = Modifier.fillMaxWidth(),
            label = { Text(stringResource(if (type == AccountType.XTREAM) R.string.field_server else R.string.field_playlist)) },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
        )
        if (type == AccountType.XTREAM) {
            OutlinedTextField(user, { user = it }, label = { Text(stringResource(R.string.field_username)) }, singleLine = true, shape = RoundedCornerShape(10.dp), colors = fieldColors(), modifier = Modifier.fillMaxWidth())
            OutlinedTextField(
                pass, { pass = it }, label = { Text(stringResource(R.string.field_password)) }, singleLine = true, shape = RoundedCornerShape(10.dp), colors = fieldColors(),
                visualTransformation = PasswordVisualTransformation(), modifier = Modifier.fillMaxWidth(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
            )
        }

        message?.let { Text(it, color = if (isError) Color(0xFFF85149) else Palette.Text, fontSize = 14.sp) }

        Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
            OutlinedButton(enabled = !busy, onClick = {
                val a = build() ?: return@OutlinedButton
                busy = true; message = testing; isError = false
                scope.launch {
                    val err = vm.testAccount(a)
                    isError = err != null; message = err ?: okText; busy = false
                }
            }) { Text(stringResource(R.string.btn_test)) }

            Button(enabled = !busy, colors = ButtonDefaults.buttonColors(containerColor = Palette.Accent), onClick = {
                val a = build() ?: return@Button
                vm.saveAccount(a); onDone()
            }) { Text(stringResource(R.string.btn_save)) }

            if (canCancel) TextButton(onClick = onDone) { Text(stringResource(R.string.btn_cancel), color = Palette.Muted) }
            if (initial.id.isNotEmpty()) TextButton(onClick = { confirmDelete = true }) { Text(stringResource(R.string.btn_delete), color = Color(0xFFF85149)) }
        }
    }

    if (confirmDelete) {
        AlertDialog(
            onDismissRequest = { confirmDelete = false },
            containerColor = Palette.Surface2,
            title = { Text(stringResource(R.string.confirm_delete, initial.name), color = Palette.Text, fontSize = 17.sp) },
            confirmButton = { TextButton(onClick = { confirmDelete = false; vm.deleteAccount(initial.id); onDone() }) { Text(stringResource(R.string.btn_delete), color = Color(0xFFF85149)) } },
            dismissButton = { TextButton(onClick = { confirmDelete = false }) { Text(stringResource(R.string.btn_cancel), color = Palette.Muted) } },
        )
    }
}
