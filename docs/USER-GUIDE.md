# User guide

## Library and playback

The current Steam installation is discovered automatically. Select a category to browse music, announcers, story dialogue, UI sounds or character banks. Search readable titles, characters or internal paths. Configuration and localization provide descriptions where verified; unknown voice lines retain their asset identity and numbered clip. Personal labels do not modify game audio.

Play, pause, seek and loop the selected clip. Library repeat ranges affect audition only. Select a verified intro/loop music target and use Replace music to open the editor. Unsupported targets remain read-only.

## Side A and Side B

Side A is your source; Side B is the game target and converted replacement. Import audio leaves the original file unchanged. The source editor provides waveform zoom/scroll, numerical boundaries and intro-to-loop/seam audition. Its accepted cuts return to the main editor. Gain, fades and optional crossfade are baked into generated audio; crossfade shortens the loop by its overlap.

Preview edits checks processing before conversion. Generate & verify creates native ADPCM files, verifies their structure and independently decodes them. Play converted auditions those actual generated files. Changed edits invalidate the previous conversion. Codec blocks can shift requested durations slightly; the review displays that adjustment and clipping measurements.

## Projects

Save writes a .sgawproj containing the target, original source hash, cuts, processing and export details. Copy source is on by default: keep the accompanying .sgawproj.sources folder with the project. Copied sources retain their original encoded bytes and extension. Relative references allow moving the project and source folder together.

Missing sources can be relinked. Changed sources or target hashes require review, and reopening regenerates output rather than trusting old conversion caches. Atomic saves and idle recovery drafts protect editing progress. Recover draft opens retained work; closing offers Save, Discard or Cancel.

## Export

Export WAVs creates a fresh folder with the target-format intro/loop files and a validation report. Export mod ZIP creates a Manager-compatible package with editable name, author, numeric version and description. These actions do not install anything. Review source, target, edits and converted playback before exporting. Existing exports are not silently overwritten.

Mod Manager is optional. Import the ZIP there through its normal workflow; do not have two tools apply changes to the same installation simultaneously.

## Apply, Restore and recovery

Close Skullgirls. Open Apply / Restore and prepare the replacement. Review the exact game folder, data folder, assets and backup before Apply. Preparation stages and verifies the complete music archive without replacing the active file. Apply verifies inputs again and replaces that one archive atomically.

One Workshop transaction can be active per installation. Restore it before applying another replacement. Restore pre-apply music uses the retained verified backup; it restores the state present before this Apply, including older mods. It refuses unexplained external changes instead of overwriting them. If an operation is interrupted, reopen Workshop, inspect Apply / Restore and use Recover. Keep all backup/journal files until recovery or restoration is complete.

Backups live in installation-backups beside the application, scoped to the installation and archive folder. Generated files are disposable conversion output; backups are not. Keep the application folder writable and retain these files during updates.

## Troubleshooting

- Missing decoder: re-extract the entire release ZIP, including tools/ffmpeg. Do not copy only the EXE.
- Encoder unavailable: the Windows ADPCM codec rejected the target. Keep the game unchanged and report the displayed ACM code.
- Source too long/large: trim it in an audio editor before importing. Limits are rejection limits, not automatic cuts.
- Archive/source changed: reopen and review the current files. Do not force old prepared output over another tool's work.
- No game discovered: select the folder containing SkullGirls.exe manually. The app does not include the game or a loader.
- Unexpected music: check the selected stage and any game-side music selection. Successful conversion alone does not establish which track the game played.
