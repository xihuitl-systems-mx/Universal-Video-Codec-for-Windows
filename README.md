# Universal Video Codec for Windows

Version 2.0.0 provides a custom HEVC decoder for Windows desktop applications that use Media Foundation, in both x86 and x64 processes. It decodes original files without converting them or creating video copies.

## Installation

Run `dist\Universal Video Codec for Windows Setup.exe` and accept the Windows administrator prompt. Restart video applications after installation. PowerShell scripts are not required. The installer includes the matching .NET 10 runtimes, registers both COM/MFT architectures, and checks that Windows can enumerate and activate each decoder. Remove the product through Windows Installed apps.

## Build

Install the .NET 10 SDK and put the Inno Setup 6 compiler at `tools\inno\ISCC.exe`. Run `Build.ps1`. The build produces both architectures, verifies PE machine types, runs COM decode tests, and packages the installer. Native library build scripts and source are under `native\x86` and `native\x64`; offline runtime installers are under `native\runtime`.

## Supported scope

- HEVC 4:2:0 video with even dimensions up to 8192 x 8192, using the dynamically linked libde265 decoder.
- HEVC Annex B and length-prefixed samples, including hvc1 configuration and Main 10 input.
- NV12 and P010 output in system memory. NV12 reduces 10-bit precision; this version does not implement Dolby Vision processing or HDR to SDR tone mapping.
- Optional local FFmpeg CUDA helper for high resolution streams on compatible NVIDIA hardware; CPU fallback is available.
- File playback also requires the host application's container reader, audio support, and selection of this decoder. Third-party desktop MFT registration does not guarantee compatibility with every application, protected media pipeline, or Store app.
- Original 4K 120 fps clips may exceed this implementation's decoding performance. A CUDA x86 diagnostic decoded 180 original frames with 180 different pixel hashes in 13.13 seconds, including helper startup; this is not a real-time playback certification.

This version uses native decoder registration. It does not install a file-opening launcher or change the user's default video application. Previous LAV Filters installations remain independently installed and can be removed separately in Installed apps.

## Diagnostics

`artifacts\native-v2\x86\HevcCodec.Cli.exe` and the x64 counterpart support `comtest`, `comselftest`, `enumtest`, `playbacktest`, and `transformfiletest`. COM tests alone do not prove that normal double-click playback displays video. The installer log and actual player verification are separate evidence.

## Verification on this PC (2026-10-15)

The installer completed successfully and MFTEnumEx enumerated and activated the custom decoder in both x86 and x64 processes. Normal Explorer double-click on the original IMG_1819.MOV opened Windows Media Player Legacy 32-bit with visible moving video, observed near 00:02 and 01:11. No media copy or conversion was made. This validates this playback route on this PC; it does not certify all Windows video applications.
