param([switch]$VerifyHashes)
$ErrorActionPreference = 'Stop'
$toolsDirectory = Join-Path $PSScriptRoot 'tools'
foreach ($name in @('yt-dlp.exe', 'ffmpeg.exe', 'ffprobe.exe', 'node.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $toolsDirectory $name) -PathType Leaf)) {
        throw "Missing tools/$name. See tools/README.md."
    }
}
if ($VerifyHashes) {
    $count = 0
    foreach ($line in Get-Content -LiteralPath (Join-Path $toolsDirectory 'SHA256SUMS.txt')) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^([0-9a-fA-F]{64})\s+([^\\/:]+)$') {
            throw 'Invalid entry in tools/SHA256SUMS.txt.'
        }
        $expected = $Matches[1]
        $name = $Matches[2]
        $actual = (Get-FileHash -LiteralPath (Join-Path $toolsDirectory $name) -Algorithm SHA256).Hash
        if ($actual -ne $expected) { throw "SHA-256 mismatch: tools/$name" }
        $count++
    }
    if ($count -eq 0) { throw 'Empty tools/SHA256SUMS.txt.' }
    Write-Output "SHA-256 verified: $count files."
}
Write-Output 'Runtime tools are present.'
