# Historical development notes

This is the development README captured before public release preparation. Its pending-work and dependency statements describe those earlier milestones; use the root README and current compatibility/validation documents for release status.

# Skullgirls Audio Workshop

Standalone Windows prototype for the music editor described in [PROPOSAL.md](PROPOSAL.md).
Version 0.9 browses music, story voices, UI effects and character banks, with game-derived clip descriptions,
saved personal labels, seeking and range auditioning. The music replacement editor imports common audio formats,
cuts an intro/loop pair, applies gain/fades/crossfade, generates native audio and independently decodes
it for review. Editable projects, source copies, recovery drafts, native WAV exports and mod ZIP
exports and a first Apply/Restore workflow are supported. In-game listening, broader acceptance and
portable-release validation remain open.

## Apply, Restore and interruption recovery

The first installation workflow supports the observed alignment-1 `music-win.gfs` layout and one
intro/loop pair in a single atomic archive replacement. It does not install voice banks or transact
across multiple archives. Linked/reparse installation and backup paths are rejected.

1. Close Skullgirls. Generate and verify the replacement, then open **Apply / Restore…** in the editor.
2. **Prepare Apply…** copies and hashes the current music archive, builds a staged archive beside the
   active one, and verifies every payload, including all untouched entries. This does not replace the
   active music. It checks free space, the game build and active archive folder, and locks out another
   Workshop writer. Editing/exporting your project does not install anything.
3. Review the exact archive, two assets and backup location, then accept Apply. A verified preparation
   can also be resumed later with **Apply prepared…**. Apply rechecks hashes immediately before its
   atomic replacement, then verifies installed and displaced archives and records completion.
4. Test stage entry, intro transition, at least two loops, leaving/re-entering the stage and an
   unaffected stage. Close the game before **Restore pre-apply music**.
5. Restore verifies both its backup and the current installed hash, stages the baseline on the game
   drive, atomically restores it and verifies the full restored hash. Reopen the editor after an
   installation change to refresh archive fingerprints. Projects can still be saved before reopening.

Backups are in `installation-backups/` beside the executable, scoped by installation, archive folder
and transaction ID. They capture **your actual pre-apply state**, including any existing mods, rather
than assuming a Steam-original baseline. They are retained after Restore/aborted preparations and are
separate from disposable `generated/` audio. Keep this backup store when moving/updating the app.
Restore the active Workshop transaction before applying another replacement; stacking is not supported.

The library's **Apply / Restore…** button works without a project and shows pending state on startup.
Use **Recover interrupted install** for unfinished preparations, commits or restores. Recovery returns
to the verified pre-apply archive, or aborts a preparation that never changed it. Unknown external
archive changes, corrupt backups, changed builds/locations or malformed journals block writes for
review. Workshop does not force-overwrite other tools' changes or adopt Manager's private backups.
If an operation fails, keep its journal/backups and reopen Apply / Restore rather than copying files
manually. Cancellation is available during preparation; commit/restore finish their safe step.

`.build/checks-installation/installation-results.json` records 22 isolated scenarios: install/restore,
preexisting-mod preservation, restart recovery at seven checkpoints, partial restore stages, stale
inputs, corrupted backups/journals, insufficient space, cancellation, competing writers and changed
build/location checks. It also verifies a byte-identical no-op of the real music archive and all real
replacement payloads without installing that fixture. These checks do not simulate every OS/filesystem
failure or establish generic multi-archive transaction support.

`.build/checks-installation/live-cycle-results.json` records a real closed-game Apply/Restore cycle:
the installed output matched its staged hash and Restore returned the exact original full archive hash.
The user reported clean stage entry and one loop boundary, but no noticeable −12 dB volume change;
that test does not establish replacement playback. Restore returned the original hash. Read-only
runtime inspection confirmed the game opens `data02/music-win.gfs` with the local loader active.
A user-requested diagnostic replaced only the menu intro/loop with **Moonlit Melee at normal gain**.
The user confirmed the new song and two clean loop boundaries. Restore returned the complete
archive to its original hash; the backup is retained. Evidence is under
`.build/playback-menu-moonlit-20261026/`. Explicit intro-transition and post-Restore audible menu
checks remain open.

