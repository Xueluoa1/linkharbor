param([Parameter(Mandatory=$true)][string]$Backend)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$output = Join-Path $root ('test-output/installer-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$sources = @('tests/InstallerTests.cs','packaging/Setup.cs','src/Theme.cs','src/AssemblyInfo.cs') | ForEach-Object { Join-Path $root $_ }
$testExe = Join-Path $output 'InstallerTests.exe'
& $compiler /nologo /target:exe /main:InstallerTests /platform:x64 /optimize+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll "/resource:$Backend,backend" "/out:$testExe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Installer tests compile failed.' }
& $testExe $output
if ($LASTEXITCODE -ne 0) { throw "Installer tests failed; inspect $output" }
Write-Output "Installer validation: $output"
