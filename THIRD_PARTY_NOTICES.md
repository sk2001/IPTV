# Licences et avis des composants tiers

IPTV Player s'appuie sur les composants ci-dessous. Ce fichier (avec `LICENSES/`) est aussi accessible dans l'application :
**Paramètres → À propos → Licences des composants**.

> Le code propre à ce projet est sous **licence MIT** (fichier `LICENSE`). Elle est compatible avec toutes les licences ci-dessous :
> la LGPL autorise l'utilisation de libVLC / LibVLCSharp comme bibliothèques dynamiques par une application sous une autre licence,
> sous réserve des obligations listées plus bas.

## Composants livrés dans `IptvPlayer.exe`

| Composant | Version | Licence | Rôle |
|---|---|---|---|
| **libVLC** (libvlc, libvlccore + plugins) — VideoLAN / VLC Authors | 3.0.24 | **LGPL-2.1-or-later** (plugins LGPL uniquement, voir ci-dessous) | Lecture, décodage, sortie de flux (enregistrement) |
| **LibVLCSharp** et **LibVLCSharp.WPF** — VideoLAN | 3.10.2 | **LGPL-2.1-or-later** | Interface .NET de libVLC, contrôle vidéo WPF |
| **CommunityToolkit.Mvvm** — .NET Foundation and Contributors | 8.4.2 | MIT | MVVM (propriétés observables, commandes) |
| **QRCoder** — Raffael Herrmann and contributors | 1.6.0 | MIT | QR code generation (share my setup) |
| **System.Security.Cryptography.ProtectedData** — Microsoft | 9.0.0 | MIT | Chiffrement DPAPI des mots de passe |
| **Runtime .NET 9** et **WPF** (Microsoft.NETCore.App, Microsoft.WindowsDesktop.App) — Microsoft | 9.0.x | MIT | Runtime autonome inclus dans l'exe |

Le runtime .NET embarque des binaires natifs Microsoft (`D3DCompiler_47`, `vcruntime140`, `wpfgfx`, `PresentationNative`, `PenImc`) distribués
tels quels dans le pack d'exécution officiel, dont la redistribution avec une application autonome est prévue par Microsoft.
Les avis de composants tiers du runtime sont reproduits dans `LICENSES/DOTNET-THIRD-PARTY-NOTICES.txt`.

## Éléments non livrés

* **Segoe Fluent Icons / Segoe UI Variable** (polices Windows) : utilisées si elles sont installées sur la machine, **non redistribuées**.
* **Outils de build** (SDK .NET, ILLink, Crossgen2, PowerShell) : utilisés uniquement pour compiler, rien n'en est livré en dehors du runtime ci-dessus.
* **Logo et icône** : œuvre originale du projet (`assets/logo.svg`, `tools/make-icon.ps1`). Le cône VLC n'est pas utilisé.

## Moteur VLC : uniquement des plugins LGPL

Le paquet NuGet `VideoLAN.LibVLC.Windows` contient 321 plugins : **317 sont LGPL-2.1+**, **4 sont GPL-2+** (`libx26410b` — encodeur x264,
`liblua`, `libdolby_surround_decoder`, `libheadphone_channel_mixer`). Pour que la distribution ne contienne que du code LGPL, `build.ps1`
**exclut automatiquement** tout plugin dont la licence déclarée n'est pas la LGPL (VLC inscrit la licence en toutes lettres dans chaque
plugin), ainsi que le dossier `lua\` et les en-têtes de développement. L'application n'a besoin d'aucun de ces éléments (pas de transcodage,
playlists analysées par l'application elle-même).

## Obligations de la LGPL-2.1 (libVLC, LibVLCSharp)

1. **Texte de la licence** : `LICENSES/LGPL-2.1.txt`, aussi visible dans l'application.
2. **Remplacement de la bibliothèque** : libVLC est une bibliothèque dynamique (DLL) séparée. Pour utiliser votre propre version, placez un dossier
   `libvlc` (contenant `libvlc.dll`, `libvlccore.dll` et `plugins\`) **à côté de `IptvPlayer.exe`** : il prend le pas sur le moteur embarqué.
   Le moteur embarqué est extrait dans `%LocalAppData%\IptvPlayer\libvlc-<version>` et peut aussi y être remplacé.
3. **LibVLCSharp** : le code source de ce projet est disponible avec son script de construction (`build.ps1`) ; il suffit de changer la version du
   paquet `LibVLCSharp.WPF` dans `desktop/IptvPlayer.csproj` (ou de le remplacer par une version modifiée) puis de reconstruire.
4. **Sources** de VLC / libVLC : <https://www.videolan.org/vlc/> (archives : <https://download.videolan.org/pub/videolan/vlc/>) ;
   paquet NuGet : <https://code.videolan.org/videolan/libvlc-nuget> ; LibVLCSharp : <https://code.videolan.org/videolan/LibVLCSharp>.
5. Toute distribution du binaire doit s'accompagner de ces avis et d'un moyen d'obtenir le code source de ce projet.

## Marques

« VLC » et le cône sont des marques de VideoLAN. IPTV Player n'est ni affilié à VideoLAN ni approuvé par lui ;
« VLC » n'est cité que pour décrire le moteur de lecture utilisé.

## Contenus et brevets

* L'application **ne fournit aucun contenu ni abonnement IPTV** : l'utilisateur est responsable de disposer des droits sur les flux qu'il lit
  ou enregistre.
* La LGPL ne concède pas de licence sur les **brevets** de codecs (H.264, HEVC, AAC, etc. décodés via FFmpeg / libavcodec dans libVLC) :
  sans objet pour un usage personnel, à examiner avant toute distribution commerciale.

## Texte de la licence MIT

S'applique à CommunityToolkit.Mvvm (Copyright (c) .NET Foundation and Contributors),
à System.Security.Cryptography.ProtectedData, au runtime .NET et à WPF (Copyright (c) Microsoft Corporation / .NET Foundation and Contributors).

```
The MIT License (MIT)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
