package app.iptvplayer.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.iptvplayer.AppViewModel
import app.iptvplayer.EpisodesUi
import app.iptvplayer.R

/** Saisons et épisodes d'une série (✓ vu, ▶ % entamé), avec bascule vu / non vu. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun EpisodesSheet(vm: AppViewModel, ui: EpisodesUi) {
    val ud by vm.userData.data.collectAsState()
    ModalBottomSheet(
        onDismissRequest = vm::closeEpisodes,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        containerColor = Palette.Surface,
    ) {
        Column(Modifier.padding(horizontal = 16.dp).heightIn(min = 240.dp)) {
            Text(ui.series.name, color = Palette.Text, fontSize = 20.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(stringResource(R.string.episodes_title), color = Palette.Muted, fontSize = 13.sp, modifier = Modifier.padding(bottom = 8.dp))

            when {
                ui.loading -> Text(stringResource(R.string.episodes_loading), color = Palette.Muted, modifier = Modifier.padding(24.dp))
                ui.episodes.isEmpty() -> Text(stringResource(R.string.episodes_none), color = Palette.Muted, modifier = Modifier.padding(24.dp))
                else -> LazyColumn {
                    ui.episodes.groupBy { it.season }.forEach { (season, eps) ->
                        item(key = "s$season") {
                            Text(stringResource(R.string.season_n, season), color = Palette.Accent, fontSize = 14.sp, modifier = Modifier.padding(top = 14.dp, bottom = 4.dp))
                        }
                        items(eps, key = { it.id }) { ep ->
                            val w = vm.episodeEntry(ui.series, ep, ud)
                            val status = when { w?.completed == true -> "✓"; w?.inProgress == true -> "▶ ${(w.progress * 100).toInt()} %"; else -> "" }
                            Row(Modifier.fillMaxWidth().clickable { vm.playEpisode(ui.series, ep) }.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                                Text(ep.display, color = Palette.Text, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                                if (status.isNotEmpty()) Text(status, color = Palette.Accent, fontSize = 12.sp, modifier = Modifier.padding(horizontal = 8.dp))
                                IconButton(onClick = { vm.setEpisodeWatched(ui.series, ep, w?.completed != true) }) {
                                    Icon(Icons.Filled.Check, stringResource(if (w?.completed == true) R.string.mark_unwatched else R.string.mark_watched),
                                        tint = if (w?.completed == true) Palette.Accent else Palette.Line)
                                }
                            }
                        }
                    }
                }
            }
            Box(Modifier.padding(bottom = 16.dp))
        }
    }
}
