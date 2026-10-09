# Build + Publish TempPanel (ejecutar cuando .NET SDK esté instalado)
# Uso: powershell -ExecutionPolicy Bypass -File C:\tempanel\build.ps1

$ErrorActionPreference = "Stop"
$proj = "C:\tempanel\TempPanel\TempPanel.csproj"
$publishDir = "C:\tempanel\TempPanel\bin\publish\win-x64"

Write-Host "== Restaurando dependencias ==" -ForegroundColor Cyan
dotnet restore $proj

Write-Host "== Compilando Release ==" -ForegroundColor Cyan
dotnet build $proj -c Release --no-restore

Write-Host "== Publicando autocontenido win-x64 (single-file) ==" -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:EnableWindowsTargeting=true `
  -o $publishDir

Write-Host ""
Write-Host "== Verificando artefactos ==" -ForegroundColor Cyan
Get-ChildItem $publishDir | Format-Table Name, Length
$exe = Join-Path $publishDir "TempPanel.exe"
if (Test-Path $exe) { Write-Host "OK: $exe" -ForegroundColor Green }
else { Write-Host "FALLO: no se generó TempPanel.exe" -ForegroundColor Red; exit 1 }
