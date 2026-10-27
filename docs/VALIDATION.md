# Validation record

## Local development checks

- Native decoding, all 24 configured music target profiles and their 48 original formats; native encoding independently decoded and compared with FFmpeg.
- Project settings, copied/relocated sources, relinking/review, atomic saves, drafts and conflict handling.
- Native and mod ZIP exports, exact payload verification, metadata, stale-input rejection and optional isolated Manager import.
- 22 local installation/recovery scenarios, including interrupted checkpoints, low disk space, external changes, corrupt backup/journal, racing writers and changed build/location.
- Full real-archive no-op identity and replacement repacks preserving all untouched entries.
- Twelve import format/layout cases, each decoded, converted to native ADPCM and reopened from an original-format copied project. Includes corrupt/unknown inputs, cancellation and source/decoded-size limits.
- Desktop UI import check showing an MP3 waveform with editing enabled.
- User-confirmed main-menu replacement and two clean loop boundaries, followed by byte-identical Restore.

Historical local records are summarized in DEVELOPMENT-HISTORY.md and Research/FINDINGS.md. Private machine-specific reports and game-derived fixtures are deliberately excluded from the repository.

## Release CI

The Release validation workflow creates an entirely synthetic GFS installation and native ADPCM template, then runs imports, generation, projects, exports and installation/recovery checks. The codec input fixtures are generated sine waves, not game audio. A clean Windows runner has no real Skullgirls installation or other Skullgirls tools.

The workflow also builds a self-contained application, checks startup with an empty DOTNET_ROOT, and packages only clean publish output plus matching application and decoder source. The corresponding workflow run is the authoritative result for the release commit. Synthetic tests establish code/codec behavior, not new in-game compatibility claims.

## Remaining acceptance

Broader game builds and stage/alternate-music behavior, an explicitly isolated intro transition, audible post-Restore menu verification, full clean-user-PC interaction and broader OS/power-loss acceptance remain open. Version 0.9.0 was promoted to a stable release after the user downloaded the published package and confirmed it works. This confirmation does not expand the documented game-build or target scope.
