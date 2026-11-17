package app.iptvplayer.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
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
import app.iptvplayer.model.Kind
import app.iptvplayer.model.MediaItem
import app.iptvplayer.net.Programme
import coil3.compose.AsyncImage
import kotlinx.coroutines.delay

private const val MIN = 60_000L
private const val HOUR = 3_600_000L
private const val WINDOW_H = 38                     // fenetre du guide : de -2 h a +36 h

private fun floor30(t: Long) = t - (t % (30 * MIN))

/**
 * TV en direct « a la Plex » : bandeau de l'emission selectionnee (+ mini-lecteur), filtre, puis guide en grille
 * (chaines en lignes, programmes en colonnes horaires). Telephone (tactile) et TV (D-pad) partagent ce code.
 */
@Composable
fun LiveTvScreen(vm: AppViewModel, tv: Boolean) {
    val items by vm.items.collectAsState()
    val categories by vm.categories.collectAsState()
    val filter by vm.filter.collectAsState()
    val guide by vm.epg.collectAsState()
    val ud by vm.userData.data.collectAsState()
    val sel by vm.guideSel.collectAsState()
    val ps by vm.player.state.collectAsState()
    val full by vm.playerFull.collectAsState()
    val previous by vm.previousChannel.collectAsState()
    val wide = tv || LocalConfiguration.current.screenWidthDp >= 840
    val miniActive = ps.request != null && !full

    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(Unit) { while (true) { delay(30_000); now = System.currentTimeMillis() } }

    val dpPerMin = if (tv) 5f else 4f
    val windowStart = remember { floor30(System.currentTimeMillis() - 2 * HOUR) }
    val hScroll = rememberScrollState()
    val density = LocalDensity.current
    LaunchedEffect(Unit) {
        val px = with(density) { ((System.currentTimeMillis() - 30 * MIN - windowStart) / MIN.toFloat() * dpPerMin).dp.roundToPx() }
        hScroll.scrollTo(px.coerceAtLeast(0))
    }

    val live = remember(items) { items.filter { it.kind == Kind.LIVE } }
    val selItem = live.firstOrNull { it.id == sel.itemId } ?: ps.request?.let { r -> live.firstOrNull { it.id == r.itemId } } ?: live.firstOrNull()
    val progs = selItem?.epgId?.let { guide[it] }.orEmpty()
    val selProg = progs.firstOrNull { it.start == sel.progStart } ?: progs.firstOrNull { it.start <= now && it.stop > now } ?: progs.firstOrNull { it.start > now }
    val channelW = if (tv) 230.dp else if (wide) 200.dp else 128.dp

    Column(Modifier.fillMaxSize()) {
        Hero(vm, selItem, selProg, now, tv, wide, miniActive, previous != null, selItem?.let { vm.isFavorite(it, ud) } == true)
        FilterRow(vm, categories, filter.categoryId, filter.query, tv)

        Row(Modifier.fillMaxWidth()) {
            Spacer(Modifier.width(channelW))
            Box(Modifier.weight(1f).horizontalScroll(hScroll)) {
                Box(Modifier.width((WINDOW_H * 60 * dpPerMin).dp).height(22.dp)) {
                    for (h in 0 until WINDOW_H) {
                        val t = windowStart + h * HOUR
                        Text(formatClock(t), color = Palette.Muted, fontSize = 11.sp, modifier = Modifier.padding(start = (h * 60 * dpPerMin + 4).dp))
                    }
                }
            }
        }

        Box(Modifier.weight(1f).fillMaxWidth()) {
            if (live.isEmpty()) {
                Text(stringResource(R.string.empty_no_content), color = Palette.Muted, modifier = Modifier.align(Alignment.Center).padding(24.dp))
            }
            LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 8.dp)) {
                items(live, key = { it.id }) { item ->
                    val list = item.epgId?.let { guide[it] }.orEmpty()
                    GuideRow(vm, item, list, vm.isFavorite(item, ud), item.id == selItem?.id, sel.progStart.takeIf { sel.itemId == item.id },
                        windowStart, now, dpPerMin, channelW, hScroll, tv)
                }
            }
        }

        if (!wide && miniActive) MiniBar(vm, ps.request?.title.orEmpty(), ps.playing)
    }
}

// ---------------------------------------------------------------------------------------------- Bandeau

