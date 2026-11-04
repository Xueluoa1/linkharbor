param([switch]$SkipBuild, [string]$InnoCompiler = (Join-Path $PSScriptRoot 'build/inno/ISCC.exe'))
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (!(Test-Path -LiteralPath $InnoCompiler)) { throw 'Install Inno Setup 6.7.3 and pass -InnoCompiler <ISCC.exe>. See docs/安装包构建.md.' }
if (-not $SkipBuild) { & (Join-Path $root 'build.ps1') }
$work = Join-Path $root ('build/installer-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $work 'stage'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
& (Join-Path $root 'package.ps1') -SkipBuild -OutputDirectory $work -StoreOnly
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Get-ChildItem -LiteralPath $work -Filter '*.zip' | Select-Object -First 1
[IO.Compression.ZipFile]::ExtractToDirectory($zip.FullName, $stage)
[IO.File]::WriteAllText((Join-Path $stage 'installed-mode'), "LinkHarbor installed edition`r`n")
Copy-Item -LiteralPath (Join-Path (Split-Path $InnoCompiler) 'license.txt') -Destination (Join-Path $stage 'tools/InnoSetup-LICENSE.txt')
$iss = Join-Path $root 'packaging/installer.iss'
& $InnoCompiler "/DStageDir=$stage" "/DOutputDir=$work" $iss
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$output = Join-Path $root 'dist/LinkHarbor-v1.0.1-Setup.exe'
New-Item -ItemType Directory -Force -Path (Split-Path $output) | Out-Null
$backend = Join-Path $work 'LinkHarbor-Backend.exe'
$sources = @('packaging/Setup.cs','src/Theme.cs','src/AssemblyInfo.cs') | ForEach-Object { Join-Path $root $_ }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll "/resource:$backend,backend" "/win32icon:$(Join-Path $root 'src/app.ico')" "/out:$output" $sources
if ($LASTEXITCODE -ne 0) { throw 'Setup UI compilation failed.' }
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($output + '.sha256.txt'), "$hash  $([IO.Path]::GetFileName($output))`n", [Text.UTF8Encoding]::new($false))
Write-Output "Installer: $output"
Write-Output "Backend and staging: $work"
