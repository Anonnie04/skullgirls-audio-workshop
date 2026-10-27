# Skullgirls Audio Workshop — product proposal

Date: 26 October 2026  
Status: Original specification; the version 0.9.0 music workflow is implemented in the public 0.9.0 release. See [STATUS.md](STATUS.md) for current evidence, supported scope and remaining release acceptance.

## Purpose

A standalone Windows application for creating Skullgirls music replacements, with voice and sound-effect editing planned for a later release. It should turn selecting, preparing, converting and installing audio into a reviewable workflow without requiring users to manipulate archives manually.

The first release focuses on replacing existing music. It does not promise new music slots, new game events or changes to the game's audio engine.

## Standalone design

Users need their own supported game installation and source audio. They do not need Sprite Workshop, Palette Editor or Mod Manager.

The application owns its project format, conversion pipeline, archive handling, installation backups and recovery workflow. It can reuse appropriately licensed code, but its portable release must include the components required for its supported features.

Mod Manager integration is optional: users can export a complete replacement mod ZIP and import it into Manager. Ordinary projects, native audio exports and local Apply/Restore remain independent. A missing Manager must not generate startup warnings or block any standalone feature.

## First release: music replacement

### Browse and inspect

- Select a game installation and discover supported music assets.
- Show readable track or stage names where mappings have been verified; retain internal paths for identification.
- Preview the original audio and show duration, codec, sample rate and channels.
- Identify related intro and looping assets where the selected game version provides a verified relationship.
- Mark unsupported assets clearly rather than guessing their format or purpose.

### Prepare replacement audio

- Import WAV, MP3, FLAC and OGG through a validated decoding backend.
- Display a waveform with zoom, seeking and numerical selection controls.
- Select an intro and looping section, subject to the target's supported playback structure.
- Audition the intro-to-loop transition and repeated loop boundary.
- Adjust gain and add fades; offer a deliberate crossfade option where it can be baked into the generated audio.
- Show clipping indicators and provide an original/replacement comparison at consistent playback volume.
- Preserve the user's source file; all editing is represented by project settings and generated outputs.

The application must not assume that every track uses the same two-file arrangement. If the game has no usable loop metadata, the editor must prepare the appropriate audio segments rather than invent unsupported loop-point fields.

### Convert and verify

- Derive required output properties from the selected target and verified format rules.
- Convert to the required codec, sample rate and channel layout.
- Decode the generated output again to verify structure and inspect duration, levels and boundaries.
- Let users preview the converted result before exporting or applying.
- Explain lossy encoding and any boundary or duration adjustments caused by codec constraints.

A successful conversion is not proof of correct in-game playback. Each supported target family needs an in-game verification case.

### Save, export, apply and restore

- Save an editable local project with source references, edits, target identity and export details.
- Export native replacement files without modifying the game.
- Export a complete mod ZIP with replacement paths and editable name, author, version and description.
- Apply directly with verified backups, staged archive generation and installed-file verification.
- Restore the pre-apply state from the app's own verified backups.
- Recover an interrupted installation before allowing another write operation.

Package export must not require Manager to be installed. Support for loading modified archives must be verified separately; if a target needs additional game-side setup, explain that requirement without making Manager mandatory.

## Main user workflow

1. Choose the Skullgirls installation.
2. Select a supported music target and listen to its original audio.
3. Import a replacement source file.
4. Adjust the intro, loop, gain and fades as appropriate for that target.
5. Generate and audition the converted result.
6. Save the project, then export files, export a mod ZIP or Apply locally.
7. Close the game before local installation; review the affected archives and backup location.
8. Test playback in-game. Use Restore if needed.

The review should identify the actual target assets and archive folder. A title such as a stage name is helpful context, but cannot replace exact target identification.

## Proposed interface

- **Library:** target search, verified grouping, original playback and format information.
- **Editor:** waveform, intro/loop selection, gain, fades and transition audition.
- **Review:** converted playback, target compatibility, validation results and affected files.
- **Project actions:** Save project, Export audio, Export mod ZIP, Apply and Restore.

