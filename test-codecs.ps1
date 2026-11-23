$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /codepage:65001 /r:System.Web.Extensions.dll ('/out:' + (Join-Path $PSScriptRoot 'test-output/codec-tests.exe')) (Join-Path $PSScriptRoot 'src/Core.cs') (Join-Path $PSScriptRoot 'src/Batch.cs') (Join-Path $PSScriptRoot 'tests/CodecTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Codec tests compilation failed' }
& ./test-output/codec-tests.exe
if ($LASTEXITCODE -ne 0) { throw 'Codec tests failed' }
