$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
New-Item -ItemType Directory -Force -Path test-output/fixture,test-output/fake-tools | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /codepage:65001 /r:System.Web.Extensions.dll ('/out:' + (Join-Path $PSScriptRoot 'test-output/fake-tools/yt-dlp.exe')) (Join-Path $PSScriptRoot 'tests/TestHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test host compile failed' }
& $compiler /nologo /target:exe /codepage:65001 /r:System.Web.Extensions.dll ('/out:' + (Join-Path $PSScriptRoot 'test-output/tests.exe')) (Join-Path $PSScriptRoot 'src/Core.cs') (Join-Path $PSScriptRoot 'tests/Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Tests compile failed' }
& ./tools/ffmpeg.exe -hide_banner -loglevel error -y -f lavfi -i 'testsrc=size=320x180:rate=10' -f lavfi -i 'sine=frequency=440:sample_rate=44100' -t 3 -c:v libx264 -pix_fmt yuv420p -c:a aac test-output/fixture/video.mp4
if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed' }
& ./tools/ffmpeg.exe -hide_banner -loglevel error -y -i test-output/fixture/video.mp4 -map 0:v -map 0:a -c copy -f dash test-output/fixture/manifest.mpd
if ($LASTEXITCODE -ne 0) { throw 'DASH fixture generation failed' }
$pidFile = Join-Path $PSScriptRoot 'test-output/child-pid.txt'
if (Test-Path -LiteralPath $pidFile) { Remove-Item -LiteralPath $pidFile }
$portFile = Join-Path $PSScriptRoot 'test-output/server-port.txt'
if (Test-Path -LiteralPath $portFile) { Remove-Item -LiteralPath $portFile }
$server = Start-Process -FilePath (Join-Path $PSScriptRoot 'tools/node.exe') -ArgumentList 'tests/server.js' -WindowStyle Hidden -PassThru
try {
    for ($i = 0; $i -lt 100 -and -not (Test-Path -LiteralPath $portFile); $i++) { Start-Sleep -Milliseconds 50 }
    $port = (Get-Content -LiteralPath $portFile -Raw).Trim()
    & ./test-output/tests.exe "http://127.0.0.1:$port"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally { if (-not $server.HasExited) { Stop-Process -Id $server.Id } }