## Export replacements

1. Generate and review the converted audio in Side B. Export actions enable after successful generation.
2. Open **Export → Export WAVs…** or **Export mod ZIP…**. Review exact intro/loop paths, durations,
   clipping and timing adjustments, and edit name, author, numeric version and description.
3. Continue and select a destination outside the game. The output path appears in the editor.

**Export WAVs…** creates a fresh folder containing the two native MS ADPCM WAVs at
`music-win.gfs/music/<target filename>.wav`, plus `export.json`. These are target-format replacements,
not PCM masters. Export copies the verified converted bytes without re-encoding.

**Export mod ZIP…** writes root `mod.json`, `export.json` and exactly two replacement paths under
`music-win.gfs/`. It uses the existing Manager package layout, with a stable ID derived from the
project ID. Existing ZIPs are preserved: choose a new filename for each export. Manager is optional
and is not needed to create either export. Importing multiple versions with the same project ID into
Manager may require removing/replacing the previously imported mod under Manager's normal rules.

**Export → Mod details…** edits the same fields before generation. Details are included in projects
and recovery drafts. Changing these fields does not invalidate audio; changing cuts/processing does.
Accepting details marks the project unsaved, even if the destination picker is subsequently cancelled.

Export rechecks source/target hashes, edit identity and cached WAV hashes, independently decodes the
actual WAVs, then stages and verifies the output before publishing it. Cancel is supported before
publication. Reports contain hashes and edit/format information, and exclude source audio and local
source/game paths. No game archives are written or mods enabled. Conversion and package validation
do not establish correct in-game playback or loader setup; those acceptance checks remain open.

Verification is recorded in `.build/checks-exports/export-results.json`. The optional Manager harness
uses a fresh isolated library to check disabled import, exact audio and replacement scope; it does not
install to the game. `Tests --exports GAME OUTPUT_DIRECTORY` exercises export behavior.

## Save and recover projects

- In the music editor, **Save project** (Ctrl+S) or **Save as…** (Ctrl+Shift+S) writes a versioned
  `.sgawproj` file with exact target asset paths/hashes, source identity, cuts, gain, fades and crossfade.
  Projects can keep incomplete cuts; conversion still requires a valid selection.
- **Copy source: ON/OFF** is on by default. A verified original-format copy goes in `<project>.sgawproj.sources/`
  and the project references it relatively. Move that folder together with the project file. With the
  option off, the project references the source by absolute path. Saving does not alter the source.
- Use **Open project…** in the library to reopen it against the active installation. Missing sources
  prompt for relinking; a different source hash or changed target fingerprint requires explicit review.
  Saved generated audio is never silently reused: generate again after opening.
- Edits applied in the main editor are written to `drafts/` beside the executable after about one second
  of idle time. **Recover draft…** opens these after an interruption; use last-modified time to select
  the desired draft. Changes pending inside the separate source-cut dialog enter the draft after
  **Use these cuts**, while Cancel leaves the previous project state intact.
- Closing unsaved work offers Save, Discard or Cancel. Successful saving or deliberate discard removes
  that window's unchanged recovery draft. Recovered drafts use Save as so the original project remains
  available. An external project change blocks ordinary Save; use Save as to preserve both versions.
- Project and draft writes stage and flush a temporary file before replacing the JSON atomically.
  Temporary staging files are not offered as recovery drafts. Unsupported project versions and invalid
  files are rejected without replacing them. Projects/source copies must be outside the game folder.

Project storage checks are recorded in `.build/checks-projects/project-results.json` (round trips,
copied/relocated sources, incomplete drafts, interrupted writes, conflicts, malformed/future files and
unchanged game/source). This is local project recovery; installation/restore recovery is still pending.

