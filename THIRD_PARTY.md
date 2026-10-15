# Third-party components

## libde265 1.1.3

The custom MFT dynamically links to libde265, licensed under GNU LGPL 3 or later. License copies are included as LGPL-3.0.txt and GPL-3.0.txt. The DLL can be replaced with a compatible build. Both native libraries were built with the static Microsoft C runtime. Corresponding source, build scripts and native diagnostic sources are kept in this project under native\x86 and native\x64.

Upstream source: https://github.com/strukturag/libde265/releases/tag/v1.1.3

## FFmpeg

The installer includes the existing local FFmpeg binary as an optional CUDA helper. Its license is included as FFMPEG-LICENSE.txt. Upstream: https://ffmpeg.org/. The distributed binary's exact source/build provenance must be retained when redistributing this package publicly.

## Microsoft .NET

The installer includes unmodified official .NET Runtime 10.0.12 installers for Windows x86 and x64. They are installed only if .NET 10 is missing for that architecture. The runtime is covered by Microsoft's included notices and licenses. Downloads were verified against Microsoft's release metadata SHA-512 hashes.

## LAV Filters

Version 2.0.0 does not bundle or install LAV Filters. Earlier versions installed the official LAV Filters 0.83 package separately; its uninstall entry is independent of this product.
