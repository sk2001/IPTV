package app.iptvplayer.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

/** Mêmes couleurs que l'application Windows (thème sombre, accent bleu). */
object Palette {
    val Bg = Color(0xFF0E1117)
    val Surface = Color(0xFF141922)
    val Surface2 = Color(0xFF1D2430)
    val Line = Color(0xFF2A323D)
    val Accent = Color(0xFF4C8DFF)
    val AccentSoft = Color(0xFF1B3358)
    val Text = Color(0xFFE6EDF3)
    val Muted = Color(0xFF8B98A9)
}

private val Scheme = darkColorScheme(
    primary = Palette.Accent,
    onPrimary = Color.White,
    background = Palette.Bg,
    onBackground = Palette.Text,
    surface = Palette.Surface,
    onSurface = Palette.Text,
    surfaceVariant = Palette.Surface2,
    onSurfaceVariant = Palette.Muted,
    outline = Palette.Line,
)

@Composable
fun IptvTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = Scheme, content = content)
}