@Composable
private fun Hero(vm: AppViewModel, item: MediaItem?, prog: Programme?, now: Long, tv: Boolean, wide: Boolean, mini: Boolean, hasPrevious: Boolean, favorite: Boolean) {
    val shape = RoundedCornerShape(16.dp)
    Row(
        Modifier.fillMaxWidth().padding(horizontal = if (tv) 20.dp else 12.dp, vertical = if (tv) 10.dp else 8.dp).clip(shape)
            .background(Brush.linearGradient(listOf(Color(0xFF1F3F70), Color(0xFF16202F), Color(0xFF0E1117))))
            .padding(if (tv) 16.dp else 14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            if (item == null) {
                Text(stringResource(R.string.hero_none), color = Palette.Muted)
                return@Column
            }
            val isNow = prog != null && prog.start <= now && prog.stop > now
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                AsyncImage(item.logo, null, contentScale = ContentScale.Fit, modifier = Modifier.size(if (tv) 44.dp else 34.dp).clip(RoundedCornerShape(8.dp)).background(Color(0x26FFFFFF)))
                Text(item.name, color = Palette.Text, fontSize = if (tv) 16.sp else 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
                if (prog != null) {
                    val tag = when { isNow -> stringResource(R.string.hero_on_now); prog.start > now -> stringResource(R.string.hero_up_next, formatClock(prog.start)); else -> stringResource(R.string.hero_ended) }
                    Text(tag, color = Color.White, fontSize = 10.5.sp, modifier = Modifier.background(if (isNow) Color(0xFFF85149) else Color(0x33FFFFFF), RoundedCornerShape(6.dp)).padding(horizontal = 8.dp, vertical = 2.dp))
                }
            }
            Text(prog?.title ?: item.name, color = Palette.Text, fontSize = if (tv) 24.sp else 20.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            if (prog != null) {
                Text("${formatClock(prog.start)} – ${formatClock(prog.stop)}  ·  ${((prog.stop - prog.start) / MIN)} min", color = Color(0xFFC8D1DC), fontSize = 13.sp)
                if (isNow) LinearProgressIndicator(progress = { ((now - prog.start).toFloat() / (prog.stop - prog.start)).coerceIn(0f, 1f) }, color = Palette.Accent, trackColor = Color(0x33FFFFFF), modifier = Modifier.fillMaxWidth().height(3.dp))
                Text(prog.desc.ifBlank { stringResource(R.string.hero_no_desc) }, color = Color(0xFFC8D1DC), fontSize = if (tv) 14.sp else 12.5.sp, maxLines = if (tv) 2 else if (wide) 3 else 2, overflow = TextOverflow.Ellipsis)
            }
            Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                if (tv) {
                    HeroButton(stringResource(R.string.hero_watch), true) { vm.play(item) }
                    HeroButton(if (favorite) "★" else "☆", false) { vm.toggleFavorite(item) }
                    if (hasPrevious) HeroButton(stringResource(R.string.hero_last), false) { vm.playPrevious() }
                } else {
                    Button(onClick = { vm.play(item) }, colors = ButtonDefaults.buttonColors(containerColor = Palette.Accent)) { Text(stringResource(R.string.hero_watch)) }
                    IconButton(onClick = { vm.toggleFavorite(item) }) { Icon(Icons.Filled.Star, stringResource(R.string.favorite), tint = if (favorite) Color(0xFFF5C451) else Palette.Line) }
                    if (hasPrevious) OutlinedButton(onClick = { vm.playPrevious() }) { Text(stringResource(R.string.hero_last)) }
                }
            }
        }
        if (wide) {
            Box(Modifier.padding(start = 18.dp).width(if (tv) 288.dp else 300.dp).height(if (tv) 162.dp else 169.dp).clip(RoundedCornerShape(12.dp)).background(Color.Black), contentAlignment = Alignment.Center) {
                if (mini) {
                    PlayerSurface(vm, Modifier.fillMaxSize())
                    Box(Modifier.fillMaxSize().then(if (tv) Modifier.tvFocusable(RoundedCornerShape(12.dp)) { vm.expand() } else Modifier.clickable { vm.expand() }))
                } else {
                    AsyncImage(item?.logo, null, contentScale = ContentScale.Fit, modifier = Modifier.size(72.dp), alpha = 0.35f)
                }
            }
        }
    }
}

