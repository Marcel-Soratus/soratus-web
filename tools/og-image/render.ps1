# Render og-image.png from og.html via headless Chrome.
# Output: Soratus.Web/wwwroot/brand/og-image.png (1200x630)
#
# Usage (from repo root or this folder):
#   pwsh tools/og-image/render.ps1

$ErrorActionPreference = "Stop"

$here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$html  = Join-Path $here 'og.html'
$out   = Join-Path $here '..\..\Soratus.Web\wwwroot\brand\og-image.png'
$out   = [System.IO.Path]::GetFullPath($out)

$chrome = @(
  "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
  "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $chrome) { throw "No headless Chrome/Edge found." }

Write-Host "Rendering $html -> $out"
& $chrome `
  --headless=new `
  --disable-gpu `
  --hide-scrollbars `
  --window-size=1200,630 `
  --virtual-time-budget=4000 `
  "--screenshot=$out" `
  "file:///$($html -replace '\\','/')"

if (-not (Test-Path $out)) { throw "Screenshot failed." }
Write-Host "OK: $out ($((Get-Item $out).Length) bytes)"
