# Source reuse

## Optional runtime archive compatibility

The manager can install a supported, user-supplied `CoreMessaging.dll` runtime compatibility component (SHA-256 `014bfb8c48fa7afbcd585a34d814011b96b289c742482a01376f56aafa1e11d3`). This component is not included in the repository or release packages. Its authorship and licensing remain with its original distribution. The manager's installation code is original and does not modify the game executable. The checksum identifies the supported binary; the containing ZIP's name and folder layout do not matter.

This is an independent Skullgirls adaptation, not an official release from the original authors or the game developers.

## Velvet Beautifier

Source: https://github.com/ThemModdingHerds/velvet-beautifier

Copied from commit `ce2b09e63531bda79e33990a89f5ab866b9f591b`.

Copyright and authorship remain with the original contributors, including N1ghtTheF0x. License: GPL-3.0, reproduced in `LICENSE`. This derived project is distributed under GPL-3.0.

- `Core/Vendor/Velvet/ModInfo.cs`: copied metadata model and serialization interface.
- `Core/Vendor/Velvet/Crypto.cs`: copied SHA-256 utilities.
- `Core/Mods.cs`: adapted Mod / ModDB enable markers, folder imports, library and GFS replacement layout. TFH-specific features removed; safe staging, ZIP import and explicit ordering added.
- `Core/Manager.cs`: adapts the backup / revert / apply workflow from BackupManager and RevergePackageManager. Replaced global filename-based backups with installation profiles, integrity tracking and recoverable transactions.
- `Core/FileSystem.cs`: minimal replacement for the upstream JSON helper required by ModInfo.

The original TFH lobby, executable patches, TFHRES, stage pack, news injection, GameBanana identifiers and Terminal.Gui UI were not carried over because they do not fit this Windows Skullgirls GUI.

## ThemModdingHerds.GFS

Source: https://github.com/ThemModdingHerds/gfs

Copied from commit `c6cc38ed785687d1e347a249a88e2bae9f154902` (project version 2.1.2).

`Core/Vendor/GFS/*.cs` retains the upstream namespace and source. MIT license reproduced in `licenses/GFS-MIT.txt`.

`Core/Archives.cs` uses the copied header/entry types and header serializer. It adds a bounded index reader, streaming payload copying and independent payload verification. It calculates the full table size before writing payloads; it does not use upstream in-memory merging when applying mods.

## ThemModdingHerds.IO 1.3.1

NuGet dependency used by the copied GFS source: https://www.nuget.org/packages/ThemModdingHerds.IO/1.3.1

MIT license reproduced in `licenses/IO-MIT.txt`, copied from the package. The app publish output includes these third-party notices and licenses.

## BCnEncoder.Net 2.3.0 and CommunityToolkit.HighPerformance 8.4.0

PNG/JPEG/BMP replacements are encoded as matching DDS textures using [BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET), Copyright 2020 Nomi Lakkala, under the MIT option of its dual license. The pinned package's source commit is `74eda8a330759d81e6307f19aefc547e40687825`; license included in `licenses/BCnEncoder-MIT.txt`.

Its managed dependency CommunityToolkit.HighPerformance is licensed by the .NET Foundation and Contributors under MIT. The package license and notices are included in `licenses/CommunityToolkit-MIT.md` and `licenses/CommunityToolkit-ThirdPartyNotices.txt`. No ImageSharp dependency is used; ordinary image input uses Windows System.Drawing.
