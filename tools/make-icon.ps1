# Génère l'icône de l'application depuis la géométrie du logo (identique à assets\logo.svg).
#   assets\logo.png                        (512 px, README)
#   desktop\Assets\app.ico          (16 -> 256 px, icône de l'exe et des fenêtres)
#   desktop\Assets\logo.png         (128 px, affichage dans l'interface)
# Exécuter avec Windows PowerShell : powershell.exe -STA -File tools\make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$root = Split-Path $PSScriptRoot -Parent
$assetsApp = Join-Path $root 'desktop\Assets'
New-Item -ItemType Directory -Force $assetsApp, (Join-Path $root 'assets') | Out-Null

function Get-Color([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }

function Render-Logo([int]$size) {
    $white = [System.Windows.Media.Brushes]::White
    $grad = New-Object System.Windows.Media.LinearGradientBrush((Get-Color '#6AA0FF'), (Get-Color '#3A4FE0'),
        (New-Object System.Windows.Point 0, 0), (New-Object System.Windows.Point 1, 1))

    # Petites tailles : traits un peu plus épais pour rester lisibles.
    $k = if ($size -le 32) { 1.35 } elseif ($size -le 48) { 1.15 } else { 1.0 }

    $pen = { param($w) $p = New-Object System.Windows.Media.Pen($white, $w * $k)
             $p.StartLineCap = 'Round'; $p.EndLineCap = 'Round'; $p.LineJoin = 'Round'; $p }

    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform(($size / 256.0), ($size / 256.0))))

    # Fond arrondi
    $dc.DrawRoundedRectangle($grad, $null, (New-Object System.Windows.Rect 8, 8, 240, 240), 58, 58)

    # Antenne en V
    $v = New-Object System.Windows.Media.PathGeometry
    $fig = New-Object System.Windows.Media.PathFigure
    $fig.StartPoint = New-Object System.Windows.Point 92, 50
    $fig.Segments.Add((New-Object System.Windows.Media.LineSegment((New-Object System.Windows.Point 128, 84), $true)))
    $fig.Segments.Add((New-Object System.Windows.Media.LineSegment((New-Object System.Windows.Point 164, 50), $true)))
    $v.Figures.Add($fig)
    $dc.DrawGeometry($null, (& $pen 12), $v)

    # Écran
    $screenFill = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromArgb(36, 255, 255, 255))
    $dc.DrawRoundedRectangle($screenFill, (& $pen 12), (New-Object System.Windows.Rect 52, 84, 152, 118), 24, 24)

    # Bouton lecture
    $t = New-Object System.Windows.Media.PathGeometry
    $tf = New-Object System.Windows.Media.PathFigure
    $tf.StartPoint = New-Object System.Windows.Point 114, 118
    $tf.IsClosed = $true
    $tf.Segments.Add((New-Object System.Windows.Media.LineSegment((New-Object System.Windows.Point 114, 170), $true)))
    $tf.Segments.Add((New-Object System.Windows.Media.LineSegment((New-Object System.Windows.Point 158, 144), $true)))
    $t.Figures.Add($tf)
    $dc.DrawGeometry($white, (& $pen 10), $t)

    $dc.Pop()
    $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    , $ms.ToArray()
}

# PNG
[IO.File]::WriteAllBytes((Join-Path $root 'assets\logo.png'), (Render-Logo 512))
[IO.File]::WriteAllBytes((Join-Path $assetsApp 'logo.png'), (Render-Logo 128))

# ICO (entrées PNG)
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (Render-Logo $s) }
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s })); $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $assetsApp 'app.ico'), $ms.ToArray())

"OK : assets\logo.png, desktop\Assets\app.ico ({0} Ko), desktop\Assets\logo.png" -f [math]::Round((Get-Item (Join-Path $assetsApp 'app.ico')).Length / 1KB)
