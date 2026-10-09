# Diagnostico: invoca OK y comprueba ventana vs proceso.
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Start-Process "C:\tempanel\TempPanel\bin\publish\win-x64\TempPanel.exe" -ArgumentList "--test-battery-alert" -PassThru
Start-Sleep -Milliseconds 2500

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

$win = Find-TestWin
Write-Output ("Ventana antes de OK: " + ($win -ne $null))
$okBtn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
  (New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "OK")))
Write-Output ("Boton enabled: " + $okBtn.Current.IsEnabled)
$okBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
for ($i = 0; $i -lt 6; $i++) {
  Start-Sleep -Milliseconds 1000
  $w2 = Find-TestWin
  $proc.Refresh()
  Write-Output ("t+" + (($i+1)) + "s ventana=" + ($w2 -ne $null) + " exited=" + $proc.HasExited)
  if ($w2 -eq $null) { break }
}
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Write-Output "limpieza: proceso detenido" }
