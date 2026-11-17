package app.iptvplayer.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.iptvplayer.AppViewModel
import app.iptvplayer.R

/** Premier lancement sur TV : QR code a scanner avec l'application du telephone (ou saisie manuelle d'un compte). */
@Composable
fun TvSetupScreen(vm: AppViewModel, onManual: () -> Unit) {
    Row(Modifier.fillMaxSize().padding(48.dp), horizontalArrangement = Arrangement.spacedBy(48.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Text(stringResource(R.string.tv_setup_title), color = Palette.Text, fontSize = 30.sp)
            Text(stringResource(R.string.tv_setup_steps), color = Palette.Muted, fontSize = 17.sp)
            Box(
                Modifier.padding(top = 18.dp).background(Palette.AccentSoft, RoundedCornerShape(12.dp))
                    .tvFocusable(RoundedCornerShape(12.dp)) { onManual() }.padding(horizontal = 22.dp, vertical = 12.dp),
            ) { Text(stringResource(R.string.tv_setup_manual), color = Palette.Text, fontSize = 16.sp) }
        }
        Box(Modifier.width(300.dp).background(Palette.Surface, RoundedCornerShape(16.dp)).padding(24.dp), contentAlignment = Alignment.Center) {
            OfferPanel(vm, 240.dp)
        }
    }
}
