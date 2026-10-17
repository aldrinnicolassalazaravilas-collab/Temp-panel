# Crea el Release v2.3.5 en GitHub y sube el instalador como asset.
# Usa la credencial ya guardada en el equipo (no pide ni muestra secretos).
# Ejecutar cuando dist\TempPanel-Setup-x64.exe sea el build v2.3.5 final.
$ErrorActionPreference = "Stop"

$owner = "aldrinnicolassalazaravilas-collab"
$repo = "Temp-panel"
$tag = "v2.3.5"
$asset = "C:\tempanel\dist\TempPanel-Setup-x64.exe"

if (-not (Test-Path $asset)) { Write-Output "FALTA asset: $asset"; exit 1 }

$fill = ("protocol=https`nhost=github.com`n`n" | git credential fill)
$token = ($fill | Where-Object { $_ -match '^password=' } | Select-Object -First 1) -replace '^password=',''
if (-not $token) { Write-Output "Sin credencial guardada"; exit 1 }

$H = @{ Authorization = "Bearer $token"; Accept = "application/vnd.github+json"; "X-GitHub-Api-Version" = "2022-11-28" }

# 1. Existe ya el release?
$rel = $null
try { $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$repo/releases/tags/$tag" -Headers $H -TimeoutSec 30 }
catch { $rel = $null }

if (-not $rel) {
  Write-Output "Creando release $tag..."
  $body = @{ tag_name = $tag; name = "TempPanel $tag"; draft = $false; prerelease = $false;
    body = "TempPanel ${tag}: monitor de hardware en tiempo real para Windows. Instalador autocontenido x64." } | ConvertTo-Json
  $rel = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$owner/$repo/releases" -Headers $H -Body $body -ContentType "application/json" -TimeoutSec 30
} else { Write-Output "Release $tag ya existe (id $($rel.id))" }

# 2. Subir asset (borra el anterior con mismo nombre si existe)
foreach ($a in @($rel.assets)) {
  if ($a.name -eq "TempPanel-Setup-x64.exe") {
    Write-Output "Borrando asset anterior..."
    Invoke-RestMethod -Method Delete -Uri "https://api.github.com/repos/$owner/$repo/releases/assets/$($a.id)" -Headers $H -TimeoutSec 30 | Out-Null
  }
}
$upUrl = ([string]$rel.upload_url).Split('{')[0]
$upUrl = "${upUrl}?name=TempPanel-Setup-x64.exe"
Write-Output "Subiendo instalador..."
Add-Type -AssemblyName System.Net.Http
$handler = New-Object System.Net.Http.HttpClientHandler
$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromMinutes(10)
$client.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $token)
$client.DefaultRequestHeaders.UserAgent.ParseAdd("TempPanel-release-script")
$client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json")
$fs = [IO.File]::OpenRead($asset)
$content = New-Object System.Net.Http.StreamContent($fs)
$content.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream")
$respMsg = $client.PostAsync($upUrl, $content).Result
$fs.Close(); $client.Dispose()
Write-Output ("Subida: " + [int]$respMsg.StatusCode)
if (-not $respMsg.IsSuccessStatusCode) { Write-Output $respMsg.Content.ReadAsStringAsync().Result; exit 1 }

# 3. Verificar releases/latest
$latest = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$repo/releases/latest" -Headers $H -TimeoutSec 30
Write-Output ("latest: " + $latest.tag_name)
foreach ($a in @($latest.assets)) { Write-Output ("asset: " + $a.name + " (" + $a.size + " bytes)") }
$token = $null
Write-Output "OK"
