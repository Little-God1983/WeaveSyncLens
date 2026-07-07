# Downloads ffmpeg release essentials and places ffmpeg.exe in third_party/ffmpeg/
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root 'third_party/ffmpeg'
if (Test-Path (Join-Path $target 'ffmpeg.exe')) { Write-Host 'ffmpeg already present.'; exit 0 }

$zip = Join-Path $env:TEMP 'ffmpeg-release-essentials.zip'
Invoke-WebRequest 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $zip
$extract = Join-Path $env:TEMP 'ffmpeg-extract'
Expand-Archive $zip -DestinationPath $extract -Force
New-Item -ItemType Directory -Force $target | Out-Null
$exe = Get-ChildItem $extract -Recurse -Filter ffmpeg.exe | Select-Object -First 1
Copy-Item $exe.FullName (Join-Path $target 'ffmpeg.exe')
Remove-Item $zip; Remove-Item $extract -Recurse -Force
Write-Host "ffmpeg.exe installed to $target"
