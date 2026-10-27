# Build and source

Application source is GPL-3.0. Build with the .NET 9 SDK on Windows. No game is needed for release fixture tests; local game-research tests require the user's own installation.

## Decoder

On Ubuntu 24.04 install gcc-mingw-w64-x86-64 and make, then run `bash Tools/build-ffmpeg.sh`. The script downloads FFmpeg 8.1.3 from ffmpeg.org, verifies SHA-256 7138d28c96d9d3e3af4ee3d8cad72741f8ffb40da90c1112235dea3ecd3178a3, and builds a minimal static Windows x64 executable with native audio decoders and no optional external libraries. It copies the executable, license texts, source details and build/toolchain information into vendor/ffmpeg.

The official FFmpeg archive is unmodified. Exact configure flags are in the script and generated build-configuration.txt. The separate executable can be replaced with a compatible modified build; Workshop does not sign-check or lock it. The matching complete FFmpeg source archive and build script accompany each release.

To build Windows binaries locally after the Linux decoder job, download its release-decoder artifact and place its contents in vendor/ffmpeg. This is a developer build step; end users receive the decoder already included.

## Application and checks

```powershell
./Build.ps1 -Portable -OutputDirectory .build/release-publish
dotnet build Tests/Tests.csproj -c Release -p:NuGetAudit=false
dotnet Tests/bin/Release/net9.0/Tests.dll --release .build/release-checks
./Package.ps1 -PublishDirectory .build/release-publish
```

The default Build.ps1 output is framework-dependent for development. -Portable includes the Windows x64 runtime. Publish release files into a fresh directory, separate from a working app's backups/drafts. Package.ps1 rejects private game/project content, checks required runtime/decoder files and archives only the clean publish directory. Its application-source ZIP comes from the committed Git tree.

GitHub Actions builds the pinned decoder on Linux, then builds, tests and packages on Windows. The release source archive includes synthetic test tones. No game audio, game executable, archives, installation backups or projects are part of the repository/release.

The portable package includes Microsoft's runtime license and third-party notices supplied by dotnet publish. FFmpeg LGPL texts and toolchain notices are under tools/ffmpeg. See THIRD-PARTY-NOTICES.md for reuse attribution.
