param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'src/AssemblyInfo.cs')), 'Version = "([0-9.]+)"').Groups[1].Value
$output = Join-Path $root "dist/LinkHarbor-v$version-Windows-x64.exe"
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
$staging = Join-Path $root ('build/singlefile-' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $root 'package.ps1') -SkipBuild:$SkipBuild -OutputDirectory $staging
$payload = Join-Path $staging "LinkHarbor-v$version-Windows-x64.zip"
$manifest = Join-Path $staging 'payload.sha256'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($payload)
try {
    $lines = foreach ($entry in $archive.Entries) {
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { ([BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()) + '  ' + $entry.FullName }
        finally { $stream.Dispose(); $sha.Dispose() }
    }
} finally { $archive.Dispose() }
[IO.File]::WriteAllLines($manifest, [string[]]$lines, [Text.UTF8Encoding]::new($false))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
New-Item -ItemType Directory -Force -Path (Join-Path $root 'dist') | Out-Null
$icon = [IO.Path]::GetFullPath((Join-Path $root 'src/app.ico'))
$launcher = [IO.Path]::GetFullPath((Join-Path $root 'packaging/SingleFileLauncher.cs'))
$metadata = [IO.Path]::GetFullPath((Join-Path $root 'src/AssemblyInfo.cs'))
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /win32icon:$icon /out:$output /resource:"$payload,LinkHarbor.Payload.zip" /resource:"$manifest,LinkHarbor.Payload.sha256" $launcher $metadata
if ($LASTEXITCODE -ne 0) { throw 'Single-file launcher compilation failed.' }
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($output + '.sha256.txt'), "$hash  $([IO.Path]::GetFileName($output))`n", [Text.UTF8Encoding]::new($false))
Write-Output "Created full offline EXE: $output"
