# Genera assets\pintop.ico (16, 24, 32, 48, 256 px) dibujando el glifo de chincheta.
# Solo hace falta volver a ejecutarlo si se cambia el diseño del icono.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'assets\pintop.ico'
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

$sizes = 16, 24, 32, 48, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 120, 215))
    $g.FillEllipse($bg, 0, 0, $s - 1, $s - 1)
    $font = New-Object System.Drawing.Font 'Segoe MDL2 Assets', ([float]($s * 0.62)), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString([string][char]0xE842, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, ([float]($s / 32)), $s, $s), $sf)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

# Formato ICO con imágenes PNG embebidas
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $pngs[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$d.Length); $w.Write([uint32]$offset)
    $offset += $d.Length
}
foreach ($d in $pngs) { $w.Write($d) }
$w.Close()
Write-Host "Icono generado: $out"
