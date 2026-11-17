# Génère les icônes de l'application Android depuis le dessin du logo (identique à assets\logo.svg) :
#   android\app\src\main\res\mipmap-*\ic_launcher.png   (lanceur, 48 -> 192 px)
#   android\app\src\main\res\drawable-xhdpi\tv_banner.png (bannière Android TV, 640x360 = 320x180 dp)
# Exécuter avec Windows PowerShell : powershell.exe -STA -File tools\make-android-icons.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$root = Split-Path $PSScriptRoot -Parent
$res = Join-Path $root 'android\app\src\main\res'

function Get-Color([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }

# Dessine le logo (repère 256x256) dans $dc à la position ($x,$y) et à la taille $size.
function Draw-Logo($dc, [double]$x, [double]$y, [double]$size) {
    $white = [System.Windows.Media.Brushes]::White
    $grad = New-Object System.Windows.Media.LinearGradientBrush((Get-Color '#6AA0FF'), (Get-Color '#3A4FE0'),
        (New-Object System.Windows.Point 0, 0), (New-Object System.Windows.Point 1, 1))
    $k = if ($size -le 64) { 1.3 } elseif ($size -le 100) { 1.1 } else { 1.0 }
    $pen = { param($w) $p = New-Object System.Windows.Media.Pen($white, $w * $k)
             $p.StartLineCap = 'Round'; $p.EndLineCap = 'Round'; $p.LineJoin = 'Round'; $p }

    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform($x, $y)))
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform(($size / 256.0), ($size / 256.0))))

    $dc.DrawRoundedRectangle($grad, $null, (New-Object System.Windows.Rect 8, 8, 240, 240), 58, 58)

    $v = New-Object System.Windows.Media.PathGeometry
    $fig = New-Object System.Windows.Media.PathFigure
    $fig.StartPoint = New-Object System.Windows.Point 92, 50
    $fig.Segments.Add((New-Object System.Windows.Media.LineSegment((New-Object System.Windows.Point 128, 84), $true)))
    $fig.Segments.Add((New-Object System.Windows.Media.LineSegment((New-Object System.Windows.Point 164, 50), $true)))
    $v.Figures.Add($fig)
    $dc.DrawGeometry($null, (& $pen 12), $v)

    $screenFill = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromArgb(36, 255, 255, 255))
    $dc.DrawRoundedRectangle($screenFill, (& $pen 12), (New-Object System.Windows.Rect 52, 84, 152, 118), 24, 24)

    $t = New-Object System.Windows.Media.PathGeometry
    $tf = New-Object System.Windows.Media.PathFigure
    $tf.StartPoint = New-Object System.Windows.Point 114, 118
    $tf.IsClosed = $true
    $tf.Segments.Add((New-Object System.Windows.Media.LineSegment((New-Object System.Windows.Point 114, 170), $true)))
    $tf.Segments.Add((New-Object System.Windows.Media.LineSegment((New-Object System.Windows.Point 158, 144), $true)))
    $t.Figures.Add($tf)
    $dc.DrawGeometry($white, (& $pen 10), $t)

    $dc.Pop(); $dc.Pop()
}

function Save-Png([System.Windows.Media.DrawingVisual]$dv, [int]$w, [int]$h, [string]$path) {
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($w, $h, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    $fs = [IO.File]::Create($path); $enc.Save($fs); $fs.Dispose()
}

# --- Lanceur
foreach ($d in @{ mdpi = 48; hdpi = 72; xhdpi = 96; xxhdpi = 144; xxxhdpi = 192 }.GetEnumerator()) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen(); Draw-Logo $dc 0 0 $d.Value; $dc.Close()
    Save-Png $dv $d.Value $d.Value (Join-Path $res "mipmap-$($d.Key)\ic_launcher.png")
}

# --- Bannière Android TV (640x360 px)
$dv = New-Object System.Windows.Media.DrawingVisual
$dc = $dv.RenderOpen()
$dc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush((Get-Color '#0E1117'))), $null, (New-Object System.Windows.Rect 0, 0, 640, 360))
Draw-Logo $dc 70 80 200
$face = New-Object System.Windows.Media.Typeface('Segoe UI Variable Text, Segoe UI')
$ft = New-Object System.Windows.Media.FormattedText('IPTV Player', [Globalization.CultureInfo]::InvariantCulture,
    [System.Windows.FlowDirection]::LeftToRight, $face, 56, [System.Windows.Media.Brushes]::White, 1.0)
$ft.SetFontWeight([System.Windows.FontWeights]::SemiBold)
$dc.DrawText($ft, (New-Object System.Windows.Point 295, 148))
$dc.Close()
Save-Png $dv 640 360 (Join-Path $res 'drawable-xhdpi\tv_banner.png')

"OK : mipmap-*/ic_launcher.png (5 densités) et drawable-xhdpi/tv_banner.png"
