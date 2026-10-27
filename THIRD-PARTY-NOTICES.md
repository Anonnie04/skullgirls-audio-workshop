# Source reuse

## Bundled FFmpeg source decoder

The release tools/ffmpeg/ffmpeg.exe is a Windows x64 build of unmodified FFmpeg 8.1.3.
Copyright the FFmpeg developers and individual contributors. The pinned official source archive
SHA-256 is 7138d28c96d9d3e3af4ee3d8cad72741f8ffb40da90c1112235dea3ecd3178a3.

Tools/build-ffmpeg.sh builds native audio decoders without optional external codec libraries,
network protocols or GPL-only components. This configuration is LGPL-2.1-or-later; the bundled
COPYING.LGPLv2.1, COPYING.LGPLv3, LICENSE.md, SOURCE.txt, build-configuration.txt and toolchain notices
provide its license and provenance. FFmpeg is invoked as a separate replaceable process for imports.
Native game encoding remains Windows ACM. A different local development build was used before release;
its earlier broad GPL binary is not part of the release package.

The complete matching unmodified [FFmpeg source](https://ffmpeg.org/releases/ffmpeg-8.1.3.tar.xz)
and build script are attached beside each Windows release. Source can also be obtained from ffmpeg.org.
The Workshop application and its source are GPL-3.0. Microsoft's self-contained runtime license and
third-party notices are carried in the published package. MinGW-w64/GCC runtime notices are included
with the decoder; see toolchain-notices.txt for their licenses and runtime exceptions.

## Skullgirls Mod Manager

Local source: sibling `Skullgirls Mod Manager` project, checkout
`7272ce97b46d9bc06f2de513273e7f2546f12ae7` on 26 October 2026.
That project is distributed under GPL-3.0. The full license is reproduced in `LICENSE`.
This derived Audio Workshop application is distributed under GPL-3.0.
Original authorship remains with the source project's contributors.

- `App/AudioPlayback.cs`: copied and adapted from Manager's `App/AudioPlayback.cs`.
  Changes adapt PCM storage, add queued range repetition and one-time seek tails, preserve
  pause while queuing, detect device completion, validate ranges and make disposal idempotent.
- `Core/ManagedDecoder.cs`: copies the `Wav` decoder and its primitive-reading helpers from
  Manager's `Core/AssetPreviews.cs`. A Workshop adapter validates PCM8/16 and Microsoft ADPCM metadata
  and converts decoded samples to its PCM representation.
- `App/Workshop.cs`: playback controls follow the Manager's device-position transport design.
  The Workshop UI, numeric controls, range overlay and keyboard controls are implemented here.
- `App/GameLocation.cs`: copied Manager's current Steam-installation discovery, with a Windows
  platform guard for the registry lookup. Explicit folder arguments take precedence; otherwise
  the browser discovers and opens the current installation when shown.

The Manager's original third-party notices are preserved in
`licenses/Skullgirls-Mod-Manager-NOTICES.md` for provenance. Other Manager modules,
its vendored GFS/Velvet code and its NuGet dependencies are not included in the application.
There is no runtime reference to Manager. Full application source is present in this local
project's `App` and `Core` folders, with build instructions in `README.md` and `Build.ps1`.

## Windows APIs

Playback uses the Windows-supplied `winmm.dll`. Queued looping follows Microsoft's
[waveform loop documentation](https://learn.microsoft.com/en-us/windows/win32/multimedia/looping-playback-wav).
The UI timer only displays position; it does not restart the loop.
This implementation has not been measured for audible discontinuities or verified in-game.

## Read-only bank inspection

`Core/GameLibrary.cs` enumerates bounded, structurally validated RIFF/WAVE payloads inside the local
`.snd-wav` and `.snd-wav_jpvoice` assets. It does not parse or rebuild bank event tables, or assign
engine sound IDs. No source from SndWavRepacker was copied. That project's
[reader/repacker](https://github.com/Devyatyi9/SndWavRepacker) was consulted as research context;
the current inspection operates on validated embedded WAV boundaries in the user's own files.