## Music replacement editor

Select a configuration-backed music pair, then **Replace music… → Import audio…**.
Supported sources: **WAV, MP3, FLAC, OGG/Vorbis, OGG/Opus, M4A/AAC, WMA and AIFF**.
24-bit, float and extensible WAV are decoded by the bundled FFmpeg backend. PCM8/16 and Microsoft
ADPCM WAV retain the managed path and their original mono/stereo rate. Other imports normalize to
44.1 kHz stereo PCM16 for editing; multichannel sources are downmixed. Game outputs still use each
target's native ADPCM codec, block size and rate. Import does not modify the source or game.

Inputs are limited to 128 MB and decoded audio to 32 million samples (about six minutes at stereo
44.1 kHz), with an overall ten-minute ceiling for the managed path. Oversized decoded sources are
rejected, not truncated. Imports can be canceled and time out after two minutes. Damaged, protected
or unsupported encodings show an error. Only local files in the listed formats are accepted.
Projects copy the original encoded bytes and extension, and decode again when reopened/relinked.
The application carries its own decoder under `tools/ffmpeg`; no separate installation is required.

The editor presents two Art Deco record decks: **Side A / Source** holds the imported file and source
audition, while **Side B / Target** holds the selected game track and original/edited/converted
auditions. Each disc carries its title; only the playing side rotates, with pause holding the angle
and Stop returning both decks to rest. The waveform and cutting controls remain below the decks.

Choose **Edit source…** on Side A for a dedicated cutting window. Click the large waveform to place
the cursor, then use **Intro starts here**, **Loop starts here** or **Loop ends here** (I/L/E shortcuts
when a number field is not focused). Numeric times, zoom and scrolling give finer control.
**Play intro → loop** auditions the selected source intro once and repeats its loop; **Check loop seam**
starts near the end to hear the wrap quickly. These auditions use the unprocessed source. Use Side B's
**Preview edits** for gain/fades/crossfade. **Use these cuts** transfers boundaries back and invalidates
previous converted output; Cancel discards changes made in the cutting window.

- Use the waveform click-action selector or the numeric fields to set intro start, loop start and
  loop end. Each segment must be at least 10 ms. Zoom and the brass scroll control inspect the source.
- Gain is -60 to +24 dB. Intro fade-in and loop fade-out are optional. Crossfade blends the loop tail
  into its head and shortens the loop by the overlap duration; it must be shorter than half the loop.
- **Preview edits** auditions processed PCM. **Generate & verify** encodes each segment with its
  target's own sample rate, exact stereo MS ADPCM format and 140-byte / 128-frame block structure.
  Segment lengths are resampled to the nearest complete block, with the measured duration adjustment
  displayed. No zero padding is appended. The full-span `smpl` loop end is updated to the final frame.
- The clipping counter reports samples clipped before encoding. Reduce gain when it is nonzero.
  ADPCM is lossy; **Play converted** auditions the actual generated files after independent decoding.
  Changing edits invalidates this preview. **Play original** uses the same device and playback volume.
- Pair audition plays the intro once and then repeats the loop when Loop is on. Paired assets can have
  different rates: only the preview intro is resampled to the loop's rate for a single device stream.
  The two generated files retain their separate target rates. This is not an in-game playback test.
- Generated WAVs and `validation.json` go in a fresh subfolder of `generated/` beside the executable.
  The displayed folder can be copied from the output field. The report binds edits to source/target
  hashes and output hashes. This disposable output is not an editable saved project or installation.
  Cancel stops between codec operations; failed staged output is removed. Source and game files remain
  unchanged, and changed source/target data requires reimporting or reopening the editor.

Windows ACM supplies encoding; the app's separate managed decoder verifies saved output. Manager
is not required, and FFmpeg is bundled for source imports. Availability on a clean Windows machine and actual in-game
compatibility still need acceptance testing. The editor currently supports the observed 24 mapped
intro/loop music pairs; other assets remain browse/preview-only.

