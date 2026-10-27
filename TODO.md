# Planned features

Added 27 October 2026. Implemented for version 0.10.0. Automated collection and crop/bypass checks pass; local listening covered full-song looping and a separate-intro track. Broader acceptance remains open.

## Multi-song mod projects

- [x] Add multiple music replacements to one project, each with its own source, cuts, gain, fades and loop settings.
- [x] Save and reopen the complete collection, retaining original-format source copies/relinking and target fingerprints.
- [x] Add, edit and remove songs; prevent duplicate or conflicting target asset replacements.
- [x] Export the collection as one mod folder or Manager-compatible ZIP with shared mod details and verified payloads.
- [x] Apply all selected songs together by staging and verifying one rebuilt music archive.
- [x] Preserve unrelated entries and provide one scoped backup, journal, recovery and Restore operation for the collection.

## Loop preparation controls

- [x] Make cropping the end of a song explicit in the source editor, using waveform and numerical loop-end controls.
- [x] Audition the cropped end-to-start seam repeatedly before generation, and audition the actual converted result afterward.
- [x] Add a per-song checkbox to start playback and looping from the source's beginning, ignoring the selected intro snippet.
- [x] Generate one native codec block of silent intro and loop source frame zero through the crop end. Structure and decoding are verified; local full-song looping was confirmed by listening.
- [x] Save both options per song and preserve their behavior across project reopening, collection exports and Apply.

## Import and waveform editing

- [x] Import a source by dragging one supported audio file onto Side A or the waveform.
- [x] Drag intro, loop and crop-end markers with zoom/scroll support and Escape cancellation.

## Acceptance

- [x] Round-trip a multi-song project with different source formats and independent settings.
- [x] Verify exported folders/ZIPs and reject duplicate targets or stale generated audio.
- [x] Verify collection Apply/Restore and interrupted-operation recovery without changing unrelated assets.
- [x] Confirm local cropped full-song looping and separate-intro playback/looping in-game.
- [ ] Expand listening to repeated boundaries across more targets and investigate the isolated initial-static report.
