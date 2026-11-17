<#
.SYNOPSIS
    Génère les applications du dépôt : Windows (desktop) et/ou Android.

.DESCRIPTION
    ./build.ps1                         tout : desktop + android
    ./build.ps1 desktop                 seulement Windows  -> dist\desktop\IptvPlayer.exe (exécutable UNIQUE : runtime .NET + WPF + VLC embarqués)
    ./build.ps1 android                 seulement Android  -> dist\android\*.apk (un APK par architecture + un universel)
    ./build.ps1 android -AndroidVariant debug|release|auto
    ./build.ps1 -Output D:\sortie       dossier de sortie (par défaut : dist)

    Android : « auto » (défaut) compile en release si android\keystore.properties existe (APK signé), sinon en debug (signé avec la clé de débogage).
    Un échec d'une cible n'empêche pas les autres ; le code de sortie est 1 si l'une d'elles a échoué.
    Au premier lancement de l'exe Windows, VLC est extrait dans %LocalAppData%\IptvPlayer\libvlc-<version>.
#>
param(
    [Parameter(Position = 0)][ValidateSet('all', 'desktop', 'android')][string]$Target = 'all',
    [ValidateSet('auto', 'debug', 'release')][string]$AndroidVariant = 'auto',
    [string]$Output = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root = $PSScriptRoot
$dist = if ($Output) { $Output } else { Join-Path $root 'dist' }

# ---------------------------------------------------------------------------------------------- Windows (desktop)

function Build-Desktop {
    $proj = Join-Path $root 'desktop'
    $out = Join-Path $dist 'desktop'

    # 1) Localise le moteur VLC x64 du paquet NuGet (restauré au besoin).
    dotnet restore $proj | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore a échoué' }
    $pkgRoot = Join-Path $env:USERPROFILE '.nuget\packages\videolan.libvlc.windows'
    $ver = (Get-ChildItem $pkgRoot -Directory | Sort-Object { [version]($_.Name -replace '[^\d.].*$') } | Select-Object -Last 1).Name
    $vlcDir = Join-Path $pkgRoot "$ver\build\x64"

    # 2) Zip du moteur VLC, en ne gardant que du LGPL (voir THIRD_PARTY_NOTICES.md) :
    #    - plugins dont la licence déclarée n'est pas la LGPL (ex. x264, GPL-2+) exclus automatiquement ;
    #    - dossier lua\ (scripts et interface web de VLC, inutiles ici), en-têtes include\ et .lib exclus.
    $zip = Join-Path $proj 'obj\embed\libvlc-lgpl.zip'
    if (-not (Test-Path $zip)) {
        New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null
        Write-Host "Compression de VLC $ver (plugins LGPL uniquement) ..."
        $fs = [System.IO.File]::Create($zip)
        $archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
        $base = (Resolve-Path $vlcDir).Path.TrimEnd('\') + '\'
        $excluded = New-Object System.Collections.Generic.List[string]
        $files = Get-ChildItem $vlcDir -Recurse -File | Where-Object { $_.Extension -ne '.lib' -and $_.FullName -notmatch '\\(include|lua)\\' }
        foreach ($f in $files) {
            if ($f.Extension -eq '.dll' -and $f.FullName -match '\\plugins\\') {
                # VLC écrit la licence de chaque plugin en toutes lettres dans le binaire.
                $txt = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($f.FullName))
                if ($txt -notmatch 'GNU Lesser General Public License') { $excluded.Add($f.Name); continue }
            }
            $rel = $f.FullName.Substring($base.Length).Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal)
        }
        $archive.Dispose(); $fs.Dispose()
        Write-Host ("Plugins exclus (non LGPL) : " + $(if ($excluded.Count) { $excluded -join ', ' } else { 'aucun' }))
    }
    $stamp = "$ver-$((Get-Item $zip).Length)"

    # 3) Publication en fichier unique.
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    dotnet publish $proj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true `
        -p:DebugType=none -p:EmbedVlc=true "-p:VlcZip=$zip" "-p:VlcStamp=$stamp" -o $out | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish a échoué' }

    $exe = Join-Path $out 'IptvPlayer.exe'
    return ('{0} ({1:N0} Mo)' -f $exe, ((Get-Item $exe).Length / 1MB))
}

# ---------------------------------------------------------------------------------------------- Android

function Build-Android {
    $android = Join-Path $root 'android'
    $out = Join-Path $dist 'android'
    . (Join-Path $root 'tools\android-env.ps1') | Out-Null          # JAVA_HOME, ANDROID_HOME, PATH, android\local.properties

    $hasKey = Test-Path (Join-Path $android 'keystore.properties')
    $variant = if ($AndroidVariant -eq 'auto') { if ($hasKey) { 'release' } else { 'debug' } } else { $AndroidVariant }
    if ($variant -eq 'release' -and -not $hasKey) {
        throw 'Un APK release doit être signé : créer android\keystore.properties (voir android\README.md, §4) ou utiliser -AndroidVariant debug.'
    }
    if ($AndroidVariant -eq 'auto' -and -not $hasKey) { Write-Host 'Android : pas de android\keystore.properties -> APK de test (debug).' }
    if (-not (Test-Path (Join-Path $android 'libvlc-lgpl\jniLibs'))) {
        Write-Warning 'libVLC LGPL absente (android\libvlc-lgpl\jniLibs) : le paquet officiel (GPL) est utilisé, pour un usage privé uniquement. Voir android\README.md, §5.'
    }

    $task = 'assemble' + $variant.Substring(0, 1).ToUpper() + $variant.Substring(1)
    Push-Location $android
    try {
        & .\gradlew.bat $task --console=plain -q | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "gradlew $task a échoué" }
    }
    finally { Pop-Location }

    New-Item -ItemType Directory -Force $out | Out-Null
    Get-ChildItem $out -Filter *.apk -ErrorAction SilentlyContinue | Remove-Item -Force
    $apks = Get-ChildItem (Join-Path $android "app\build\outputs\apk\$variant") -Filter *.apk
    if (-not $apks) { throw 'Aucun APK produit.' }
    $apks | Copy-Item -Destination $out
    return ($apks | ForEach-Object { '{0} ({1:N0} Mo)' -f (Join-Path $out $_.Name), ($_.Length / 1MB) }) -join "`n     "
}

# ---------------------------------------------------------------------------------------------- Exécution

$targets = if ($Target -eq 'all') { @('desktop', 'android') } else { @($Target) }
$results = [ordered]@{}
foreach ($t in $targets) {
    Write-Host "`n=== $t ===" -ForegroundColor Cyan
    try { $results[$t] = @{ Ok = $true; Text = (& "Build-$((Get-Culture).TextInfo.ToTitleCase($t))") } }
    catch { $results[$t] = @{ Ok = $false; Text = $_.Exception.Message } }
}

Write-Host "`n=== Résumé ===" -ForegroundColor Cyan
$failed = 0
foreach ($t in $results.Keys) {
    if ($results[$t].Ok) { Write-Host ("OK     {0}`n     {1}" -f $t, $results[$t].Text) -ForegroundColor Green }
    else { $failed++; Write-Host ("ÉCHEC  {0} : {1}" -f $t, $results[$t].Text) -ForegroundColor Red }
}
if ($failed -gt 0) { exit 1 }