Developer verification: `Tests --editor GAME OUTPUT_DIRECTORY` uses synthetic audio, probes all 48
mapped target formats, checks DSP/metadata/stale-source behavior and unchanged game music, compares
generated PCM exactly with FFmpeg, and tests the intro-to-loop device handoff using silence.

## Run the prototype

The interface uses a Skullgirls-inspired Art Deco record-player theme: burgundy and charcoal
surfaces, brass geometric borders, ivory typography, a geometric record medallion and a vinyl preview display.
The vinyl's printed label, rim marker and groove details rotate during playback; pause holds the angle
and Stop/completion stops the animation. A separate animation clock prevents seeks and repeated audio
from making the disc jump backwards. Soft stationary highlights and a brighter rim give the grooves
a lacquered finish, while the tonearm stays separate from rotation. The current clip name is printed
on the rotating label and repeated on a stationary plaque below the disc. Opaque buffered layout
panels and a single record animation paint loop avoid transparent-parent redraws during playback. Library
rows show a second line for dialogue previews or source context. All artwork is drawn in the app;
no game artwork or external image assets are required. The personal-label dialog shares the theme.

Double-click `Launch Skullgirls Audio Workshop.cmd`. The current Steam installation's music library
opens automatically using the same discovery rules as Mod Manager: `SKULLGIRLS_GAME_ROOT` when
explicitly configured, then Steam's registry location and library folders. Automatic discovery checks
the standard `steamapps/common/Skullgirls` folder, not dated legacy copies. A folder passed on the
command line overrides discovery. **Open installation** remains available to choose another folder;
if discovery finds nothing, the window stays open for manual selection. This opens the library and
does not launch the game executable. The local prototype requires the .NET 9 Desktop Runtime,
which is installed on this machine. Manager and FFmpeg are not required to run the browser.

- All audio-bearing archives are indexed on startup. Use **Audio category and archive** to choose
  Music, English/Japanese story voices, UI/shared effects, or default/Japanese character banks.
  Each category has a title, description of its purpose, and exact archive filename.
- **Announcers** is a dedicated category for the seven `hitspark` base/alternate banks across
  default and Japanese audio. These already belonged to the character archives; the category
  exposes them without duplicating the audio inventory. The banks mix announcer voices and shared
  combat effects. Base/alternate labels are retained until exact voice-pack and clip identities are mapped.
- Search titles, character identifiers, category names, stage references, archive names or internal paths.
- Categories appear in a consistent order: Music, Announcers, English/Japanese story voices,
  default/Japanese character banks, then UI/shared effects. Voice entries are ordered by character
  and naturally by clip number (100 before 1000). Base banks precede alternate banks.
  Character names, player numbers and language variants are expanded where their identifiers are known.
- Music retains 24 configuration-backed pairs plus 54 unmapped assets. Other clip titles are derived
  from filenames; exact dialogue, events and voice triggers are not inferred from those titles.
- Select a character bank to enumerate its validated embedded RIFF/WAVE clips. The clip selector
  identifies each by number and byte offset. Bank event tables and sound IDs are not interpreted.
- Story clips with a unique localization-key match show a dialogue preview in the library and full
  game text with its key and scene references in the scrollable description panel. Search also checks
  these descriptions. This is game dialogue text linked by filename, not a transcription of the audio.
- Music segment selectors show the track name and Intro/Loop role from `music.ini`. Embedded
  RIFF INFO titles are shown in the description where present.
- **Label clip** saves your own title and description; **Reset label** restores the automatic label.
  Labels live in `dist/SkullgirlsAudioWorkshop/labels/clip-labels.json`, independently of the game.
  Back up this folder when moving the app. Labels identify the archive, asset, offset and WAV SHA-256,
  so edited audio will not silently inherit a previous label. Personal labels are shown separately
  from game-derived metadata. Corrupt label catalogs are preserved and block label writes for review.
