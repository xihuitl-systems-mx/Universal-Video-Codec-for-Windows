param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$sdk = (Get-Command dotnet -ErrorAction Stop).Source
$project = Join-Path $root 'src\HevcCodec.Cli\HevcCodec.Cli.csproj'
$bundle = Join-Path $root 'artifacts\native-v2'
$compiler = Join-Path $root 'tools\inno\ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Inno Setup 6 compiler is required in tools\inno.' }
foreach ($arch in @('x86', 'x64')) {
    $properties = @('-c', 'Release', '-r', "win-$arch", "-p:CodecArchitecture=$arch", "-p:NETCoreSdkRuntimeIdentifier=win-$arch", "-p:OutputPath=bin\Native-$arch\Release\", '--nologo')
    & $sdk restore $project "-p:RuntimeIdentifier=win-$arch" "-p:CodecArchitecture=$arch" --nologo
    if ($LASTEXITCODE -ne 0) { throw "Restore failed for $arch" }
    # Clean shared intermediate files before switching architectures.
    & $sdk clean $project @properties -v:q
    if ($LASTEXITCODE -ne 0) { throw "Clean failed for $arch" }
    $destination = Join-Path $bundle $arch
    & $sdk publish $project @properties --self-contained false -o $destination
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $arch" }
    foreach ($name in @('HevcCodec.comhost.dll', 'HevcCodec.runtimeconfig.json', 'HevcCodec.deps.json')) {
        Copy-Item -LiteralPath (Join-Path $root "src\HevcCodec\bin\Native-$arch\Release\$name") -Destination $destination -Force
    }
    Copy-Item -LiteralPath (Join-Path $root "native\$arch\libde265.dll") -Destination $destination -Force
    foreach ($name in @('HevcCodec.dll', 'HevcCodec.comhost.dll', 'HevcCodec.Cli.dll', 'libde265.dll')) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $destination $name))
        $machine = [BitConverter]::ToUInt16($bytes, [BitConverter]::ToInt32($bytes, 60) + 4)
        $expected = if ($arch -eq 'x86') { 0x14c } else { 0x8664 }
        if ($machine -ne $expected) { throw "Wrong architecture: $arch $name" }
    }
    if (-not $SkipTests) {
        $cli = Join-Path $destination 'HevcCodec.Cli.exe'
        & $cli comtest
        if ($LASTEXITCODE -ne 0) { throw "COM test failed for $arch" }
        foreach ($format in @('8bit', '10bit')) {
            $output = if ($format -eq '8bit') { 'nv12' } else { 'p010' }
            & $cli comselftest (Join-Path $root "testdata\sample-$format.h265") 5 $output
            if ($LASTEXITCODE -ne 0) { throw "Decode test failed for $arch $format" }
        }
    }
}
& $compiler (Join-Path $root 'installer\UniversalVideoCodec.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer packaging failed' }
Write-Host (Join-Path $root 'dist\Universal Video Codec for Windows Setup.exe')
