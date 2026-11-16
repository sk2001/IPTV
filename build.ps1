# Génère dist\IptvPlayer.exe : un exécutable UNIQUE (runtime .NET + WPF + moteur VLC embarqués).
# Au premier lancement, VLC est extrait dans %LocalAppData%\IptvPlayer\libvlc-<version>.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$proj = Join-Path $PSScriptRoot 'src\IptvPlayer'
$dist = Join-Path $PSScriptRoot 'dist'

# 1) Localise le moteur VLC x64 du paquet NuGet (restauré au besoin).
dotnet restore $proj | Out-Null
$pkgRoot = Join-Path $env:USERPROFILE '.nuget\packages\videolan.libvlc.windows'
$ver = (Get-ChildItem $pkgRoot -Directory | Sort-Object { [version]($_.Name -replace '[^\d.].*$') } | Select-Object -Last 1).Name
$vlcDir = Join-Path $pkgRoot "$ver\build\x64"

# 2) Zip du moteur (sans les .lib inutiles à l'exécution).
$zip = Join-Path $proj 'obj\embed\libvlc.zip'
if (-not (Test-Path $zip)) {
    New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null
    Write-Host "Compression de VLC $ver ..."
    $fs = [System.IO.File]::Create($zip)
    $archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    $base = (Resolve-Path $vlcDir).Path.TrimEnd('\') + '\'
    Get-ChildItem $vlcDir -Recurse -File | Where-Object { $_.Extension -ne '.lib' } | ForEach-Object {
        $rel = $_.FullName.Substring($base.Length).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal)
    }
    $archive.Dispose(); $fs.Dispose()
}
$stamp = "$ver-$((Get-Item $zip).Length)"

# 3) Publication en fichier unique.
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true `
    -p:DebugType=none -p:EmbedVlc=true "-p:VlcZip=$zip" "-p:VlcStamp=$stamp" -o $dist
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué" }

$exe = Join-Path $dist 'IptvPlayer.exe'
"{0}`n  -> {1} ({2:N0} Mo) | fichiers dans dist : {3}" -f 'OK', $exe, ((Get-Item $exe).Length / 1MB), (Get-ChildItem $dist -Recurse -File).Count
