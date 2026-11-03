param([string]$OutputName = '拾链下载器.exe')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
New-Item -ItemType Directory -Force -Path (Join-Path $root 'build') | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$output = Join-Path $root $OutputName
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
$icon = Join-Path $root 'src/app.ico'
& (Join-Path $root 'src/make-icon.ps1')
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /win32icon:$icon /out:$output $sources
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Write-Output "已生成：$output"
