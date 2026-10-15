# libde265 x64 native dependency

`libde265.dll` is an x64 (PE32+/AMD64) shared-library build of libde265 1.1.3 from the unmodified official source tag. Its MSVC runtime is linked statically (`/MT`); its only DLL import is `KERNEL32.dll`, so this binary does not require Visual C++ Redistributable installation.

Official source: https://github.com/strukturag/libde265/releases/tag/v1.1.3

The complete official archive and extracted sources are shared with the x86 build in `../x86/libde265-v1.1.3.zip` and `../x86/source/libde265-1.1.3`. The included `COPYING` is the upstream LGPL license.

Source archive SHA-256: `2DEC9B2D6C788A4D21580AA61D0D53118BF699CB3D7E1AAEFB4264B5D1C5F494`

Library SHA-256: `FDE593EC57BB0BABFAA62A86A74AA3ECB57558F9FA7E2DC2A56ED32B2D90B98E`

Build with `Build-libde265.ps1` using Visual Studio 2026 C++ Desktop tools. CMake selects the x64 generator, Release optimization, SIMD enabled, shared library, and no optional example dependencies. The generated DLL belongs beside the x64 codec assembly and x64 COM host.

`Build-Native-Test.cmd` compiles the same in-memory native smoke test used for x86. It writes no decoded video:

```powershell
& '.\de265-native-test.exe' '..\..\testdata\sample-8bit.h265'
& '.\de265-native-test.exe' '..\..\testdata\sample-10bit.h265'
```

Verified results: 5 frames at 64x64 for 8-bit and 10-bit, with checksums `16763502830716638810` and `14834563443234805556`, matching the x86 output exactly.