- Choose a segment to view duration, codec, sample rate, block size, sampler presence and waveform.
- Use **Play**, **Pause / Resume**, and **Stop**. The playhead and time display follow the device.
- Click the waveform to seek, or enter seconds and press **Seek**. Seeking works before playback and while paused.
- Toggle **Loop: on/off** to repeat playback. Set repeat start/end in seconds to loop a selected range;
  the full clip is used by default. **Full segment** restores the complete range.
  The shaded waveform shows the range. Repetition is queued to the audio device rather than restarted by the UI timer.
- Space plays or toggles pause outside text/numeric controls. Escape stops playback.
- Segments play independently. Intro-to-loop transitions across separate assets are not implemented yet.
- Changing selection stops playback. The application only reads the installation.

The archive-folder rule follows the locally researched loader detection: recognized loader plus
`data02` selects that folder; otherwise `data01`. This is not runtime verification of game loading.
Preview supports mono/stereo PCM8/16 and Microsoft ADPCM WAVs. Other codecs show an unsupported
message. The entire archive folder is indexed without loading unrelated art payloads into memory.
Sound banks are inspected on demand; this is read-only browsing, not bank editing or repacking.

### Current installation's audio catalog

| Category | Archive | Contents |
| --- | --- | --- |
| Music | music-win.gfs | 100 WAVs |
| Story voices · English | story_vo-win.gfs | 1,486 WAVs |
| Story voices · Japanese | story_vo_jp-win.gfs | 1,486 WAVs |
| UI and shared sound effects | ui-win.gfs | 159 WAVs |
| Character voices and combat · Default | characters-win.gfs | 31 banks, 12,278 embedded WAVs |
| Character voices and combat · Japanese | characters_jp-win.gfs | 18 banks, 6,033 embedded WAVs |

These totals describe this installation, not every game version. Titles and descriptions are broad
archive roles based on archive and asset paths. No transcription or exact event mapping is claimed.

## Version 0.4 clip metadata

This installation has 1,473 English and 1,476 Japanese story WAV filenames that uniquely match a
localization key. These 2,949 links are displayed with the corresponding game text. Thirteen English
and ten Japanese filenames do not match uniquely and retain their filename-derived labels; split
or combined lines are not guessed. Sound banks with no verified line mapping remain identified by
bank and clip number until a personal label is supplied.

Metadata verification is recorded in `.build/checks-metadata/metadata-results.json`:

```powershell
dotnet run --project Tests -p:NuGetAudit=false -- --metadata 'D:\Steam\steamapps\common\Skullgirls' .build/checks-metadata
```

The check covers localization matches, scene evidence, music roles, embedded WAV titles, personal
label persistence/reset, changed-content rejection, corrupt-catalog preservation and unchanged game
archives. This attaches information in the browser/catalog; it does not rename or rewrite installed
WAVs or repack the game's archives.

The announcer/organization check is available as:

```powershell
dotnet run --project Tests -p:NuGetAudit=false -- --organization 'D:\Steam\steamapps\common\Skullgirls' .build/checks-organization
```

## Version 0.3 catalog verification

Validated all **21,542 WAV headers**, decoded 110 representative clips across all six archives and
every one of the 49 banks, and checked that all six installed archive hashes stayed unchanged.
Evidence: `.build/checks-v03/catalog-results.json`. The independent FFmpeg comparison is saved in
`.build/checks-v03/ffmpeg-comparison.json`.

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.build/dotnet-home"
dotnet run --project Tests -p:NuGetAudit=false -- --catalog 'D:\Steam\steamapps\common\Skullgirls' .build/checks-v03
python Tools/verify_managed_decode.py .build/checks-v03/catalog-results.json .build/checks-v03/samples
```

Build with `./Build.ps1`. The output is `dist/SkullgirlsAudioWorkshop/`.
`./Build.ps1 -Portable` requests a self-contained Windows x64 publish; it needs runtime packages
from NuGet. That package was not produced in this session because NuGet was unreachable and the
required runtime packages were not cached. The default publish uses the installed runtime.

## Prototype verification

The core check decodes all 100 installed music assets through the copied managed decoder, checks sample counts
and PCM headers, resolved all configured track references, rejected four malformed inputs, and
confirmed the installed music archive hash was unchanged. Run it with:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.build/dotnet-home"
dotnet run --project Tests -p:NuGetAudit=false -- 'D:\Steam\steamapps\common\Skullgirls' .build/checks
```

