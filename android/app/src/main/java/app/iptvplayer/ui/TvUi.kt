package app.iptvplayer.ui

import android.app.UiModeManager
import android.content.Context
import android.content.res.Configuration
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items as gridItems
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Star
import androidx.compose.material.icons.filled.Tv
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.iptvplayer.ALL
import app.iptvplayer.AppViewModel
import app.iptvplayer.CategoryUi
import app.iptvplayer.FAV
import app.iptvplayer.HIST
import app.iptvplayer.R
import app.iptvplayer.Status
import app.iptvplayer.model.Kind
import app.iptvplayer.model.MediaItem
import coil3.compose.AsyncImage

/** Vrai sur un téléviseur / boîtier Android TV (interface pilotée à la télécommande). */
fun isTelevision(context: Context): Boolean {
    val ui = context.getSystemService(Context.UI_MODE_SERVICE) as? UiModeManager
    return ui?.currentModeType == Configuration.UI_MODE_TYPE_TELEVISION
}

/** Élément focalisable à la télécommande : liseré + léger agrandissement, clic et appui long. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun Modifier.tvFocusable(
    shape: androidx.compose.ui.graphics.Shape = RoundedCornerShape(10.dp),
    onLongClick: (() -> Unit)? = null,
    onFocus: (() -> Unit)? = null,
    onClick: () -> Unit,
): Modifier {
    var focused by remember { mutableStateOf(false) }
    val scale by animateFloatAsState(if (focused) 1.04f else 1f, label = "focusScale")
    return this
        .scale(scale)
        .border(if (focused) 3.dp else 0.dp, if (focused) Color.White else Color.Transparent, shape)
        .clip(shape)
        .onFocusChanged { focused = it.isFocused; if (it.isFocused) onFocus?.invoke() }
        .combinedClickable(onClick = onClick, onLongClick = onLongClick)
}

/** Racine de l'interface TV : barre latérale + contenu. */
@Composable
fun TvShell(vm: AppViewModel, tab: Tab, onTab: (Tab) -> Unit, onEditAccount: (app.iptvplayer.model.Account) -> Unit, onAddAccount: () -> Unit) {
    Row(Modifier.fillMaxSize()) {
        Column(
            Modifier.fillMaxHeight().width(190.dp).background(Palette.Surface).padding(horizontal = 12.dp, vertical = 24.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Text("IPTV Player", color = Palette.Text, fontSize = 18.sp, modifier = Modifier.padding(start = 8.dp, bottom = 16.dp))
            RailItem(Icons.Filled.Tv, R.string.nav_live, tab == Tab.LIVE) { onTab(Tab.LIVE) }
            RailItem(Icons.Filled.PlayArrow, R.string.nav_movies, tab == Tab.MOVIES) { onTab(Tab.MOVIES) }
            RailItem(Icons.Filled.Star, R.string.nav_series, tab == Tab.SERIES) { onTab(Tab.SERIES) }
            RailItem(Icons.Filled.Settings, R.string.nav_settings, tab == Tab.SETTINGS) { onTab(Tab.SETTINGS) }
        }
        Box(Modifier.weight(1f).fillMaxHeight()) {
            if (tab == Tab.SETTINGS) SettingsScreen(vm, onEditAccount = onEditAccount, onAddAccount = onAddAccount)
            else if (tab == Tab.LIVE) LiveTvScreen(vm, tv = true)
            else TvBrowse(vm, tab)
        }
    }
}

@Composable
private fun RailItem(icon: ImageVector, label: Int, selected: Boolean, onClick: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().height(48.dp)
            .background(if (selected) Palette.AccentSoft else Color.Transparent, RoundedCornerShape(10.dp))
            .tvFocusable(onClick = onClick).padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Icon(icon, null, tint = if (selected) Palette.Text else Palette.Muted, modifier = Modifier.size(22.dp))
        Text(stringResource(label), color = if (selected) Palette.Text else Palette.Muted, fontSize = 15.sp)
    }
}

