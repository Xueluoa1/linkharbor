$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /codepage:65001 /r:System.Web.Extensions.dll ('/out:' + (Join-Path $PSScriptRoot 'test-output/batch-tests.exe')) (Join-Path $PSScriptRoot 'src/Core.cs') (Join-Path $PSScriptRoot 'src/Batch.cs') (Join-Path $PSScriptRoot 'tests/BatchTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Batch test compilation failed' }
$portFile = Join-Path $PSScriptRoot 'test-output/server-port.txt'
if (Test-Path -LiteralPath $portFile) { Remove-Item -LiteralPath $portFile }
$server = Start-Process -FilePath (Join-Path $PSScriptRoot 'tools/node.exe') -ArgumentList 'tests/server.js' -WindowStyle Hidden -PassThru
try {
    for ($i = 0; $i -lt 100 -and -not (Test-Path -LiteralPath $portFile); $i++) { Start-Sleep -Milliseconds 50 }
    $port = (Get-Content -LiteralPath $portFile -Raw).Trim()
    & ./test-output/batch-tests.exe "http://127.0.0.1:$port"
    if ($LASTEXITCODE -ne 0) { throw 'Batch tests failed' }
} finally { if (-not $server.HasExited) { Stop-Process -Id $server.Id } }
