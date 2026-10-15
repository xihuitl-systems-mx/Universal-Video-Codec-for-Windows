$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'dist\Universal Video Codec for Windows Setup.exe'
if (-not (Test-Path -LiteralPath $installer)) { throw 'Build the installer with Build.ps1 first.' }
Start-Process -FilePath $installer -Wait
