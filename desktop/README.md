# IPTV Player — Windows (desktop)

Application WPF (.NET 9) avec **VLC embarqué** (LibVLCSharp). Fonctionnalités : voir le [README principal](../README.md).
Protocole de contrôle / synchronisation avec les appareils Android : [`../docs/protocole-controle-v1.md`](../docs/protocole-controle-v1.md).

Toutes les commandes se lancent dans **PowerShell, depuis la racine du dépôt**.

## 1. Prérequis

* Windows 10/11 x64.
* **.NET 9 SDK** (`dotnet --version` doit afficher 9.x).
* Aucun autre outil : le moteur VLC vient du paquet NuGet `VideoLAN.LibVLC.Windows`, restauré automatiquement.

## 2. Compiler

```powershell
./build.ps1 desktop                  # -> dist\desktop\IptvPlayer.exe
```

Produit **un seul fichier** (~186 Mo) avec le runtime .NET, WPF et le moteur VLC embarqués (seuls les plugins VLC sous licence LGPL sont inclus : les plugins GPL sont exclus automatiquement, voir `../THIRD_PARTY_NOTICES.md`).
La première compilation compresse VLC (quelques dizaines de secondes) ; les suivantes réutilisent `desktop\obj\embed\libvlc-lgpl.zip`.

Pour compiler sans fabriquer l'exécutable unique (développement, démarrage plus rapide) :

```powershell
dotnet build desktop/IptvPlayer.csproj -o $env:TEMP\iptv-build      # puis lancer $env:TEMP\iptv-build\IptvPlayer.exe
```

Utilisez un dossier de sortie séparé si une autre instance de l'application est déjà ouverte (elle verrouille ses fichiers).

## 3. Installer / désinstaller

* **Installer** : copier `IptvPlayer.exe` où l'on veut et le lancer. Aucune installation, aucun droit administrateur.
  Au premier lancement, VLC est extrait (quelques secondes) dans `%LocalAppData%\IptvPlayer\libvlc-<version>` ; les lancements suivants sont immédiats.
* **Désinstaller** : supprimer l'exécutable, `%AppData%\IptvPlayer` (données) et `%LocalAppData%\IptvPlayer` (moteur VLC extrait, miniatures).
* **Pare-feu** : la première fois que l'on utilise Réglages ▸ « Partager ma configuration… », Windows peut demander d'autoriser l'application sur les réseaux privés (le PC n'écoute que pendant cette fenêtre).

## 4. Données utilisateur

`%AppData%\IptvPlayer\` : `accounts.json` (mots de passe chiffrés DPAPI), `remote-targets.json` (appareils appairés, jetons chiffrés DPAPI), `sync-meta.json` (dates de modification pour la synchronisation), `state.json` (favoris, volume, dernier compte, préférences), `history.json` (historique de visionnage), `cache\` (catalogues et guides).
`%LocalAppData%\IptvPlayer\libvlc-*` : moteur VLC extrait (les anciennes versions inutilisées sont supprimées automatiquement au lancement) ; `%LocalAppData%\IptvPlayer\img\` : miniatures des logos.

## 5. Structure

| Dossier | Contenu |
|---|---|
| `Models/`, `Services/` | Catalogue, comptes, historique, EPG, enregistrement ; `Services/Remote/` : contrôle à distance, appairage, synchronisation, serveur de partage éphémère |
| `ViewModels/` | `MainViewModel` (guide, bandeau, favoris, lecture) |
| `Controls/` | Grille du guide TV (dessin direct) |
| `Localization/` | Traductions anglais / français (`Loc.cs`) : toute nouvelle chaîne s'y ajoute dans les deux langues |
| `Themes/`, `Assets/` | Thème sombre, icône et logo |
| `*.xaml`, `*.xaml.cs` | Fenêtres : principale, comptes, paramètres, télécommande, partage, épisodes, licences |

## 6. Logo

Source : `../assets/logo.svg`. Les fichiers `Assets/app.ico` et `Assets/logo.png` se régénèrent avec
`powershell.exe -STA -File tools\make-icon.ps1` (à relancer si le dessin change dans le script).
