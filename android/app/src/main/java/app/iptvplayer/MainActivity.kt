package app.iptvplayer

import android.os.Bundle
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.app.AppCompatDelegate
import androidx.core.os.LocaleListCompat
import app.iptvplayer.ui.IptvApp
import app.iptvplayer.ui.IptvTheme

class MainActivity : AppCompatActivity() {
    private val vm: AppViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        // Langue de l'interface : anglais par défaut (comme l'application Windows), sinon le choix de l'utilisateur.
        if (AppCompatDelegate.getApplicationLocales().isEmpty) {
            AppCompatDelegate.setApplicationLocales(LocaleListCompat.forLanguageTags("en"))
        }
        super.onCreate(savedInstanceState)
        setContent { IptvTheme { IptvApp(vm) } }
    }
}