Version 0.2 evidence is in `.build/checks-v02/decoder-results.json` and `transport-results.json`.
The transport checks use silence on the real output device to test position, pause, seek while paused,
resume, native range repetition, completion and disposal. These checks do not prove audible seamlessness.
The device startup check allows up to three seconds; this machine did not always advance position
within the original 300 ms deadline. Eight observations of the repeating transport confirm at least
one wrap within the selected range. All 100 managed PCM hashes and lengths match independent
FFmpeg decodes exactly; evidence is in `.build/checks-v02/ffmpeg-comparison.json`.

To rerun only the silent transport checks:

```powershell
dotnet run --project Tests -p:NuGetAudit=false -- --transport .build/checks-v02
```

To rerun the independent comparison after generating decoder results:

```powershell
python Tools/verify_managed_decode.py .build/checks-v02/decoder-results.json Research/run-002/originals
```

The version 0.2 UI was inspected with the library loaded and a moving playhead/time display.
Repeat is preview-only and uses decoded PCM boundaries. It does not author native WAV metadata,
join intro/loop assets, or establish correct in-game playback.

Earlier ACM evidence is in `.build/checks/decoder-results.json` and `independent-decode.json`. For the checked
Moonlit Melee intro, ACM and FFmpeg output 613,632 interleaved samples; the largest sample difference
is 87 on the signed 16-bit scale. Decoder output is not assumed bit-identical between backends.
The window was visually inspected with the installed library; original playback entered its playing
state without error, and a later stopped state and unmapped-asset waveform were observed. No claim
of audible or in-game replacement verification is made from UI state alone.

The first research pass is documented in [Research/FINDINGS.md](Research/FINDINGS.md).
It includes full music decoding, archive round trips, exact-format Windows ACM encoding,
and a local test ZIP. In-game playback remains unverified.

## Reproduce the local research

Requires Python 3.13, FFmpeg/FFprobe on PATH, and Windows with a working Microsoft ADPCM ACM encoder.
Use a fresh output directory each time. These tools read the game and write research outputs outside it.

```powershell
python Tools/research_music.py --game 'D:\Steam\steamapps\common\Skullgirls' --output Research/run-003
python Tools/verify_native.py Research/run-003
```

The first command inventories and fully decodes all music, tests a no-op repack, and records an
experimental FFmpeg conversion with a different block size. The second command uses Windows ACM
to build the fixed-duration, native-block stage test. Only the second command creates the test ZIP.

The optional Manager check needs the sibling `Skullgirls Mod Manager` source tree and .NET 9:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.build/dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = 'D:\Steam\ChatGPT\Skullgirls Mod Manager\.packages'
dotnet run --project Tools/ManagerCheck -p:NuGetAudit=false -- 'D:\Steam\steamapps\common\Skullgirls' Research/run-003
```

This imports into an isolated library inside the research output, then builds and validates an archive
there. It does not apply anything to the installed game or change Manager's normal library.
The research tool's dependency on Manager is optional. The browser's `Core` and `App` projects have
no Manager runtime reference or third-party NuGet dependencies. The copied decoder runs in managed code;
Windows supplies the output device API. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and
[LICENSE](LICENSE) for the reused GPL-3.0 source and provenance.

Research outputs contain audio extracted from the user's installation. They are local fixtures and
are excluded from source control. The ZIP is a private compatibility test, not a redistributable release.
