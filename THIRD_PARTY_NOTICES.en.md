# Third-party licenses and notices

IPTV Player relies on the components below. This file (together with `LICENSES/`) is also available in the application:
**Settings → About → Component licenses**.

> The code of this project is under the **MIT license** (`LICENSE` file). It is compatible with every license listed below:
> the LGPL allows an application under another license to use libVLC / LibVLCSharp as dynamic libraries,
> provided the obligations listed further down are met.

## Components shipped in `IptvPlayer.exe`

| Component | Version | License | Role |
|---|---|---|---|
| **libVLC** (libvlc, libvlccore + plugins) — VideoLAN / VLC Authors | 3.0.24 | **LGPL-2.1-or-later** (LGPL plugins only, see below) | Playback, decoding, stream output (recording) |
| **LibVLCSharp** and **LibVLCSharp.WPF** — VideoLAN | 3.10.2 | **LGPL-2.1-or-later** | .NET interface to libVLC, WPF video control |
| **CommunityToolkit.Mvvm** — .NET Foundation and Contributors | 8.4.2 | MIT | MVVM (observable properties, commands) |
| **System.Security.Cryptography.ProtectedData** — Microsoft | 9.0.0 | MIT | DPAPI encryption of passwords |
| **.NET 9 runtime** and **WPF** (Microsoft.NETCore.App, Microsoft.WindowsDesktop.App) — Microsoft | 9.0.x | MIT | Self-contained runtime included in the exe |

The .NET runtime ships native Microsoft binaries (`D3DCompiler_47`, `vcruntime140`, `wpfgfx`, `PresentationNative`, `PenImc`) distributed
as-is in the official runtime pack; redistributing them with a self-contained application is supported by Microsoft.
The runtime's third-party notices are reproduced in `LICENSES/DOTNET-THIRD-PARTY-NOTICES.txt`.

## Items not shipped

* **Segoe Fluent Icons / Segoe UI Variable** (Windows fonts): used if installed on the machine, **not redistributed**.
* **Build tools** (.NET SDK, ILLink, Crossgen2, PowerShell): used only to build; nothing of them is shipped other than the runtime above.
* **Logo and icon**: original work of the project (`assets/logo.svg`, `tools/make-icon.ps1`). The VLC cone is not used.

## VLC engine: LGPL plugins only

The `VideoLAN.LibVLC.Windows` NuGet package contains 321 plugins: **317 are LGPL-2.1+**, **4 are GPL-2+** (`libx26410b` — x264 encoder,
`liblua`, `libdolby_surround_decoder`, `libheadphone_channel_mixer`). So that the distribution only contains LGPL code, `build.ps1`
**automatically excludes** any plugin whose declared license is not the LGPL (VLC writes each plugin's license in plain text inside the binary),
as well as the `lua\` folder and the development headers. The application needs none of them (no transcoding, playlists are parsed by the application itself).

## LGPL-2.1 obligations (libVLC, LibVLCSharp)

1. **License text**: `LICENSES/LGPL-2.1.txt`, also visible in the application.
2. **Replacing the library**: libVLC is a separate dynamic library (DLL). To use your own version, put a `libvlc` folder
   (containing `libvlc.dll`, `libvlccore.dll` and `plugins\`) **next to `IptvPlayer.exe`**: it takes precedence over the embedded engine.
   The embedded engine is extracted to `%LocalAppData%\IptvPlayer\libvlc-<version>` and can also be replaced there.
3. **LibVLCSharp**: the source code of this project is available with its build script (`build.ps1`); change the version of the
   `LibVLCSharp.WPF` package in `src/IptvPlayer/IptvPlayer.csproj` (or replace it with a modified version) and rebuild.
4. **Sources** of VLC / libVLC: <https://www.videolan.org/vlc/> (archives: <https://download.videolan.org/pub/videolan/vlc/>);
   NuGet package: <https://code.videolan.org/videolan/libvlc-nuget>; LibVLCSharp: <https://code.videolan.org/videolan/LibVLCSharp>.
5. Any distribution of the binary must be accompanied by these notices and a way to obtain the source code of this project.

## Trademarks

“VLC” and the cone are trademarks of VideoLAN. IPTV Player is neither affiliated with nor endorsed by VideoLAN;
“VLC” is only mentioned to describe the playback engine in use.

## Content and patents

* The application **provides no content or IPTV subscription**: the user is responsible for having the rights to the streams played or recorded.
* The LGPL grants no **patent** license for codecs (H.264, HEVC, AAC, etc. decoded through FFmpeg / libavcodec in libVLC):
  irrelevant for personal use, to be reviewed before any commercial distribution.

## MIT license text

Applies to CommunityToolkit.Mvvm (Copyright (c) .NET Foundation and Contributors),
System.Security.Cryptography.ProtectedData, the .NET runtime and WPF (Copyright (c) Microsoft Corporation / .NET Foundation and Contributors).

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
