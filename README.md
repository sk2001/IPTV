# IPTV Player

Lecteur IPTV Windows autonome (WPF / .NET 9) avec **VLC embarqué** (LibVLCSharp).

## Fonctionnalités

- Comptes multiples : **Xtream Codes** (serveur + identifiants, ou collage direct d'une URL `get.php?username=…&password=…`) et **playlists M3U** (URL ou fichier local), avec bouton *Tester*.
- Mots de passe chiffrés avec DPAPI (`%AppData%\IptvPlayer\accounts.json`).
- Live, Films, Séries (choix de la saison / de l'épisode), catégories, recherche instantanée, favoris.
- Guide des programmes (EPG court) pour les chaînes Xtream.
- Lecteur VLC : barre de progression (VOD), volume, muet, pistes audio / sous-titres, plein écran.
- Raccourcis : `Espace` pause · `F` / `F11` plein écran · `Échap` quitter le plein écran · `M` muet · `↑ ↓` volume · `← →` ±10 s · double-clic sur la vidéo = plein écran.
- Performances : catalogue mis en cache (JSON gzip) → affichage instantané au lancement, rafraîchi en arrière-plan ; listes virtualisées (des dizaines de milliers de chaînes) ; décodage matériel VLC ; initialisation de VLC en arrière-plan.

## Compiler

```powershell
./build.ps1
```

Produit **un seul fichier** : `dist\IptvPlayer.exe` (~186 Mo) avec le runtime .NET, WPF et le moteur VLC embarqués.
Au premier lancement, VLC est extrait (quelques secondes) dans `%LocalAppData%\IptvPlayer\libvlc-<version>` ; les lancements suivants sont immédiats.

## Données utilisateur

`%AppData%\IptvPlayer\` : `accounts.json`, `state.json` (favoris, volume, dernier compte), `cache\` (catalogues). `%LocalAppData%\IptvPlayer\libvlc-*` : moteur VLC extrait.

