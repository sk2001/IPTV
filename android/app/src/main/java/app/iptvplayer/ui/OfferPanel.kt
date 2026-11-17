package app.iptvplayer.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import app.iptvplayer.AppViewModel
import app.iptvplayer.R
import app.iptvplayer.remote.qrBitmap

/**
 * Affiche l'offre de configuration de cet appareil : QR code (adresse + PIN a usage unique + empreinte du certificat),
 * adresse ip:port et PIN en clair. L'offre est ouverte a l'affichage et fermee quand le panneau disparait.
 */
@Composable
fun OfferPanel(vm: AppViewModel, qrSize: Dp = 220.dp) {
    val rc = vm.remote
    val offer by rc.offer.collectAsState()
    var failed by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) { failed = !rc.openOffer() }
    DisposableEffect(Unit) { onDispose { rc.closeOffer() } }

    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
        val o = offer
        if (o != null) {
            val qr = remember(o.link) { qrBitmap(o.link).asImageBitmap() }
            Image(qr, stringResource(R.string.remote_qr_desc), Modifier.size(qrSize))
            Text("${o.host}:${o.port}", color = Palette.Text, fontSize = 18.sp)
            Text(stringResource(R.string.offer_pin, o.pin), color = Palette.Text, fontSize = 26.sp)
            Text(stringResource(R.string.offer_valid), color = Palette.Muted, fontSize = 12.sp)
        } else {
            Text(stringResource(if (failed) R.string.offer_no_network else R.string.offer_expired), color = Palette.Muted, fontSize = 14.sp, modifier = Modifier.padding(8.dp))
            OutlinedButton(onClick = { failed = !rc.openOffer() }) { Text(stringResource(R.string.offer_new)) }
        }
    }
}
