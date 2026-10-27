# Release decoder

Release binaries use a minimal Windows x64 FFmpeg 8.1.3 build compiled from the official
unmodified source archive by `Tools/build-ffmpeg.sh`. It uses FFmpeg's native audio decoders,
no optional third-party codec libraries, no network protocols and no GPL-only components.

The build workflow supplies `ffmpeg.exe`, LGPL license texts, configuration, toolchain notices
and source information here before publishing. Generated binaries are not committed.
Run the Linux build script with the MinGW-w64 cross compiler, or use the GitHub Actions workflow,
then copy the decoder artifact into this directory before building Workshop.

Each release attaches the exact FFmpeg source archive separately, alongside Workshop source
and the Windows package. FFmpeg runs as a replaceable executable under `tools/ffmpeg`.
