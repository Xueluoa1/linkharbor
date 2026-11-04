param([string]$Bundle = (Join-Path $PSScriptRoot 'dist/LinkHarbor-v1.0.1-Windows-x64.exe'))
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path $PSScriptRoot ('build/singlefile-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$exe = Join-Path $testRoot '拾链完整程序.exe'
Copy-Item -LiteralPath $Bundle -Destination $exe
$runtime = Join-Path $testRoot '拾链-v1.0.1'
function Expand-Bundle {
    $process = Start-Process -FilePath $exe -ArgumentList '--extract-only' -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(120000)) { Stop-Process -Id $process.Id; throw 'Bundle extraction timed out.' }
    if ($process.ExitCode -ne 0) { throw "Bundle failed: $($process.ExitCode)" }
}
function Assert-Runtime {
    foreach ($line in Get-Content -LiteralPath (Join-Path $runtime 'tools/SHA256SUMS.txt')) {
        if ($line -match '^([a-fA-F0-9]{64})\s+([^\\/:]+)$') {
            $expected = $Matches[1]
            $actual = (Get-FileHash -LiteralPath (Join-Path $runtime ('tools/' + $Matches[2])) -Algorithm SHA256).Hash
            if ($actual -ne $expected) { throw 'Extracted dependency hash mismatch.' }
        }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $runtime '拾链下载器.exe')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot '拾链下载器.exe')).Hash) { throw 'Application hash mismatch.' }
    foreach ($file in @('LICENSE', 'AUTHOR_NOTICE.md', 'THIRD_PARTY_NOTICES.txt', 'docs/使用教程.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $runtime $file))) { throw "Missing bundled document: $file" }
    }
}
Expand-Bundle
Assert-Runtime
$settings = Join-Path $runtime 'settings.json'
[IO.File]::WriteAllText($settings, '{}')
New-Item -ItemType Directory -Path (Join-Path $runtime '下载') | Out-Null
$personalFile = Join-Path $runtime '下载/用户文件.txt'
[IO.File]::WriteAllText($personalFile, 'preserve-user-data')
# A subsequent start must repair a corrupted component and preserve user data.
[IO.File]::WriteAllText((Join-Path $runtime 'tools/node.exe'), 'damaged')
Remove-Item -LiteralPath (Join-Path $runtime 'tools/ffprobe.exe')
Expand-Bundle
Assert-Runtime
if ([IO.File]::ReadAllText($settings) -ne '{}' -or [IO.File]::ReadAllText($personalFile) -ne 'preserve-user-data') { throw 'User data was changed.' }
$before = (Get-Item -LiteralPath (Join-Path $runtime 'tools/node.exe')).LastWriteTimeUtc
Expand-Bundle
if ((Get-Item -LiteralPath (Join-Path $runtime 'tools/node.exe')).LastWriteTimeUtc -ne $before) { throw 'Healthy component was unnecessarily replaced.' }
Write-Output "PASS: standalone extraction, all dependency hashes, repair, repeat startup, user data preservation. Runtime: $runtime"
