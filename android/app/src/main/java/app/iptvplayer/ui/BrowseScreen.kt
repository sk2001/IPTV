package app.iptvplayer.ui

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.iptvplayer.ALL
import app.iptvplayer.AppViewModel
import app.iptvplayer.CategoryUi
import app.iptvplayer.FAV
import app.iptvplayer.HIST
import app.iptvplayer.R
import app.iptvplayer.Status
import app.iptvplayer.data.UserData
import app.iptvplayer.model.Kind
import app.iptvplayer.model.MediaItem
import coil3.compose.AsyncImage

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BrowseScreen(vm: AppViewModel, onManageAccounts: () -> Unit) {
    val items by vm.items.collectAsState()
    val categories by vm.categories.collectAsState()
    val filter by vm.filter.collectAsState()
    val ud by vm.userData.data.collectAsState()
    val status by vm.status.collectAsState()
    val account by vm.selectedAccount.collectAsState()
    val accounts by vm.accounts.collectAsState()
    var menu by remember { mutableStateOf(false) }

    Column(Modifier.fillMaxSize()) {
        // Compte actif + actualisation
        Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.weight(1f)) {
                TextButton(onClick = { menu = true }) {
                    Text(account?.name ?: stringResource(R.string.account_none), color = Palette.Text, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                DropdownMenu(expanded = menu, onDismissRequest = { menu = false }, containerColor = Palette.Surface2) {
                    accounts.forEach { a ->
                        DropdownMenuItem(text = { Text(a.name, color = Palette.Text) }, onClick = { menu = false; vm.selectAccount(a.id) })
                    }
                    DropdownMenuItem(text = { Text(stringResource(R.string.manage_accounts), color = Palette.Accent) }, onClick = { menu = false; onManageAccounts() })
                }
            }
            IconButton(onClick = { vm.refresh() }) { Icon(Icons.Filled.Refresh, stringResource(R.string.refresh), tint = Palette.Muted) }
        }

        // Recherche
        OutlinedTextField(
            value = filter.query,
            onValueChange = vm::setQuery,
            singleLine = true,
            leadingIcon = { Icon(Icons.Filled.Search, null, tint = Palette.Muted) },
            placeholder = { Text(stringResource(R.string.search_hint), color = Palette.Muted) },
            modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp),
            shape = RoundedCornerShape(12.dp),
            colors = OutlinedTextFieldDefaults.colors(
                focusedTextColor = Palette.Text, unfocusedTextColor = Palette.Text,
                focusedContainerColor = Palette.Surface2, unfocusedContainerColor = Palette.Surface2,
                focusedBorderColor = Palette.Accent, unfocusedBorderColor = Palette.Line, cursorColor = Palette.Text,
            ),
        )

        // Catégories
        LazyRow(Modifier.padding(vertical = 8.dp), contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            items(categories, key = { it.id }) { c ->
                FilterChip(
                    selected = filter.categoryId == c.id,
                    onClick = { vm.setCategory(c.id) },
                    label = { Text("${categoryName(c)}  ${c.count}", maxLines = 1) },
                    colors = FilterChipDefaults.filterChipColors(
                        containerColor = Palette.Surface2, labelColor = Palette.Text,
                        selectedContainerColor = Palette.AccentSoft, selectedLabelColor = Color.White,
                    ),
                )
            }
        }

        StatusLine(status)

        Box(Modifier.weight(1f).fillMaxWidth()) {
            val emptyText = when {
                items.isNotEmpty() || status is Status.Loading -> null
                filter.categoryId == FAV -> stringResource(R.string.empty_no_fav)
                filter.categoryId == HIST -> stringResource(R.string.empty_no_history)
                filter.query.isBlank() -> stringResource(R.string.empty_no_content)
                else -> stringResource(R.string.empty_no_result)
            }
            if (emptyText != null && status !is Status.Updating) {
                Text(emptyText, color = Palette.Muted, textAlign = TextAlign.Center, modifier = Modifier.align(Alignment.Center).padding(32.dp))
            }
            LazyColumn(Modifier.fillMaxSize()) {
                items(items, key = { "${it.kind}|${it.id}" }) { item ->
                    ItemRow(vm, item, ud, inHistory = filter.categoryId == HIST)
                }
            }
        }
    }
}

@Composable
private fun categoryName(c: CategoryUi): String = when (c.id) {
    FAV -> "★ " + stringResource(R.string.cat_favorites)
    HIST -> "↺ " + stringResource(R.string.cat_history)
    ALL -> stringResource(R.string.cat_all)
    else -> c.name.orEmpty()
}

@Composable
private fun StatusLine(status: Status) {
    val text = when (status) {
        Status.None -> null
        Status.Loading -> stringResource(R.string.status_loading)
        Status.Updating -> stringResource(R.string.status_updating)
        is Status.Cached -> stringResource(R.string.status_cached, formatDateTime(status.fetchedAt))
        is Status.Updated -> stringResource(R.string.status_updated, status.count)
        is Status.Error -> stringResource(R.string.status_error, status.message)
    }
    if (text != null) {
        Text(
            text, fontSize = 11.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
            color = if (status is Status.Error) MaterialTheme3Error else Palette.Muted,
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 2.dp),
        )
    }
}

private val MaterialTheme3Error = Color(0xFFF85149)
private val Gold = Color(0xFFF5C451)

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun ItemRow(vm: AppViewModel, item: MediaItem, ud: UserData, inHistory: Boolean) {
    val fav = vm.isFavorite(item, ud)
    val watch = vm.watchOf(item, ud)
    val seen = stringResource(R.string.watched)
    val guide by vm.epg.collectAsState()
    val now = if (item.kind == Kind.LIVE) vm.nowOn(item, guide) else null

    val sub = buildList {
        if (!item.rating.isNullOrBlank()) add("★ ${item.rating}")
        if (watch != null) {
            val state = when { watch.completed -> "✓"; watch.inProgress -> "▶ ${(watch.progress * 100).toInt()} %"; else -> null }
            if (item.kind == Kind.SERIES) add("S%02dE%02d".format(watch.season, watch.number) + (state?.let { " $it" } ?: ""))
            else if (state != null) add(if (watch.completed) "✓ $seen" else state)
        }
    }.joinToString("  ·  ")

    Row(
        Modifier.fillMaxWidth()
            .combinedClickable(onClick = { vm.play(item) }, onLongClick = { if (inHistory) vm.removeFromHistory(item) })
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        AsyncImage(
            model = item.logo, contentDescription = null, contentScale = ContentScale.Fit,
            modifier = Modifier.size(52.dp).clip(RoundedCornerShape(8.dp)).background(Palette.Surface2),
        )
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
            Text(item.name, color = Palette.Text, fontSize = 14.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            if (now != null) Text("${formatClock(now.start)}  ${now.title}", color = Palette.Muted, fontSize = 11.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            if (sub.isNotEmpty()) Text(sub, color = Palette.Muted, fontSize = 11.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            if (watch?.inProgress == true) {
                LinearProgressIndicator(
                    progress = { watch.progress.toFloat() }, color = Palette.Accent, trackColor = Palette.Line,
                    modifier = Modifier.fillMaxWidth().padding(top = 4.dp).height(2.dp),
                )
            }
        }
        IconButton(onClick = { vm.toggleFavorite(item) }) {
            Icon(Icons.Filled.Star, stringResource(R.string.favorite), tint = if (fav) Gold else Palette.Line)
        }
    }
}
