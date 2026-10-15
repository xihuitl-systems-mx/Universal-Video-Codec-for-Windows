param([string]$VisualStudioRoot = 'C:\Program Files\Microsoft Visual Studio\18\Community')
$ErrorActionPreference = 'Stop'
$nativeRoot = $PSScriptRoot
$cmake = Join-Path $VisualStudioRoot 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$msbuild = Join-Path $VisualStudioRoot 'MSBuild\Current\Bin\MSBuild.exe'
$sourceRoot = Join-Path (Split-Path $nativeRoot -Parent) 'x86'
$source = Join-Path $sourceRoot 'source\libde265-1.1.3'
$build = Join-Path $nativeRoot 'build'
if (-not (Test-Path -LiteralPath $cmake)) { throw 'Visual Studio CMake was not found.' }
if (-not (Test-Path -LiteralPath $source)) {
    Expand-Archive -LiteralPath (Join-Path $sourceRoot 'libde265-v1.1.3.zip') -DestinationPath (Join-Path $sourceRoot 'source')
}
& $cmake -S $source -B $build -G 'Visual Studio 18 2026' -A x64 -DENABLE_DECODER=OFF -DENABLE_SDL=OFF -DENABLE_ENCODER=OFF -DENABLE_SHERLOCK265=OFF -DBUILD_SHARED_LIBS=ON -DCMAKE_BUILD_TYPE=Release '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded'
if ($LASTEXITCODE -ne 0) { throw 'libde265 CMake configuration failed.' }
& $msbuild (Join-Path $build 'libde265\de265.vcxproj') /p:Configuration=Release /p:Platform=x64 /nodeReuse:false /verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw 'libde265 x64 compilation failed.' }
Copy-Item -LiteralPath (Join-Path $build 'libde265\Release\libde265.dll') -Destination (Join-Path $nativeRoot 'libde265.dll') -Force
Write-Host 'libde265 x64 is ready.'
