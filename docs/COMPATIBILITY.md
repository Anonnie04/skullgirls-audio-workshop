# Compatibility and known limits

Version 0.9.0 is a preview for Windows x64. The portable package includes .NET and FFmpeg; native encoding uses Windows ACM. The CI startup check and isolated fixtures do not replace full testing on a clean user's PC.

## Game evidence

Local research used modern Steam app 245170, manifest build 14696341. The tested executable SHA-256 was FB757433D2D11602DED18040078E70319AFDAE98C35FC22D789B38D012AD400B.

On that installation the recognized local CoreMessaging.dll hash was 014BFB8C48FA7AFBCD585A34D814011B96B289C742482A01376F56AAFA1E11D3. With that loader and data02 present, Workshop selects data02; otherwise it selects data01. The running game's loaded modules and open handles confirmed data02/music-win.gfs on this specific setup. Other custom loaders and clean-install loader requirements are unverified. No loader is included or installed by Workshop.

The observed music-win.gfs uses Reverge Package File 1.1, alignment 1, with native stereo MS ADPCM, 140-byte blocks and 128 samples per channel per block. Sample rates vary per asset. Unsupported layouts are rejected. The editor exposes 24 music.ini-backed pairs. Arbitrary singleton music and bank replacement are not supported.

## In-game result

A diagnostic changed only the main-menu intro and loop to Moonlit Melee. The user identified the new song and reported two loop boundaries without unusual sound, silence or clicks. Restore returned the entire archive to its exact pre-apply SHA-256. Explicit isolated intro-transition and post-Restore audible checks remain open.

An earlier attenuated stage test was inconclusive because the intended volume difference was not heard. Do not count its normal stage/re-entry observations as proof that the replacement was loaded. Broader stage-family, alternate-music and game-build acceptance remains open.

## Scope

Voice, announcer, dialogue and sound-effect browsing are implemented; individual bank replacement, event mappings, new sound events, new music slots and multi-archive installation are outside this release. Some descriptions derive from filenames; exact spoken labels are shown only where linked to verified localization or personally supplied.

Apply changes one music archive. It preserves unrelated entries and includes staged verification, scoped backups, journaling and recovery guards. It does not promise generic multi-archive transactions or immunity to every OS/filesystem/power failure.
