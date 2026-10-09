# Verificacion install/desinstall (PS 5.1). Respeta otras instancias: solo toca el PID lanzado.
$unKey = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8A3F2B1C-7D4E-4A5F-9C6B-1E2F3A4B5C6D}_is1"
Write-Output "== Instalando (silencioso + icono escritorio) =="
& C:\tempanel\dist\TempPanel-Setup-x64.exe /SILENT /TASKS="desktopicon" | Out-Null
Start-Sleep -Seconds 8
$appDir = (Get-ItemProperty $unKey -ErrorAction SilentlyContinue).InstallLocation
$exe = Join-Path $appDir "TempPanel.exe"
Write-Output ("Dir: " + $appDir)
Write-Output ("Existe exe: " + (Test-Path $exe))
Write-Output ("Existe frontend: " + (Test-Path (Join-Path $appDir "Frontend\index.html")))
Write-Output ("Acceso escritorio: " + (Test-Path "$env:USERPROFILE\Desktop\TempPanel.lnk"))
Write-Output ("Acceso inicio: " + (Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\TempPanel\TempPanel.lnk"))

Write-Output "== Lanzando app instalada =="
$p = Start-Process $exe -PassThru
Start-Sleep -Seconds 10
$p.Refresh()
Write-Output ("En ejecucion (PID " + $p.Id + "): " + (-not $p.HasExited))
$w = (Get-Process -Id $p.Id -ErrorAction SilentlyContinue).MainWindowTitle
Write-Output ("Titulo ventana: " + $w)
$conns = @(Get-NetTCPConnection -OwningProcess $p.Id -ErrorAction SilentlyContinue | Where-Object { $_.State -eq "Established" })
Write-Output ("Conexiones TCP establecidas: " + $conns.Count)
Stop-Process -Id $p.Id -Force
Start-Sleep -Seconds 3
$still = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output ("Proceso instalado tras cerrar: " + (($still -ne $null)))

Write-Output "== Desinstalando (silencioso) =="
$unins = Join-Path $appDir "unins000.exe"
& $unins /SILENT | Out-Null
Start-Sleep -Seconds 10
Write-Output ("Dir tras desinstalar: " + (Test-Path $appDir))
Write-Output ("Registro tras desinstalar: " + (Test-Path $unKey))
Write-Output "FIN"
