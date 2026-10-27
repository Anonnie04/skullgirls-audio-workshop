# Music format research — 26 October 2026

Follow-up: the version 0.2 browser now copies Manager's playback transport and managed decoder,
with GPL-3.0 notices in the project root. The "no source copied" statement below describes the
initial research prototype only. Decoder and silent transport results are documented in README.md.

## Evidence and scope

Inspected `D:\Steam\steamapps\common\Skullgirls`, Steam app 245170, manifest build ID **14696341**.
Executable SHA-256: `fb757433d2d11602ded18040078e70319afdae98c35fc22d789b38d012ad400b`.
The manifest identifies the installation; this is not proof of Steam-original archive contents.

The recognized `CoreMessaging.dll` is present, SHA-256
`014bfb8c48fa7afbcd585a34d814011b96b289c742482a01376f56aafa1e11d3`.
Manager's `Core/ArchiveCompatibility.cs` and `Core/Manager.cs` therefore select `data02`.
At the initial research stage, runtime file accesses had not been observed. A later read-only
process-handle inspection confirmed the running modern game had `data02/music-win.gfs` open,
alongside `core.gfs`, `characters-foits.gfs`, `sprites.gfs`, `ui-win.gfs` and `ui.gfs` in data02.
Its loaded modules also included the local game-folder `CoreMessaging.dll`.
Both archive folders were read and fingerprinted. The music and core archives currently match
between `data01` and `data02`:

| Archive | Bytes | SHA-256 |
| --- | ---: | --- |
| music-win.gfs | 376,907,824 | e077284c566722121f5854d1e407cb71d526df74c5dac71505a86be3faf8e4ae |
| core.gfs | 10,559,300 | 375c60f542b265b2c317bb1067553e6df7ade13c41278b978791c9b2b878261e |

Evidence files are in `run-002/`: `inventory.json`, extracted `music.ini` and `stages.ini`,
`native-validation.json`, and the optional `manager-validation.json`.
`run-001` is an incomplete first experiment that captured FFmpeg's native-block rejection.

## Verified locally

- Music archive: Reverge Package File 1.1; 100 entries, all `music/*.wav`, alignment 1.
- All 100 assets fully decode with FFmpeg 6.0 using error-fatal decoding.
- All are stereo Microsoft ADPCM (format tag 2), 4 bits/sample, 140 bytes/block,
  128 samples/channel/block. The seven predictor coefficients match across the test pair.
- There are **21 distinct sample rates**, ranging from **44,078 to 44,160 Hz**;
  only 50 assets use exactly 44,100 Hz. Retain per-target rates until timing behavior is understood.
- All 100 WAVs contain `fmt `, `data`, and a 60-byte `smpl` chunk. The latter has one
  sampler-loop record. Its role in the engine is unverified; do not discard it or equate it
  with the game's intro/loop transition behavior.
- `core/music.ini` explicitly identifies **24 track pairs** and their optional stage references.
  All 48 configured asset references resolve. These mappings do not cover every music asset.
  Filename-based pairings outside this configuration remain hypotheses.
- A no-op Python repack is byte-identical to the installed music archive.
- Replacement repacks preserve the entry order, alignment, count, and SHA-256 of all 98
  untouched payloads, and contain the exact bytes of both replacements.
- Game archive fingerprints were unchanged after the research runs.

## Encoding result