@Composable
private fun HeroButton(label: String, primary: Boolean, onClick: () -> Unit) {
    Box(
        Modifier.background(if (primary) Palette.Accent else Color(0x33FFFFFF), RoundedCornerShape(10.dp))
            .tvFocusable(RoundedCornerShape(10.dp), onClick = onClick).padding(horizontal = 22.dp, vertical = 10.dp),
        contentAlignment = Alignment.Center,
    ) { Text(label, color = Color.White, fontSize = 15.sp) }
}

// ---------------------------------------------------------------------------------------------- Filtre

@Composable
private fun liveCatName(c: CategoryUi): String = when (c.id) {
    FAV -> "★ " + stringResource(R.string.cat_favorites)
    HIST -> "↺ " + stringResource(R.string.cat_history)
    ALL -> stringResource(R.string.cat_all)
    else -> c.name.orEmpty()
}

@Composable
private fun FilterRow(vm: AppViewModel, categories: List<CategoryUi>, selected: String, query: String, tv: Boolean) {
    Column(Modifier.fillMaxWidth().padding(horizontal = if (tv) 20.dp else 12.dp)) {
        if (!tv) Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedTextField(
                value = query, onValueChange = vm::setQuery, singleLine = true, modifier = Modifier.weight(1f).height(52.dp),
                leadingIcon = { Icon(Icons.Filled.Search, null, tint = Palette.Muted) },
                placeholder = { Text(stringResource(R.string.search_hint), color = Palette.Muted, fontSize = 13.sp) },
                shape = RoundedCornerShape(12.dp),
                colors = OutlinedTextFieldDefaults.colors(
                    focusedTextColor = Palette.Text, unfocusedTextColor = Palette.Text,
                    focusedContainerColor = Palette.Surface2, unfocusedContainerColor = Palette.Surface2,
                    focusedBorderColor = Palette.Accent, unfocusedBorderColor = Palette.Line, cursorColor = Palette.Text,
                ),
            )
            IconButton(onClick = { vm.refresh() }) { Icon(Icons.Filled.Refresh, stringResource(R.string.refresh), tint = Palette.Muted) }
        }
        LazyRow(Modifier.padding(vertical = if (tv) 2.dp else 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), contentPadding = PaddingValues(horizontal = 4.dp, vertical = 3.dp)) {
            items(categories, key = { it.id }) { c ->
                val on = selected == c.id
                val shape = RoundedCornerShape(18.dp)
                Box(
                    Modifier.background(if (on) Palette.AccentSoft else Palette.Surface2, shape)
                        .then(if (tv) Modifier.tvFocusable(shape) { vm.setCategory(c.id) } else Modifier.clip(shape).clickable { vm.setCategory(c.id) })
                        .padding(horizontal = 14.dp, vertical = 7.dp),
                ) { Text("${liveCatName(c)}  ${c.count}", color = Palette.Text, fontSize = 13.sp, maxLines = 1) }
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------- Grille

@Composable
private fun GuideRow(
    vm: AppViewModel, item: MediaItem, progs: List<Programme>, favorite: Boolean, rowSelected: Boolean, selStart: Long?,
    windowStart: Long, now: Long, dpPerMin: Float, channelW: Dp, hScroll: androidx.compose.foundation.ScrollState, tv: Boolean,
) {
    val rowH = if (tv) 64.dp else 58.dp
    val windowEnd = windowStart + WINDOW_H * HOUR
    val shape = RoundedCornerShape(10.dp)
    Row(Modifier.fillMaxWidth().height(rowH), verticalAlignment = Alignment.CenterVertically) {
        // Cellule chaine : logo, nom, etoile
        Row(
            Modifier.width(channelW).fillMaxHeight().padding(horizontal = 6.dp, vertical = 2.dp)
                .then(if (tv) Modifier.tvFocusable(shape, onLongClick = { vm.toggleFavorite(item) }, onFocus = { vm.selectGuide(item, null) }) { vm.play(item) }
                      else Modifier.clip(shape).clickable { vm.selectGuide(item, null) })
                .background(if (rowSelected) Palette.AccentSoft else Color.Transparent, shape).padding(horizontal = 6.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            AsyncImage(item.logo, null, contentScale = ContentScale.Fit, modifier = Modifier.size(if (tv) 40.dp else 34.dp).clip(RoundedCornerShape(8.dp)).background(Palette.Surface2))
            Text(item.name, color = Palette.Text, fontSize = if (tv) 14.sp else 12.5.sp, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
            if (favorite) Icon(Icons.Filled.Star, null, tint = Color(0xFFF5C451), modifier = Modifier.size(16.dp))
        }
        // Programmes (defilement horizontal commun a toutes les lignes)
        Box(Modifier.weight(1f).fillMaxHeight().horizontalScroll(hScroll)) {
            val totalW = (WINDOW_H * 60 * dpPerMin).dp
            val nowX = ((now - windowStart) / MIN.toFloat() * dpPerMin)
            Row(
                Modifier.width(totalW).fillMaxHeight().drawBehind {
                    drawRect(Color(0xFFF85149), Offset(nowX.dp.toPx() - 1.dp.toPx(), 0f), Size(2.dp.toPx(), size.height))
                },
                verticalAlignment = Alignment.CenterVertically,
            ) {
                if (progs.isEmpty()) {
                    Text(stringResource(R.string.guide_no_info), color = Palette.Muted, fontSize = 12.sp, modifier = Modifier.padding(start = ((now - windowStart) / MIN.toFloat() * dpPerMin - 30).coerceAtLeast(8f).dp))
                } else {
                    var cursor = windowStart
                    for (p in progs) {
                        if (p.stop <= cursor) continue
                        val s = maxOf(p.start, cursor)
                        if (s >= windowEnd) break
                        val e = minOf(p.stop, windowEnd)
                        if (s > cursor) Spacer(Modifier.width(((s - cursor) / MIN.toFloat() * dpPerMin).dp))
                        val w = ((e - s) / MIN.toFloat() * dpPerMin).dp
                        ProgrammeCell(vm, item, p, w, p.start <= now && p.stop > now, p.stop <= now, selStart == p.start, tv)
                        cursor = e
                    }
                }
            }
        }
    }
}

@Composable
private fun ProgrammeCell(vm: AppViewModel, item: MediaItem, p: Programme, w: Dp, isNow: Boolean, isPast: Boolean, selected: Boolean, tv: Boolean) {
    val shape = RoundedCornerShape(8.dp)
    Column(
        Modifier.width(w).fillMaxHeight().padding(1.dp).clip(shape)
            .background(if (isNow) Palette.AccentSoft else if (isPast) Palette.Surface else Palette.Surface2)
            .then(if (selected) Modifier.border(2.dp, Color.White, shape) else if (isNow) Modifier.border(1.dp, Palette.Accent, shape) else Modifier)
            .then(
                if (tv) Modifier.tvFocusable(shape, onFocus = { vm.selectGuide(item, p) }) { vm.play(item) }
                else Modifier.clickable { if (selected) vm.play(item) else vm.selectGuide(item, p) },
            )
            .padding(horizontal = 8.dp, vertical = 5.dp),
        verticalArrangement = Arrangement.Center,
    ) {
        Text(p.title, color = if (isPast) Palette.Muted else Palette.Text, fontSize = 12.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
        Text("${formatClock(p.start)} – ${formatClock(p.stop)}", color = Palette.Muted, fontSize = 10.5.sp, maxLines = 1)
    }
}

// ---------------------------------------------------------------------------------------------- Barre mini (telephone)

@Composable
private fun MiniBar(vm: AppViewModel, title: String, playing: Boolean) {
    Row(
        Modifier.fillMaxWidth().background(Palette.Surface2).clickable { vm.expand() }.padding(horizontal = 10.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Box(Modifier.width(96.dp).height(54.dp).clip(RoundedCornerShape(8.dp)).background(Color.Black)) { PlayerSurface(vm, Modifier.fillMaxSize()) }
        Column(Modifier.weight(1f)) {
            Text(title, color = Palette.Text, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(stringResource(R.string.mini_tap), color = Palette.Muted, fontSize = 11.sp)
        }
        IconButton(onClick = { vm.player.togglePause() }) { Icon(if (playing) Icons.Filled.Pause else Icons.Filled.PlayArrow, null, tint = Palette.Text) }
        IconButton(onClick = { vm.closePlayer() }) { Icon(Icons.Filled.Close, stringResource(R.string.back), tint = Palette.Muted) }
    }
}
