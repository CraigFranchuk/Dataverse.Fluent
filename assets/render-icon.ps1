# Renders assets/icon.svg to assets/icon.png (128x128, the size nuget.org recommends) with headless Chrome or Edge.
$ErrorActionPreference = 'Stop'

$svg = Join-Path $PSScriptRoot 'icon.svg'
$png = Join-Path $PSScriptRoot 'icon.png'

$browser = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $browser) { throw 'Chrome or Edge is required to render the icon.' }

# The SVG is 512 units square; scaling it to 25% gives a crisp 128px render.
$html = Join-Path ([IO.Path]::GetTempPath()) 'fluent-dataverse-icon.html'
$uri = ([Uri]$svg).AbsoluteUri
Set-Content $html "<html><body style='margin:0;background:transparent'><img src='$uri' width='128' height='128'></body></html>"

& $browser --headless --disable-gpu --hide-scrollbars --default-background-color=00000000 `
    --window-size=128,128 "--screenshot=$png" ([Uri]$html).AbsoluteUri 2>$null | Out-Null
Remove-Item $html

if (-not (Test-Path $png)) { throw 'Rendering failed.' }
Write-Host "Wrote $png"
