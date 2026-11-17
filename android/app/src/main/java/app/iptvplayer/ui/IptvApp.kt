package app.iptvplayer.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Star
import androidx.compose.material.icons.filled.Tv
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import app.iptvplayer.AppViewModel
import app.iptvplayer.R
import app.iptvplayer.model.Account
import app.iptvplayer.model.AccountType
import app.iptvplayer.model.Kind

enum class Tab { LIVE, MOVIES, SERIES, SETTINGS }

fun newAccount() = Account(id = "", name = "", type = AccountType.XTREAM, url = "")

/** Racine de l'interface : premier lancement (compte), navigation par onglets, lecteur plein écran en surimpression. */
@Composable
fun IptvApp(vm: AppViewModel) {
    val accounts by vm.accounts.collectAsState()
    val player by vm.player.state.collectAsState()
    val episodes by vm.episodes.collectAsState()
    var tab by rememberSaveable { mutableStateOf(Tab.LIVE) }
    var editing by remember { mutableStateOf<Account?>(null) }
    val tv = isTelevision(androidx.compose.ui.platform.LocalContext.current)

    LaunchedEffect(tab) {
        when (tab) {
            Tab.LIVE -> vm.setKind(Kind.LIVE)
            Tab.MOVIES -> vm.setKind(Kind.MOVIE)
            Tab.SERIES -> vm.setKind(Kind.SERIES)
            Tab.SETTINGS -> Unit
        }
    }

    Box(Modifier.fillMaxSize().background(Palette.Bg)) {
        if (tv && accounts.isEmpty() && editing == null) {
            TvSetupScreen(vm, onManual = { editing = newAccount() })
        } else if (accounts.isEmpty() || editing != null) {
            AccountEditorScreen(
                initial = editing ?: newAccount(),
                canCancel = accounts.isNotEmpty(),
                vm = vm,
                onDone = { editing = null },
            )
        } else if (tv) {
            TvShell(vm, tab, onTab = { tab = it }, onEditAccount = { editing = it }, onAddAccount = { editing = newAccount() })
        } else {
            Scaffold(
                containerColor = Palette.Bg,
                bottomBar = {
                    NavigationBar(containerColor = Palette.Surface) {
                        val colors = NavigationBarItemDefaults.colors(
                            selectedIconColor = Palette.Text, selectedTextColor = Palette.Text,
                            indicatorColor = Palette.AccentSoft, unselectedIconColor = Palette.Muted, unselectedTextColor = Palette.Muted,
                        )
                        NavigationBarItem(tab == Tab.LIVE, { tab = Tab.LIVE }, { Icon(Icons.Filled.Tv, null) }, label = { Text(stringResource(R.string.nav_live)) }, colors = colors)
                        NavigationBarItem(tab == Tab.MOVIES, { tab = Tab.MOVIES }, { Icon(Icons.Filled.PlayArrow, null) }, label = { Text(stringResource(R.string.nav_movies)) }, colors = colors)
                        NavigationBarItem(tab == Tab.SERIES, { tab = Tab.SERIES }, { Icon(Icons.Filled.Star, null) }, label = { Text(stringResource(R.string.nav_series)) }, colors = colors)
                        NavigationBarItem(tab == Tab.SETTINGS, { tab = Tab.SETTINGS }, { Icon(Icons.Filled.Settings, null) }, label = { Text(stringResource(R.string.nav_settings)) }, colors = colors)
                    }
                },
            ) { padding ->
                Box(Modifier.padding(padding).fillMaxSize()) {
                    if (tab == Tab.SETTINGS) {
                        SettingsScreen(vm, onEditAccount = { editing = it }, onAddAccount = { editing = newAccount() })
                    } else {
                        BrowseScreen(vm, onManageAccounts = { tab = Tab.SETTINGS })
                    }
                }
            }
        }

        if (player.request != null) PlayerScreen(vm)
        episodes?.let { EpisodesSheet(vm, it) }
        val ctrlOpen by vm.controllerOpen.collectAsState()
        if (ctrlOpen && player.request == null) ControllerScreen(vm, onClose = { vm.controllerOpen.value = false })
    }
}
