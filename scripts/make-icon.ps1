# Generates the placeholder 1024x1024 app icon (opaque PNG, as App Store/iOS require).
# Windows PowerShell 5.1 (System.Drawing). Re-run after changing colours.
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "App\Assets.xcassets\AppIcon.appiconset\AppIcon.png"
$size = 1024
$bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

# Dusk sky
$rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
$sky = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(34, 30, 44)), ([System.Drawing.Color]::FromArgb(196, 110, 52)), 90
$g.FillRectangle($sky, $rect)

# Rising sun
$sun = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(250, 204, 112))
$g.FillEllipse($sun, 312, 330, 400, 400)

# Hills (far and near)
$far = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(92, 72, 70))
$g.FillPolygon($far, [System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point 0, 640), (New-Object System.Drawing.Point 260, 520),
    (New-Object System.Drawing.Point 520, 610), (New-Object System.Drawing.Point 780, 500),
    (New-Object System.Drawing.Point 1024, 600), (New-Object System.Drawing.Point 1024, 1024),
    (New-Object System.Drawing.Point 0, 1024)))
$near = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(58, 78, 52))
$g.FillPolygon($near, [System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point 0, 760), (New-Object System.Drawing.Point 512, 650),
    (New-Object System.Drawing.Point 1024, 760), (New-Object System.Drawing.Point 1024, 1024),
    (New-Object System.Drawing.Point 0, 1024)))

# River
$river = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 150, 190))
$g.FillPolygon($river, [System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point 500, 680), (New-Object System.Drawing.Point 540, 680),
    (New-Object System.Drawing.Point 700, 1024), (New-Object System.Drawing.Point 560, 1024)))

# Campfire
$flame = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 120, 40))
$g.FillPolygon($flame, [System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point 330, 860), (New-Object System.Drawing.Point 370, 730),
    (New-Object System.Drawing.Point 410, 860)))
$core = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 214, 110))
$g.FillPolygon($core, [System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point 350, 860), (New-Object System.Drawing.Point 372, 790),
    (New-Object System.Drawing.Point 392, 860)))

$g.Dispose()
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "Icon written to $out"
