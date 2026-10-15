# libde265 x86 native dependency

`libde265.dll` is a Win32 (PE32/i386) shared-library build of libde265 1.1.3 from the unmodified official source tag. Its MSVC runtime is linked statically (`/MT`); its only DLL import is `KERNEL32.dll`. The source zip and extracted source are retained here for reproducible builds and LGPL source access.

Official source: https://github.com/strukturag/libde265/releases/tag/v1.1.3

Source archive: https://github.com/strukturag/libde265/archive/refs/tags/v1.1.3.zip

Source archive SHA-256: `2DEC9B2D6C788A4D21580AA61D0D53118BF699CB3D7E1AAEFB4264B5D1C5F494`

Library SHA-256: `3BAFEC009354633AA3E01116E23EC89448B3BAC4C983B6826AD4A1D9476CA7B8`

Build with `Build-libde265.ps1` using Visual Studio 2026 C++ Desktop tools. CMake configures the Win32 generator, Release optimization, SIMD enabled, the shared library, and no optional example dependencies. Exported libde265 C symbols use the C calling convention and are compatible with the codec's existing `CallingConvention.Cdecl` imports.

`Build-Native-Test.cmd` builds the native smoke-test program. Run it with a synthetic Annex B sample as its sole argument. It decodes into memory and reports dimensions, bit depth and a checksum; it writes no video:

```powershell
& '.\de265-native-test.exe' '..\..\testdata\sample-8bit.h265'
& '.\de265-native-test.exe' '..\..\testdata\sample-10bit.h265'
```

Verified results: 5 frames at 64x64 for 8-bit and 10-bit, with nonzero checksums `16763502830716638810` and `14834563443234805556` respectively.

## .NET COM host deployment

Microsoft supports framework-dependent COM hosting; self-contained COM component deployment is unsupported: https://learn.microsoft.com/en-us/dotnet/core/native-interop/expose-components-to-com

The COM host must be built for `win-x86` and the matching architecture .NET runtime must be installed. The DLL belongs beside the x86 managed codec assembly and x86 `.comhost.dll`. Building a managed assembly with `PlatformTarget=x86` alone is insufficient: ensure the native COM host is actually PE32, using the `NETCoreSdkRuntimeIdentifier`/runtime identifier selection from the Microsoft SDK. The local machine currently has .NET 10.0.12 x86 but no .NET 8 x86 runtime.
