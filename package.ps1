param([switch]$SkipBuild, [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
& (Join-Path $root 'check-tools.ps1') -VerifyHashes
if (-not $SkipBuild) { & (Join-Path $root 'build.ps1') }
$app = Join-Path $root '拾链下载器.exe'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid executable product version.' }
$sourceVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'src/AssemblyInfo.cs')), 'Version = "([0-9.]+)"').Groups[1].Value
if ($version -ne $sourceVersion) { throw 'Executable is stale; build it again before packaging.' }
$dist = $OutputDirectory
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$archivePath = Join-Path $dist "LinkHarbor-v$version-Windows-x64.zip"
if (Test-Path -LiteralPath $archivePath) { throw "Archive already exists: $archivePath. Move it aside before rebuilding." }
$files = @('拾链下载器.exe', 'README.md', '使用说明.txt', 'AUTHOR_NOTICE.md', 'LICENSE', 'THIRD_PARTY_NOTICES.txt', 'CHANGELOG.md',
    'docs/使用教程.md', 'docs/THIRD_PARTY_SOURCES.md', 'docs/FFMPEG_BUILD_CONFIG.txt', "docs/releases/v$version.md", 'tools/README.md', 'tools/SHA256SUMS.txt')
$files += @(Get-ChildItem -LiteralPath (Join-Path $root 'tools') -Filter '*-LICENSE.txt' -File | ForEach-Object { 'tools/' + $_.Name })
foreach ($line in Get-Content -LiteralPath (Join-Path $root 'tools/SHA256SUMS.txt')) {
    if ($line -match '^[a-fA-F0-9]{64}\s+([^\\/:]+)$') { $files += 'tools/' + $Matches[1] }
}
$files = @($files | Sort-Object -Unique)
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf)) { throw "Missing package file: $file" }
}
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root $file), $file.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $dist "LinkHarbor-v$version-SHA256SUMS.txt"), "$hash  $([IO.Path]::GetFileName($archivePath))`n", [Text.UTF8Encoding]::new($false))
Write-Output "Created $archivePath ($($files.Count) files)."
