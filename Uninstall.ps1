$ErrorActionPreference = 'Stop'
$uninstaller = Join-Path ${env:ProgramFiles} 'Universal Video Codec for Windows\unins000.exe'
if (-not (Test-Path -LiteralPath $uninstaller)) { throw 'Installation not found.' }
Start-Process -FilePath $uninstaller -Wait