Long operations run outside the UI thread and report their current phase. Preview playback stops cleanly when the target changes or the application closes. Cancel is available only at stages where it can leave a consistent state.

## Project and storage model

A versioned project records:

- Selected target paths and relevant source-archive fingerprints.
- Source audio references and hashes, with an option to copy audio into the project.
- Edit parameters, conversion settings and export metadata.
- Validation results tied to the particular generated output.

Use atomic project saves and recoverable drafts. Missing or changed source files must produce a relink/review workflow rather than silently reusing stale output. Regenerate previews when edits or conversion settings change.

Store installation backups separately from disposable conversion caches. Scope backup records to the installation and active archive folder. Cleanup must not remove restore data as if it were temporary audio.

## Installation correctness

Local Apply must operate safely even if another tool previously changed the game:

- Inspect the actual selected installation and determine the active archive location using verified rules.
- Refuse stale preparations if target archives changed after review.
- Stage and verify all affected outputs before replacing game files.
- Preserve unrelated archive entries and maintain a persistent transaction journal.
- Keep rollback copies and provide restart recovery after interruption.
- Refuse to overwrite unexplained external changes during restore.
- Explain that a backup captures the user's current pre-apply state; it is not necessarily an unmodified Steam original.

Do not silently adopt another tool's backup baseline or require its private state. Optional Manager export uses Manager's ordinary package installation workflow instead of having both applications write the same transaction.

## Research gates before committing to supported formats

1. **Inventory current assets:** identify music containers, codecs, configuration references and intro/loop relationships from the user's installation.
2. **Prove round-trip handling:** extract and decode representative assets, produce valid replacements, and verify archive repacking preserves unrelated entries.
3. **Verify playback:** test stage entry, intro transition, repeated loops, transitions away from a stage and any relevant alternate music behavior.
4. **Validate conversion tooling:** confirm encoding support, Windows packaging, dependency licenses and redistribution requirements. FFmpeg is a candidate, not an already selected dependency.
5. **Verify install locations and loader requirements:** determine which targets work directly and which require additional setup on supported builds.

Document the supported game versions and target families resulting from these checks. Unknown formats remain unsupported until verified.

## Later release: voice and sound-effect banks

After music workflows are reliable, investigate browsing and editing individual clips inside sound banks.

Potential features include searchable bank entries, clip playback, batch replacement, conversion to each entry's required format and rebuilding banks while preserving untouched clips and structural metadata.

The existing [SndWavRepacker project](https://github.com/Devyatyi9/SndWavRepacker) documents Skullgirls sound-bank unpacking and repacking and is a useful research reference. Audit its format assumptions, current-game compatibility and license before reusing code. This proposal does not assume that its output is already validated for the user's installation.

Do not promise readable voice-line labels or event remapping until reliable mappings have been established. Adding sound events and changing voice triggers are outside the initial bank-editing scope.

## Release acceptance criteria

- Complete the music workflow on a machine with none of the other Skullgirls tools installed.
- Decode each advertised input format and reject malformed or unsupported input clearly.
- Verify converted assets independently of the preview buffer.
- Audition transitions accurately, then confirm representative replacements in-game.
- Reopen projects without losing edits; recover from missing sources and interrupted saves.
- Demonstrate that Apply preserves unrelated assets and Restore returns the verified pre-apply state.
- Exercise interrupted multi-file installation, insufficient disk space and externally changed archives using isolated fixtures.
- Export a self-contained mod ZIP without Manager; test its import separately when Manager is available.
- Package the runtime and required conversion components so the portable release does not rely on a development environment.

## Suggested delivery order

1. Research current music formats and prove one complete replacement in-game.
2. Build standalone target browsing, decoding and original playback.
3. Add waveform editing, conversion and converted-output preview.
4. Add projects, draft recovery and native/package exports.
5. Add verified local Apply, Restore and interruption recovery before calling the standalone first release complete.
6. Expand supported music targets using verified fixtures.
7. Investigate sound-bank editing as a separate milestone.

The first prototype can be narrow, but the release should provide a complete music-replacement workflow on its own. Optional integration must not determine the core delivery schedule.
