# Prueba 12.8 robusta: espera ventana (20 s), invoca OK, espera cierre (10 s).
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Start-Process "C:\tempanel\TempPanel\bin\publish\win-x64\TempPanel.exe" -ArgumentList "--test-battery-alert" -PassThru
Write-Output ("PID popup: " + $proc.Id)

function Find-TestWin() {
  $kids = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.Condition]::TrueCondition)
  foreach ($k in $kids) {
    try { $n = $k.Current.Name } catch { $n = "" }
    if ($n -like "TempPanel*Prueba*") { return $k }
  }
  return $null
}

$win = $null
for ($i = 0; $i -lt 40 -and $win -eq $null; $i++) { Start-Sleep -Milliseconds 500; $win = Find-TestWin }
if ($win -eq $null) { Write-Output "FALLO: no aparecio la ventana"; Stop-Process -Id $proc.Id -Force; exit 1 }
Write-Output ("OK: ventana encontrada: " + $win.Current.Name)

$okBtn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
  (New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "OK")))
if ($okBtn -eq $null) { Write-Output "FALLO: boton OK no encontrado"; Stop-Process -Id $proc.Id -Force; exit 1 }
$okBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "OK: clic en OK invocado"

$winGone = $false
for ($i = 0; $i -lt 20; $i++) {
  Start-Sleep -Milliseconds 500
  $proc.Refresh()
  if ((Find-TestWin) -eq $null) { $winGone = $true; break }
}
Write-Output ("Ventana cerrada tras OK: " + $winGone)
for ($i = 0; $i -lt 10 -and -not $proc.HasExited; $i++) { Start-Sleep -Milliseconds 500; $proc.Refresh() }
Write-Output ("Proceso terminado: " + $proc.HasExited)
if ($winGone -and $proc.HasExited) { Write-Output "PRUEBA SUPERADA"; exit 0 }
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
Write-Output "PRUEBA FALLIDA"
exit 1