Installed FFmpeg 6.0 decodes the native files but rejects `adpcm_ms -block_size 140` with
`block size must be power of 2`. The
[FFmpeg 6.0 encoder source](https://www.ffmpeg.org/doxygen/6.0/adpcmenc_8c_source.html)
contains that restriction. A 128-byte FFmpeg candidate was produced only as a comparison;
it changes the block structure and omits original sampler metadata. It is not the recommended test.

Windows `msacm32` accepted the exact original format and encoded the **Moonlit Melee** pair
using 140-byte blocks. FFmpeg independently decoded those outputs. The research encoder preserves
the complete original WAV structure and non-audio chunks, replacing only data bytes; it deliberately
rejects changes to the total block count. It is a fixed-duration diagnostic, not yet a general editor.

| Asset | Rate | Duration | Measured RMS change |
| --- | ---: | ---: | ---: |
| music/skull02_1.wav | 44,103 Hz | 6.956806 s | -12.1215 dB |
| music/skull02_2.wav | 44,100 Hz | 175.653152 s | -12.0961 dB |

Gain requested: 0.25 (approximately -12 dB). Decoded sample counts, data sizes, ADPCM coefficients,
block settings and sampler metadata match the originals. Lossy re-encoding accounts for the
small difference from the requested gain. This does not prove seamless playback in the engine.

The ZIP replaces only these two assets under `music-win.gfs/music/`, with ordinary `mod.json`
metadata. The local Manager harness imports it disabled into a fresh isolated library, validates
the Python-built archive, then builds and validates its own archive. Read `manager-validation.json`
for the actual check result; no installed-game Apply is part of that harness.

## Pending in-game verification

Version 0.8 update: Workshop now has its own Apply/Restore engine. Its 22 isolated scenarios and
real closed-game Apply/Restore passed; the live music archive returned to SHA-256
`e077284c566722121f5854d1e407cb71d526df74c5dac71505a86be3faf8e4ae`.
The user subsequently tested the −12 dB Moonlit Melee candidate and reported clean entry,
one seamless loop boundary, normal re-entry and normal unaffected-stage playback. However,
the user heard no volume difference compared with Restore, so replacement playback remains
unconfirmed. Independent decoding measured −12.0815 dB on the intro and −12.0549 dB on the loop.
Restore returned the exact original archive hash. Evidence is in
`../.build/playback-moonlit-later-20261026/listening-results.json` and `measured-gain.json`.

At the user's request, a second diagnostic replaced only `music/menu_intro.wav` and
`music/menu_loop.wav` with Moonlit Melee at normal gain. These explicit diagnostic filenames
are not configuration-backed catalogue mappings. The existing native format guards and complete
repack verification passed; all 98 other assets remained byte-identical. The menu test was applied,
with original backup retained and installed archive SHA-256
`bc600d68dfda8f4af1d705704daf00d9c11033afb774f7c9a2e7f720e2b14cf1`.
Its plan and Apply result are in `../.build/playback-menu-moonlit-20261026/`.
The user confirmed the main menu now plays the Streets of New Meridian song, establishing
that the replacement loaded and played in-game. See `listening-results.json` in that folder.
This confirms the explicit menu filenames' playback use on this build. The user then reported
two clean loop boundaries with no unusual sound, gap or click. After the game closed, Restore
returned the full archive to the original `e077284c...8e4ae` hash; the backup remains retained.
Explicit intro-transition and post-Restore audible menu checks remain open.
The original Manager-based research procedure below is historical.

Candidate: `run-002/Moonlit-Melee-Native-ADPCM-Test.zip`.
Mapping evidence: `Track: Moonlit Melee`, `Stage: New_Meridian`,
`Files: skull02_1, skull02_2` in the extracted `core/music.ini`.

1. Close the game. Import the ZIP into Manager, review its two replacement paths and selected
   archive folder, enable it, and Apply through Manager's normal backup workflow.
2. Enter the stage associated with `New_Meridian` and compare to baseline using the same music
   volume. The intended audible change is the existing track playing about 12 dB quieter.
3. Check the approximately 6.96-second intro transition. Remain on the stage through at least
   two complete approximately 175.65-second loops; listen for silence, clicks, timing drift or errors.
4. Leave and re-enter the stage; check another stage still plays normally.
5. Close the game, disable this test mod and Apply again. Confirm the pre-test music state returns.
   Preserve other enabled mods; use Manager's preview to review the combined result.
6. Record the build, selected mode/stage, active archive folder, transition and loop observations,
   and restoration result. The present status remains **not tested in-game**.

Do not manually copy the full research archive over the installation: that bypasses Manager's
transaction handling and may overwrite later changes. No live-game files were modified here.

## Decisions for the next milestone

Start the browser with the 24 configuration-backed track pairs and retain exact asset paths.
Keep unmapped assets inspectable without inventing names or relationships. Use FFmpeg for the
decoding prototype and evaluate Windows ACM for encoding; confirm codec availability on clean
Windows machines before committing to it. Arbitrary-duration editing needs an explicit policy
for 128-sample block boundaries and sampler metadata, followed by engine tests.

Standalone decoding/packaging, dependency redistribution, loader requirements on clean installs,
project recovery, production Apply/Restore, and voice banks remain separate unverified work.
The sibling Manager identifies its project as GPL-3.0 and its GFS dependency as MIT in its local
notices. No Manager source was copied into this research prototype; review any future reuse and
release packaging against the actual dependency licenses.