@Composable
private fun TvBrowse(vm: AppViewModel, tab: Tab) {
    val items by vm.items.collectAsState()
    val categories by vm.categories.collectAsState()
    val filter by vm.filter.collectAsState()
    val ud by vm.userData.data.collectAsState()
    val status by vm.status.collectAsState()
    val account by vm.selectedAccount.collectAsState()
    val guide by vm.epg.collectAsState()

    Column(Modifier.fillMaxSize().padding(start = 20.dp, end = 20.dp, top = 16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Text(account?.name ?: stringResource(R.string.account_none), color = Palette.Muted, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
            OutlinedTextField(
                value = filter.query, onValueChange = vm::setQuery, singleLine = true,
                placeholder = { Text(stringResource(R.string.search_hint), color = Palette.Muted) },
                modifier = Modifier.width(320.dp), shape = RoundedCornerShape(12.dp),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                colors = OutlinedTextFieldDefaults.colors(
                    focusedTextColor = Palette.Text, unfocusedTextColor = Palette.Text,
                    focusedContainerColor = Palette.Surface2, unfocusedContainerColor = Palette.Surface2,
                    focusedBorderColor = Color.White, unfocusedBorderColor = Palette.Line, cursorColor = Palette.Text,
                ),
            )
            Box(Modifier.size(48.dp).tvFocusable(RoundedCornerShape(24.dp)) { vm.refresh() }, contentAlignment = Alignment.Center) {
                Icon(Icons.Filled.Refresh, stringResource(R.string.refresh), tint = Palette.Muted)
            }
        }

        LazyRow(Modifier.padding(vertical = 12.dp), contentPadding = PaddingValues(horizontal = 6.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            items(categories, key = { it.id }) { c ->
                val selected = filter.categoryId == c.id
                Box(
                    Modifier.background(if (selected) Palette.AccentSoft else Palette.Surface2, RoundedCornerShape(18.dp))
                        .tvFocusable(RoundedCornerShape(18.dp)) { vm.setCategory(c.id) }.padding(horizontal = 16.dp, vertical = 8.dp),
                ) { Text("${tvCategoryName(c)}  ${c.count}", color = Palette.Text, fontSize = 14.sp, maxLines = 1) }
            }
        }

        val line = when (status) {
            Status.Loading -> stringResource(R.string.status_loading)
            Status.Updating -> stringResource(R.string.status_updating)
            is Status.Error -> stringResource(R.string.status_error, (status as Status.Error).message)
            else -> null
        }
        if (line != null) Text(line, color = Palette.Muted, fontSize = 12.sp, modifier = Modifier.padding(start = 6.dp, bottom = 4.dp))

        Box(Modifier.weight(1f).fillMaxWidth()) {
            if (items.isEmpty() && status !is Status.Loading && status !is Status.Updating) {
                val t = when {
                    filter.categoryId == FAV -> stringResource(R.string.empty_no_fav)
                    filter.categoryId == HIST -> stringResource(R.string.empty_no_history)
                    filter.query.isBlank() -> stringResource(R.string.empty_no_content)
                    else -> stringResource(R.string.empty_no_result)
                }
                Text(t, color = Palette.Muted, textAlign = TextAlign.Center, modifier = Modifier.align(Alignment.Center).padding(32.dp))
            }
            val inHistory = filter.categoryId == HIST
            if (tab == Tab.LIVE) {
                LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    items(items, key = { "${it.kind}|${it.id}" }) { item ->
                        val fav = vm.isFavorite(item, ud)
                        Row(
                            Modifier.fillMaxWidth().background(Palette.Surface, RoundedCornerShape(10.dp))
                                .tvFocusable(onLongClick = { vm.toggleFavorite(item) }) { vm.play(item) }.padding(10.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            AsyncImage(item.logo, null, contentScale = ContentScale.Fit, modifier = Modifier.size(48.dp).clip(RoundedCornerShape(8.dp)).background(Palette.Surface2))
                            val now = vm.nowOn(item, guide)
                            Column(Modifier.weight(1f).padding(horizontal = 14.dp)) {
                                Text(item.name, color = Palette.Text, fontSize = 16.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                if (now != null) Text("${formatClock(now.start)}  ${now.title}", color = Palette.Muted, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            }
                            if (fav) Icon(Icons.Filled.Star, stringResource(R.string.favorite), tint = Color(0xFFF5C451))
                        }
                    }
                }
            } else {
                LazyVerticalGrid(
                    GridCells.Adaptive(150.dp), Modifier.fillMaxSize(), contentPadding = PaddingValues(10.dp),
                    horizontalArrangement = Arrangement.spacedBy(14.dp), verticalArrangement = Arrangement.spacedBy(14.dp),
                ) {
                    gridItems(items, key = { "${it.kind}|${it.id}" }) { item ->
                        PosterCard(vm, item, ud = ud, inHistory = inHistory)
                    }
                }
            }
        }
    }
}

@Composable
private fun PosterCard(vm: AppViewModel, item: MediaItem, ud: app.iptvplayer.data.UserData, inHistory: Boolean) {
    val fav = vm.isFavorite(item, ud)
    val watch = vm.watchOf(item, ud)
    Column(
        Modifier.background(Palette.Surface, RoundedCornerShape(10.dp))
            .tvFocusable(onLongClick = { if (inHistory) vm.removeFromHistory(item) else vm.toggleFavorite(item) }) { vm.play(item) },
    ) {
        Box(Modifier.fillMaxWidth().height(200.dp).background(Palette.Surface2)) {
            AsyncImage(item.logo, null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
            if (fav) Icon(Icons.Filled.Star, null, tint = Color(0xFFF5C451), modifier = Modifier.align(Alignment.TopEnd).padding(6.dp).size(20.dp))
            if (watch?.inProgress == true) {
                LinearProgressIndicator(
                    progress = { watch.progress.toFloat() }, color = Palette.Accent, trackColor = Palette.Line,
                    modifier = Modifier.align(Alignment.BottomCenter).fillMaxWidth().height(4.dp),
                )
            }
        }
        Text(item.name, color = Palette.Text, fontSize = 13.sp, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(8.dp))
    }
}

@Composable
private fun tvCategoryName(c: CategoryUi): String = when (c.id) {
    FAV -> "★ " + stringResource(R.string.cat_favorites)
    HIST -> "↺ " + stringResource(R.string.cat_history)
    ALL -> stringResource(R.string.cat_all)
    else -> c.name.orEmpty()
}
