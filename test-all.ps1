$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    & ./check-tools.ps1
    & ./test.ps1
    & ./test-batch.ps1
    & ./test-codecs.ps1
    & ./test-ui.ps1
    Write-Output 'All core, batch, codec and UI tests passed.'
} finally {
    Pop-Location
}
