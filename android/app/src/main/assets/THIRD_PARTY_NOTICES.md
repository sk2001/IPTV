# Third-party licenses and notices (Android / Android TV app)

The code of IPTV Player is under the **MIT license**. The app relies on the components below.

## Components shipped in the APK

| Component | Version | License | Role |
|---|---|---|---|
| **libVLC for Android** (rebuilt from `libvlcjni` 3.x sources: `libvlc.so`, `libvlcjni.so`) — VideoLAN / VLC Authors | engine 3.0.x | **LGPL** (VLC: 2.1+; contribs: LGPLv3) — see the section below | Playback and decoding |
| **libc++_shared** — LLVM Project (bundled with libVLC) | — | Apache-2.0 with LLVM exceptions | C++ runtime |
| **AndroidX** (core, activity, lifecycle, appcompat) — Google | see `gradle/libs.versions.toml` | Apache-2.0 | Platform libraries |
| **Jetpack Compose**, **Material 3**, **Material Icons Extended**, **TV Material** — Google | BOM 2025.10.01 / TV 1.1.0 | Apache-2.0 | User interface |
| **Kotlin** standard library, **kotlinx.coroutines**, **kotlinx.serialization** — JetBrains | 2.4.21 / 1.11.0 / 1.13.0 | Apache-2.0 | Language runtime, JSON |
| **Coil 3** (`coil-compose`, `coil-network-okhttp`) | 3.3.0 | Apache-2.0 | Image loading |
| **ZXing** (`core`, `zxing-android-embedded`) — ZXing authors / JourneyApps | 3.5.3 / 4.3.0 | Apache-2.0 | QR code generation and scanning (pairing) |
| **OkHttp** and **Okio** — Square (pulled in by Coil) | 5.x | Apache-2.0 | HTTP |

The **Apache-2.0** text is at <https://www.apache.org/licenses/LICENSE-2.0>; the **LGPL-2.1** text is in `LICENSES/LGPL-2.1.txt` of the project.

## libVLC for Android: rebuilt without GPL code

The official `libvlc-all` package is built by VideoLAN with **GPLv3 contribs by default** (inspection of its `libvlc.so` found the x264 H.264 encoder, GPL-2.0-or-later).
IPTV Player therefore **does not use the official binary** when built with the project's script: it ships libVLC **rebuilt from VideoLAN's sources with the LGPL contrib set**
(`--license l`: LGPLv3 + ad-clauses; x264, libdvdnav and other GPL-only components are absent, GnuTLS is kept for https).
The rebuilt library as a whole is therefore distributed under the **LGPL (version 3, or 2.1+ for VLC's own code)**; the project's MIT code only links to it dynamically.

* Build: `tools/build-libvlc-lgpl.sh` (Linux / WSL, NDK r27c). Java sources and exact commits: `android/libvlc-lgpl/` (`LIBVLCJNI_COMMIT`, `VLC_COMMIT`).
* The native libraries are not stored in the repository: build them with the script before `gradlew assembleRelease`. Without them the build falls back to the official AAR (**for private use only**).
* Architectures built: arm64-v8a and x86_64 (32-bit ARM needs NDK r21 and is not built).

## LGPL-2.1 obligations (libVLC)

1. License text: `LICENSES/LGPL-2.1.txt`.
2. libVLC is a separate shared library (`libvlc.so`, `libvlcjni.so`) inside the APK. To use a modified libVLC, rebuild it with `tools/build-libvlc-lgpl.sh` (or drop your own `.so` files in `android/libvlc-lgpl/jniLibs/<abi>/`) and run `gradlew assembleRelease`. The source code of this project is public.
3. Sources of libVLC / VLC: <https://www.videolan.org/vlc/> · <https://code.videolan.org/videolan/libvlcjni> · <https://code.videolan.org/videolan/vlc>.
4. Any distribution of the APK must be accompanied by these notices and a way to obtain the source code of this project.

## Trademarks, content and patents

* “VLC” and the cone are trademarks of VideoLAN. IPTV Player is neither affiliated with nor endorsed by VideoLAN.
* The application **provides no content or IPTV subscription**: the user is responsible for having the rights to the streams played.
* The LGPL grants no **patent** license for codecs (H.264, HEVC, AAC…): to be reviewed before any commercial distribution.
