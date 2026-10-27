# 0.10.0 — multi-song projects and loop editing

Save multiple replacements in one **.sgawmod** project, export one mod folder or Manager-compatible ZIP, and apply the collection with one verified archive transaction and Restore backup.

- Add, edit, import and remove songs with independent sources and processing settings; conflicting targets are rejected.
- Preserve original-format source copies and recover collection drafts.
- Crop a song's ending and optionally start and loop from source frame zero, ignoring its intro markers.
- Drag one supported source audio file onto Side A or the waveform to import it.
- Drag intro, loop and crop-end markers directly, including when zoomed. Escape cancels the current drag.
- Continue opening existing single-song .sgawproj files.

Synthetic checks cover imports, projects, exports, multi-song installation/recovery, cropped output and intro bypass. A local 21-song test replaced 42 assets and verified that 58 unrelated entries were unchanged. The user confirmed clean full-song looping on one stage and clean separate-intro playback/looping on another. This is limited listening coverage, not acceptance of every stage or system. Minor static was reported once at the initial start of the full-song track; comparison of the source and converted opening found no added startup burst, but its cause remains unresolved.

Download **SkullgirlsAudioWorkshop-0.10.0-win-x64.zip**, extract the entire archive to a writable folder outside the game, and run **SkullgirlsAudioWorkshop.exe**. The .NET runtime and decoder are included. Preserve **installation-backups**, **drafts**, **mod-drafts**, personal labels and project/source folders when updating. See [the mod project guide](MOD-PROJECTS.md) and [compatibility limits](COMPATIBILITY.md).

Game assets, user soundtrack files and private test projects are excluded. Matching application source, FFmpeg source/build script and SHA256SUMS.txt accompany the release.

---

# 0.9.0 — first public release

A standalone Windows x64 music replacement editor with Skullgirls-inspired Art Deco styling.

- Modern Steam discovery and searchable audio library: music, story voices, announcers, UI sounds and character banks.
- Source/target record decks, spinning playback display, waveform source editing, intro/loop cuts, gain, fades and optional loop crossfade.
- WAV, MP3, FLAC, OGG/Opus, M4A/AAC, WMA and AIFF imports, including higher-depth/float WAV.
- Native ADPCM generation with independent output verification and converted playback.
- Editable projects, copied original-format sources, recovery drafts, native exports and optional Manager-compatible ZIP exports.
- Single-music-archive Apply/Restore with scoped backups, journals, staged verification and restart recovery.
- Self-contained .NET runtime and a minimal FFmpeg decoder with corresponding source/build information.

The user confirmed changed main-menu music and two clean loops; Restore returned the exact original archive hash. The downloaded release package has been confirmed working by the user. Compatibility limits are documented in COMPATIBILITY.md. The editor exposes 24 configured intro/loop music pairs; voice/SFX replacement and arbitrary singleton/menu editing are not included.

Download the win-x64 ZIP, extract everything to a writable folder outside the game, and run SkullgirlsAudioWorkshop.exe. Keep installation-backups, drafts, personal labels and projects when updating. Game assets and compatibility loaders are not included.

The same release provides matching Workshop source, FFmpeg source, its build script and SHA256SUMS.txt. See the GitHub Actions run for automated validation of the release commit.
