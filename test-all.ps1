$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    & ./check-tools.ps1
    & ./tools/node.exe --test tests/platforms.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Platform extractor tests failed' }
    & ./test.ps1
    & ./test-batch.ps1
    & ./test-codecs.ps1
    & ./test-ui.ps1
    Write-Output 'All platform, core, batch, codec and UI tests passed.'
} finally {
    Pop-Location
}
