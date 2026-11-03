param([string]$AppName = '拾链下载器.exe', [ValidateRange(1, 3600)][int]$TimeoutSeconds = 300)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
& ./build.ps1 -OutputName $AppName
$reportFile = Join-Path $PSScriptRoot 'test-output/ui-smoke-result.txt'
if (Test-Path -LiteralPath $reportFile) { Remove-Item -LiteralPath $reportFile }
$portFile = Join-Path $PSScriptRoot 'test-output/server-port.txt'
if (Test-Path -LiteralPath $portFile) { Remove-Item -LiteralPath $portFile }
$server = Start-Process -FilePath (Join-Path $PSScriptRoot 'tools/node.exe') -ArgumentList 'tests/server.js' -WindowStyle Hidden -PassThru
try {
    for ($i = 0; $i -lt 100 -and -not (Test-Path -LiteralPath $portFile); $i++) { Start-Sleep -Milliseconds 50 }
    $port = (Get-Content -LiteralPath $portFile -Raw).Trim()
    $app = Start-Process -FilePath (Join-Path $PSScriptRoot $AppName) -ArgumentList @('--smoke-test', "http://127.0.0.1:$port/manifest.mpd") -WindowStyle Hidden -PassThru
    if (-not $app.WaitForExit($TimeoutSeconds * 1000)) { Stop-Process -Id $app.Id; throw "GUI smoke test timed out after $TimeoutSeconds seconds" }
    Get-Content -LiteralPath $reportFile
    if ($app.ExitCode -ne 0) { throw 'GUI smoke test failed' }
} finally { if (-not $server.HasExited) { Stop-Process -Id $server.Id } }
