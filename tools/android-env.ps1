# Prépare l'environnement Android pour la session PowerShell courante.
# Usage :  . .\tools\android-env.ps1        (note le point initial : « dot-sourcing »)
# Suppose : JDK 17 dans %USERPROFILE%\jdk-17 et SDK Android dans %USERPROFILE%\Android\Sdk.
$env:JAVA_HOME = Join-Path $env:USERPROFILE 'jdk-17'
$env:ANDROID_HOME = Join-Path $env:USERPROFILE 'Android\Sdk'
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
$env:Path = @(
    (Join-Path $env:JAVA_HOME 'bin'),
    (Join-Path $env:ANDROID_HOME 'platform-tools'),
    (Join-Path $env:ANDROID_HOME 'emulator'),
    (Join-Path $env:ANDROID_HOME 'cmdline-tools\latest\bin'),
    $env:Path
) -join ';'

# Fichier local indiquant le SDK à Gradle (non versionné).
$local = Join-Path (Split-Path $PSScriptRoot -Parent) 'android\local.properties'
"sdk.dir=" + ($env:ANDROID_HOME -replace '\\', '/') | Set-Content $local -Encoding ascii

"JAVA_HOME     = $env:JAVA_HOME"
"ANDROID_HOME  = $env:ANDROID_HOME"
