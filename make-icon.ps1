Add-Type -AssemblyName System.Drawing
$icoPath = "C:\tempanel\TempPanel\app.ico"
$size = 256
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
# Fondo oscuro redondeado
$g.Clear([System.Drawing.Color]::FromArgb(13,17,23))
$bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
  (New-Object System.Drawing.Point(0,0)),
  (New-Object System.Drawing.Point($size,$size)),
  [System.Drawing.Color]::FromArgb(20,30,48),
  [System.Drawing.Color]::FromArgb(10,15,25))
$g.FillRectangle($bgBrush, 0, 0, $size, $size)
# Termómetro estilizado: barra vertical con degradado azul->cian
$barX = 108; $barY = 40; $barW = 40; $barH = 130
$barBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
  (New-Object System.Drawing.Point(0,$barY)),
  (New-Object System.Drawing.Point(0,($barY+$barH))),
  [System.Drawing.Color]::FromArgb(47,123,255),
  [System.Drawing.Color]::FromArgb(34,211,238))
$rect = New-Object System.Drawing.Rectangle($barX, $barY, $barW, $barH)
# dibujar barra con esquinas redondeadas (aprox con rect + elipse)
$g.FillRectangle($barBrush, $rect)
# Bulbo inferior
$bulbBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(34,211,238))
$g.FillEllipse($bulbBrush, 88, 150, 80, 80)
$innerBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(13,17,23))
$g.FillEllipse($innerBrush, 104, 166, 48, 48)
$coreBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(34,211,238))
$g.FillEllipse($coreBrush, 114, 176, 28, 28)
# Marcas de grados
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(139,152,173), 6)
foreach ($y in @(60, 90, 120, 150)) { $g.DrawLine($pen, 160, $y, 185, $y) }
$g.Dispose()
# Guardar como icono (multi-resolución simplificada: 256px PNG dentro de ICO vía Bitmap->Icon)
$icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
$fs = [System.IO.File]::Create($icoPath)
$icon.Save($fs)
$fs.Close()
$bmp.Dispose()
Write-Output "Icono generado: $icoPath"
