param([string]$OutDir = (Join-Path $PSScriptRoot '..\Textures\UI'))

# Draws the loadout list's New, Copy, Load and Save icons: white on transparent, 64x64, tinted in game.
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

function New-Icon([string]$Name, [scriptblock]$Draw) {
  $bmp = New-Object System.Drawing.Bitmap 64, 64, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)
  & $Draw $g
  $g.Dispose()
  $bmp.Save((Join-Path $OutDir "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
}

function Poly([float[]]$xy) {
  $points = for ($i = 0; $i -lt $xy.Count; $i += 2) { New-Object System.Drawing.PointF $xy[$i], $xy[$i + 1] }
  , [System.Drawing.PointF[]]$points
}

# Fills the shape, then punches transparent holes with SourceCopy.
function Punch($g, [float]$x, [float]$y, [float]$w, [float]$h) {
  $g.CompositingMode = 'SourceCopy'
  $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)), $x, $y, $w, $h)
  $g.CompositingMode = 'SourceOver'
}

$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

New-Icon 'BetterCELoadouts_New' {
  param($g)
  $g.FillRectangle($white, 26, 8, 12, 48)
  $g.FillRectangle($white, 8, 26, 48, 12)
}

New-Icon 'BetterCELoadouts_Copy' {
  param($g)
  $g.FillRectangle($white, 20, 4, 38, 40)
  Punch $g 4 18 42 44
  $g.FillRectangle($white, 8, 22, 34, 36)
}

New-Icon 'BetterCELoadouts_Load' {
  param($g)
  $g.FillPolygon($white, (Poly 4, 12, 24, 12, 30, 20, 60, 20, 60, 54, 4, 54))
  Punch $g 4 26 56 3
}

New-Icon 'BetterCELoadouts_Save' {
  param($g)
  $g.FillPolygon($white, (Poly 6, 6, 48, 6, 58, 16, 58, 58, 6, 58))
  Punch $g 18 6 26 16
  $g.FillRectangle($white, 34, 9, 6, 10)
  Punch $g 14 32 36 20
}
