# Fallout 1 DAT1 and Fallout 2 DAT2 format investigation

## Format findings

The `.dat` extension does not identify one unique format. Fallout 1 and Fallout 2 use different formats, commonly called DAT1 and DAT2 respectively. FAE has separate DAT1 and DAT2 readers, and `FalloutArchive.Open` detects them by validating the archive structure rather than relying on its filename or extension.

The current structural reference is the DAT1 reader documentation and implementation in [FIFE's DAT1 header](https://github.com/fifengine/fifengine/blob/master/src/fife/vfs/dat/dat1.h) and [DAT1 reader](https://github.com/fifengine/fifengine/blob/master/src/fife/vfs/dat/dat1.cpp). Its [LZSS decoder header](https://github.com/fifengine/fifengine/blob/master/src/fife/vfs/dat/lzssdecoder.h) documents the block encoding. The DAT1 implementation describes this layout:

1. A 16-byte header containing four big-endian 32-bit words: folder count followed by three fields treated as unknown metadata.
2. One folder-name record per folder: an unsigned 8-bit byte length followed by that many name bytes.
3. One folder-content record per folder, beginning with four big-endian 32-bit words: file count and three metadata fields.
4. Each file record contains an 8-bit-length-prefixed name, then four big-endian 32-bit fields: type, data offset, unpacked size, and packed size.
5. Entry data is read from its recorded offset. FIFE identifies type `0x20` as raw and `0x40` as LZSS-compressed.
6. FIFE's LZSS decoder documents signed big-endian 16-bit block lengths: zero ends the stream, a negative value means copy the following `-N` bytes raw, and a positive value gives the size of the following LZSS token block. The token stream uses a 4 KiB ring buffer, 18-byte maximum matches, and the standard grouped flag-byte/literal/back-reference layout.

This is an implementation-derived format description, not yet a verified specification for every release. FIFE's reader is useful corroborating evidence, not a dependency or copied implementation. The FAE reader is implemented independently in `src\FalloutAssetExplorer.Core\Dat1Archive.cs`; it validates counts, lengths, offsets, and stored data sizes against archive bounds. For type `0x20`, FIFE reads `unpacked_size` bytes at the recorded offset; it does not use `packed_size` to determine the raw payload length. Consequently, FAE preserves `packed_size` as metadata but uses `unpacked_size` for raw payload bounds and reads. `packed_size` is shown as unavailable for raw entries in the browser. Unknown entry types remain enumerable but report an unsupported-type error if read.

The reader has conservative limits of 65,536 folders, 250,000 entries, a 4 GiB archive/address space, and 512 MiB packed or unpacked data per entry. Raw entries are copied in bounded chunks. LZSS blocks are checked against the declared packed and unpacked lengths and decoded into bounded buffers; no allocation is based directly on an untrusted unpacked-size field.

The [Fallout Wiki DAT page](https://fallout.wiki/wiki/DAT_file_format) explicitly distinguishes DAT1 and DAT2. Its detailed tree/footer and zlib description is for DAT2 and is not evidence for the Fallout 1 format.

## Fallout 2 DAT2 structure and implementation

The DAT2 layout is cross-checked against the [Kaitai Struct Fallout 2 DAT specification](https://formats.kaitai.io/fallout2_dat/), the [Fallout 2 Community Edition file reader](https://github.com/fallout2-ce/fallout2-ce/blob/master/src/dfile.cc), and the [Fallout 2 DAT format reference](https://fodev.net/files/fo2/dat.html). The maintained [Fallout 2 format notes](https://github.com/rotators/fallout2-docs/blob/master/content/pages/dat.md) compare the engine code, formal parser schema, older community documentation, and real archive behavior. The implementation does not use or copy an external archive tool.

DAT2 has no global magic number. The checked layout is a data region, a flat directory tree, and an 8-byte footer:

| Position | Field | Representation |
| --- | --- | --- |
| Last 8 bytes, first word | Tree size | Little-endian `uint32`, includes the file count and all entry records |
| Last 4 bytes | Data size | Little-endian `uint32`, physical file length of the DAT data region including tree and footer |
| `physical length - data size` | Data base | Start of the DAT data region; normally zero |
| `physical length - tree size - 8` | Directory start | File count followed by the flat entry records |

The first directory word is a little-endian file count. Each record contains a little-endian `uint32` path-byte length, that many path bytes, a one-byte compression flag, then little-endian `uint32` uncompressed size, stored size, and data offset. Paths are full resource paths, not nested directory records. The documented encoding is ASCII; FAE rejects non-ASCII path bytes and preserves the stored spelling in `Dat2ArchiveEntry.StoredPath`. Its browser path normalizes backslashes to forward slashes.

The stored data offset is relative to the data base. Flag `0` means uncompressed bytes, for which stored and uncompressed sizes must match. Flag `1` means a zlib-wrapped DEFLATE stream (not gzip and not raw DEFLATE). The archive reader bounds input to the entry's declared stored range, streams output through a fixed-size buffer, and requires exactly the declared uncompressed size; it does not allocate an output buffer from the archive's size field. Empty raw entries and valid zlib-compressed empty entries are supported. The parser checks every footer, tree, record, path, count, offset, and size against the physical file before accepting the directory. Entry payloads are checked against the data area and cannot overlap the directory. Duplicate paths are rejected case-insensitively, as they make entry identity ambiguous. DAT2 entry ordering is preserved but not required for the browser, which does not implement the engine's binary-search resource lookup.

The current defensive limits are 250,000 entries, a 128 MiB directory tree, 4,096 bytes per path, and 512 MiB per entry for either stored or uncompressed data. Compression is validated when an entry is read, not by eagerly decompressing every archive member during browser loading. The reader holds no persistent file handles; each requested entry read opens a read-only stream and confines reads to the entry's bounded data range.

`IGameArchive` supplies the shared archive path, game and format identity, browser entries, and entry-copy operation. `Dat1Archive` and `Dat2Archive` implement that contract while retaining their typed entry metadata and format-specific validation/decompression. The UI uses the shared browser-entry list for search, filtering, sorting, and display. Fallout 1 and Fallout 2 combined FRM entries use the shared decoder; each game resolves only its own root `COLOR.PAL`, and the resolver does not apply a Fallout 1 `MASTER.DAT` palette to a Fallout 2 archive (or vice versa).

## DAT2 verification

Synthetic coverage in `tests\FalloutAssetExplorer.Core.Tests\Dat2ArchiveTests.cs` covers empty and multi-entry archives, normalized nested paths, raw and zlib reads, compressed and uncompressed empty entries, prefixed data regions, format detection without a `.dat` extension, malformed footers/directories/counts/paths/flags/sizes/offsets, case-insensitive duplicates, corrupt zlib streams, and declared-output-size violations. `Dat1ArchiveTests.cs` also verifies that DAT1 implements the shared identity and entry-reading contract and continues to support its zero-packed-size raw-entry behavior.

The local Fallout 2 installation supplied these real DAT2 archives, parsed read-only through `FalloutArchive.Open`:

| Archive | Entries |
| --- | ---: |
| `master.dat` | 23,140 |
| `critter.dat` | 7,120 |
| `patch000.dat` | 489 |
| `f2_res.dat` | 177 |

At least one available raw and/or zlib-compressed entry in each archive was copied through the shared API and its returned byte count checked against the directory's uncompressed size. These local reads validate the listed installations only; they do not establish support for every Fallout 2 archive variant. No Fallout 2 game data was added to the repository. DAT2 writing/repacking and resource overlay lookup remain out of scope.

The final Release build for this milestone succeeded with 0 warnings and 0 errors. The complete Release test suite passed 111 tests (0 failed, 0 skipped). The Release application also launched with no archive selected and closed normally. The GUI archive-picker flow was not manually exercised; real archive directory parsing and entry reads were verified through the Core reader.

## Verification plan

Original archives were found at `D:\SteamLibrary\steamapps\common\Fallout\CRITTER.DAT` and `D:\SteamLibrary\steamapps\common\Fallout\MASTER.DAT`. `CRITTER.DAT` opens and enumerates 5,459 entries. An independent read-only directory traversal of `MASTER.DAT` confirmed that the previous raw-size equality check rejected a real directory record, not a misparsed record:

| Field | Value |
| --- | --- |
| Folder / entry | `ART\SCENERY\TEMP.REF` |
| Record start (decimal / hex) | `52,056` / `0xCB58` |
| Filename-length byte / filename bytes | `52,056` / `52,057–52,064` |
| Type field position / value | `52,065` / `0x00000020` |
| Data-offset field position / value | `52,069` / `202,277,500` |
| Unpacked-size field position / value | `52,073` / `4` |
| Packed-size field position / value | `52,077` / `0` |
| Directory position after record | `52,081` |

The four 32-bit fields decode big-endian at exactly the offsets specified by the DAT1 layout. The error was an unsupported format assumption: raw type `0x20` stores four bytes even though `packed_size` is zero. FAE now validates and reads that raw payload using `unpacked_size`, consistent with FIFE's `RawDataDAT1` implementation. Synthetic tests are not evidence of compatibility with every game release.

Synthetic coverage in `tests\FalloutAssetExplorer.Core.Tests\Dat1ArchiveTests.cs` includes:

- Empty archive and unusual legacy filename bytes.
- Valid raw entries, compressed LZSS literal/back-reference data, and LZSS raw blocks.
- Truncated headers and folder names, impossible file/folder counts, out-of-bounds and directory-overlapping offsets, zero packed size on a raw entry, and per-entry size limits.
- Unsupported types, truncated LZSS references, and cross-archive entry misuse.

After the fix, both local archives were opened read-only through the FAE parser. `CRITTER.DAT` enumerated 5,459 entries. `MASTER.DAT` parsed successfully, and the `ART/SCENERY/TEMP.REF` entry with packed size zero was read to a temporary stream as four bytes. The archives' recorded file lengths and modification timestamps were unchanged by testing. This confirms directory parsing for these local copies and raw reading of the regression entry; it does not validate every compressed entry or establish compatibility with every release. Keep copyrighted game files out of the repository and test fixtures.

The UI enumerates entries from both formats and the core readers support copying individual entries to a caller-provided stream. The user-facing extraction service reuses that streaming contract, preserves the existing reader size/decompression bounds, validates archive paths at the filesystem boundary, and stages writes through temporary files. Filesystem links beneath the selected destination are rejected by checking reparse-point attributes; this is best-effort rather than race-resistant against concurrent external changes.

## Fallout FRM format investigation

### Verified layout

Fallout 1 and Fallout 2 FRM files use the same big-endian layout. This is supported by the Fallout 1 Community Edition reader/struct declarations and the independent `glisomorph` file-spec investigation, which cross-checks Fallout 1 and Fallout 2 readers and shipped archives. The Fallout 1 reader's in-memory `Art` struct is not itself the file header: runtime padding fields are not present in the serialized 62-byte header.

| File offset | Stored field | Representation |
| --- | --- | --- |
| 0 | Version | unsigned 32-bit big-endian; shipped files use 4, with a known Fallout 1 `oknife.frm` using 3 |
| 4 | Frames per second | signed 16-bit big-endian; zero means 10 FPS |
| 6 | Action frame | signed 16-bit big-endian |
| 8 | Frames per direction | signed 16-bit big-endian |
| 10 | Six direction X shifts | six signed 16-bit big-endian values |
| 22 | Six direction Y shifts | six signed 16-bit big-endian values |
| 34 | Six direction data offsets | six unsigned 32-bit big-endian offsets relative to byte 62 |
| 58 | Frame-data byte count | unsigned 32-bit big-endian |
| 62 | Direction/frame records | variable-size frame data |

Directions are indexed clockwise as NE, E, SE, SW, W, NW. Each frame begins with a 12-byte record: signed 16-bit width and height, unsigned 32-bit pixel-data length, then signed 16-bit X/Y frame offsets. Exactly `pixel-data length` bytes of palette indices follow; shipped frame data has `width * height` bytes, top row first and left-to-right within each row. FRM pixel data is not compressed.

The six direction offsets point to frame-block starts. When a direction offset equals its immediately preceding direction offset, that direction shares the preceding frame block; otherwise it has a new block. Standard combined `.FRM` files have one shared block or six direction blocks. Frame offsets describe per-frame motion/alignment; the header's per-direction shifts are separate metadata. The FRM header FPS and action-frame fields are retained as metadata. FAE's animation preview uses a separate user-selected preview rate; it does not claim game-accurate playback timing or use unverified timing behavior.

Palette index 0 is transparent in the FRM/object drawing paths used for sprites. The FRM stores indices, not RGB colors. Fallout 1's ordinary game rendering uses the root `color.pal`; a small number of interface/movie scenes explicitly load other palettes. Palette cycling (indices 229–254 in the cited investigation) is runtime palette behavior, not FRM compression.

### Differences, limits, and assumptions

- Fallout 1 and Fallout 2 readers use the same FRM structure. Version 3 exists in at least one shipped Fallout 1 asset; the game ignores the version field. The decoder accepts versions 3 and 4 and rejects other values rather than inferring compatibility.
- Split critter files `.fr0`–`.fr5` have separate naming/selection behavior, and their `data size` field may describe all six files, not the single file's bytes. This preview is scoped to `.FRM`; split-file routing is not implemented.
- The source investigation reports that combined shipped `.FRM` files have exact data-size and frame-boundary agreement. The decoder requires those bounds to agree and rejects gaps, backward/non-adjacent direction references, unexpected padding, zero/negative dimensions, or pixel lengths different from `width * height`.
- Without the appropriate palette, correct colors cannot be inferred from index values. FAE uses a clearly labeled diagnostic grayscale mapping when its root default palette is missing or invalid; it does not infer a scene palette from the FRM basename.
- The full renderer's anchor-placement rules, palette cycling, runtime animation timing, and game-specific FID selection remain outside this preview.

### Sources consulted

- [Fallout 1 Community Edition `art.h`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcf/src/game/art.h): serialized/runtime frame structures (`Art`, `ArtFrame`) and frame APIs.
- [Fallout 1 Community Edition `art.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcf/src/game/art.cc): Fallout 1 frame access, frame layout use, offsets and FPS behavior.
- [Independent FRM file-spec investigation](https://github.com/joankaradimov/glisomorph/blob/master/fallout_common/docs/filespecs/frm.md): byte offsets, directional block sharing, pixel order/transparency, palette distinction, and comparison against Fallout 1/Fallout 2 readers and shipped archives. This is a secondary technical investigation, not a game-vendor specification.
- [fodev.net FRM reference](https://fodev.net/files/fo2/frm.html) and [FalloutMods FRM File Format](https://falloutmods.fandom.com/wiki/FRM_File_Format): corroborating format references. Fallout 2 documentation is used only where cross-checked against Fallout 1 Community Edition.

## Fallout 1 palette investigation

### Confirmed palette resource format and channels

Fallout's `.PAL` resources are binary color-table resources, not RIFF palettes. The first 768 bytes contain 256 RGB triples, in R/G/B byte order. The following 32,768 bytes are the engine's RGB555-to-palette-index conversion table, so a minimal palette is exactly `0x8300` bytes. The documented extended form is `0x38304` bytes: the minimal data, the literal `NEWC` marker, and three 65,536-byte runtime color-effect tables. FAE validates the exact supported file size, and for the extended form also validates the marker; it only needs the first 768 bytes for static sprite display.

Each component in the palette is VGA-style 6-bit (`0..63`). Fallout 1 Community Edition's display backend converts these components to 8-bit by shifting left two bits (`component << 2`), so 63 maps to 252; FAE uses that same conversion and does not bit-replicate, apply guessed gamma, or color-grade. The engine's gamma table is initialized with gamma 1.0 and is applied when updating the display palette. If any component in one entry exceeds `0x3F`, the Fallout 1 loader sets all three components for that entry to zero and marks it unmapped; FAE reproduces the black entry for display. Index 0 is forced transparent for FRM preview regardless of its RGB triple.

### Palette lookup and special cases

Fallout 1 Community Edition calls `loadColorTable("color.pal")` during game initialization, through the game's data-file layer, then applies the loaded `cmap`. The `art.cc` FRM structures contain indexed frame pixels but no palette identifier or path. Other palette loads are explicit game-context operations (for example movie subtitles, credits, and selected interface/end-game screens), not a general per-FRM lookup rule. Although the archives contain named interface/cinematic `.PAL` files, automatically selecting a same-basename palette for every FRM would not reproduce the engine's context-sensitive behavior. FAE therefore uses only the exact root `COLOR.PAL` default; asset-specific palettes remain unsupported until their corresponding game context can be identified reliably.

Palette cycling is also runtime state. The game cycles selected palette ranges (including indices in the 229–254 range described by the secondary format reference) by changing the displayed palette. The preview uses the static loaded color for each index and does not animate these effects.

### Local Fallout 1 files checked

The local install at `D:\SteamLibrary\steamapps\common\Fallout` has no loose `.PAL` files. `MASTER.DAT` contains a root `COLOR.PAL` with an uncompressed size and read payload of 33,536 bytes (`0x8300`), as well as explicitly named interface/cinematic palettes; sampled named palettes also report 33,536 bytes. `CRITTER.DAT` does not contain the standard root palette. FAE resolves `COLOR.PAL` from the currently opened archive first. If it is absent, a user-selected game-data directory is searched only for an exact root `color.pal` file or the root entry in its `MASTER.DAT`; unrelated `.PAL` files are never selected. The user-selected archive folder is not automatically treated as a game-data directory.

### Sources consulted

- [Fallout 1 Community Edition `color.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/plib/color/color.cc): `loadColorTable`, component range handling, the color table, and the default gamma table.
- [Fallout 1 Community Edition `palette.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/palette.cc): initial palette copied from `cmap` and runtime palette application.
- [Fallout 1 Community Edition `main.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/main.cc): default `color.pal` load during game initialization.
- [Fallout 1 Community Edition `svga.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/plib/gnw/svga.cc): 6-bit component-to-display conversion by `<< 2`.
- [Fallout 1 Community Edition `gmovie.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/gmovie.cc) and other call sites: explicit palette load in specific runtime contexts.
- [PAL/COL reference](https://fodev.net/files/fo2/pal.html): binary layout and optional extended tables. Its Fallout 2 engine details are treated as corroboration only; the Fallout 1 CE source was used to verify the channel and runtime rules implemented here.

### Palette implementation and verification

`src\FalloutAssetExplorer.Core\FalloutPalette.cs` parses the validated PAL layouts, leaves indexed FRM pixels untouched, and expands them to RGBA using Fallout 1's 6-bit `<< 2` conversion. The view model owns one `FalloutPaletteResolver` for its lifetime. The resolver prefers an exact root `COLOR.PAL` in the active DAT archive, then an explicitly selected game-data directory's loose root `color.pal` or root `MASTER.DAT`. When the active archive is named `CRITTER.DAT`, it also supports the conventional sibling `MASTER.DAT`, allowing a directly opened critter archive to use the installation's standard palette without requiring the user to open the master archive first. Successfully parsed palettes and supporting master-archive directory indexes are cached by canonical source path, file length, last-write timestamp, and entry metadata. Changing the configured directory or opening an archive from another installation resolves against that source instead of reusing stale palette state. Multiple or malformed default candidates produce a visible diagnostic; no arbitrary `.PAL` is used. A missing/invalid palette keeps the diagnostic grayscale preview.

Synthetic coverage is in `tests\FalloutAssetExplorer.Core.Tests\FalloutPaletteTests.cs`: known channel values, palette index lookup, alpha for index 0, indexed-frame rendering, supported extended marker, invalid lengths, archive/directory discovery, direct CRITTER-to-sibling-MASTER resolution, cache reuse while switching archives, explicit-folder precedence, folder-change cache isolation, and missing/invalid fallback diagnostics.

The local `MASTER.DAT` root palette was read from the archive, validated at 33,536 bytes, and used to render `ART/BACKGRND/BACK1.FRM`. The palette also resolved from the sibling `MASTER.DAT` when `CRITTER.DAT` was opened directly first, from the explicitly configured game-data folder, and after switching from master archive to critter archive with the same resolver cache. `ART/CRITTERS/HAPOWRAA.FRM` rendered from `CRITTER.DAT` using that shared palette. Additional FRMs from both archives were checked with this static palette and transparency behavior. These checks do not claim parity for the game's special interface/movie palette contexts or animated palette cycling.

The additional real samples rendered in the read-only check were:

| Archive | Additional FRM entries |
| --- | --- |
| `MASTER.DAT` | `ART/BACKGRND/BHOOD.FRM`, `ART/BACKGRND/CATH.FRM`, `ART/BACKGRND/FOLLOW.FRM`, `ART/BACKGRND/HUB.FRM`, `ART/BACKGRND/JUNKTOWN.FRM` |
| `CRITTER.DAT` | `ART/CRITTERS/HAPOWRAB.FRM`, `ART/CRITTERS/HAPOWRAE.FRM`, `ART/CRITTERS/HAPOWRAG.FRM`, `ART/CRITTERS/HAPOWRAH.FRM`, `ART/CRITTERS/HAPOWRAI.FRM` |

The `HAPOWRAA.FRM` sample contained 1,118 transparent pixels in its first frame; the opaque `BACK1.FRM` background frame contained none. Every required and additional sample contained non-grayscale RGB pixels after applying the local palette. No independent screenshot/reference viewer was available for direct visual comparison.

### Initial implementation plan

1. Add an independent Core decoder that accepts FRM bytes and returns version, timing/action metadata, per-direction shifts, frames, frame offsets, and indexed pixels.
2. Check every header/record/data offset, arithmetic operation, direction block, dimension, and total pixel count before allocating/copying. Apply conservative frame, dimension, and aggregate pixel limits.
3. Add synthetic FRM fixtures for shared and distinct directions, multiple frames, offsets, correct row-major pixels, and malformed/truncated boundaries.
4. Let the existing DAT1 reader stream only the selected FRM into the decoder. Add direction/frame selection, a nearest-neighbor diagnostic grayscale/index display with a clear missing-palette label, and optional preview-rate animation controls; do not enable export.
5. If original archives are present, verify selected real FRM entries without writing game content into the repository; report those checks separately from synthetic tests.

### Decoder and preview verification

The independent decoder is implemented in `src\FalloutAssetExplorer.Core\FrmDecoder.cs`, with immutable frame, direction, and document models alongside it. It validates supported version values, the fixed header and frame-record lengths, data-size agreement, sequential direction blocks or consecutive shared offsets, positive bounded dimensions, `width * height` pixel lengths, aggregate pixel limits, and exact end-of-file consumption before copying frame pixels.

The Avalonia browser previews a selected `.FRM` without extracting it to disk. Direction and frame selectors display decoded pixels using disabled bitmap interpolation. Play/Pause advances the selected direction using an adjustable 1–30 FPS preview rate (8 FPS by default); Loop is enabled by default, and manual frame selection pauses playback. Direction and FRM changes stop playback, and the window owns and disposes the single UI dispatcher timer. This is a preview rate, not a claim of Fallout runtime timing. The optional checkerboard is drawn behind the image and does not alter indexed pixels. With a valid default Fallout palette, pixels use original palette colors and index 0 is transparent; otherwise the UI reports why and displays diagnostic grayscale. The File menu allows selecting a local game-data folder when the active archive does not contain `COLOR.PAL`. Non-FRM selections clear the bitmap and show an explanatory message.

Synthetic FRM decoder regression coverage is in `tests\FalloutAssetExplorer.Core.Tests\FrmDecoderTests.cs`. It covers a minimal/shared-direction frame, multiple frames and a distinct direction block, metadata and pixel ordering, truncated headers/records/pixels, invalid offsets and dimensions, excessive frame counts, pixel-length mismatch, data-size mismatch, trailing bytes, unsupported versions, and invalid frame rates. `FrmPlaybackControllerTests.cs` separately verifies ordered advancement, looping and stop-at-end behavior, pause/resume, manual frame selection, direction/FRM changes, empty and single-frame inputs, speed limits, and stopped-state stability without wall-clock sleeps.

Real local archive smoke tests were run read-only without retaining extracted assets or adding the game data to the repository:

| Archive | Entry | Decoded metadata |
| --- | --- | --- |
| `MASTER.DAT` | `ART/BACKGRND/BACK1.FRM` | 6 directions, 1 frame per direction, first frame 130 × 169 |
| `CRITTER.DAT` | `ART/CRITTERS/HAPOWRAA.FRM` | 6 directions, 12 frames per direction, first frame 40 × 73 |
| `CRITTER.DAT` | `ART/CRITTERS/HAPOWRAB.FRM` | Additional multi-frame FRM found and decoded |

Read-only integration checks used the actual entries above to exercise frame advancement, pausing/resuming, speed changes, and palette rendering without retaining extracted assets. Native UI automation also opened the real archives and exercised the Avalonia controls: it confirmed `CRITTER.DAT` resolved `COLOR.PAL` from `MASTER.DAT`, play/pause and loop/non-loop behavior, 30 FPS playback, manual frame selection pausing at the chosen frame, direction changes resetting to frame 1 and stopping playback, switching to `MASTER.DAT` while playing before selecting single-frame `BACK1.FRM` (playback disabled), and enabling the checkerboard control on a real preview. UI automation verified interaction with the controls; this is not a human visual comparison against the original game. The decoder acceptance checks are not exhaustive validation of every FRM variant. The present implementation requires supported combined `.FRM` layout and does not handle scene-specific palettes, palette cycling, or split-file behavior.

### Native-frame PNG export

`FrmPngExporter` writes the selected document direction and frame directly as an 8-bit RGBA PNG. It reuses the indexed decoded pixels and validated `FalloutPalette`, writes native dimensions with PNG filter type 0, and preserves palette index 0 as alpha 0 and all other indices as alpha 255. The exporter does not use the preview bitmap, its scaling, the checkerboard, or diagnostic grayscale; the UI disables export unless both a valid frame and game palette are available. Fallout 2 export is available when its root default palette resolves, but its visual color match is not verified against an in-game reference. PNG compression uses the .NET `ZLibStream` and a small row buffer; no image package or game asset is added. The Avalonia save picker suggests a sanitized direction/frame filename and enables overwrite confirmation. Export covers one selected frame only, not a sprite sheet; support remains limited to FRMs accepted by the existing decoder.

`FrmPngExporterTests.cs` decodes synthetic PNG chunks and zlib scanlines to assert native dimensions, exact palette RGB, transparent and opaque alpha, fully transparent frames, selected direction/frame routing, CRCs, and isolation from preview-only presentation. It also rejects missing palettes, invalid dimensions/pixel lengths, out-of-range frame selections, and non-writable streams.

Local archive exports were written only to temporary files and deleted after inspection. The native Avalonia save UI exported `MASTER.DAT` `ART/BACKGRND/BACK1.FRM` at 130 × 169 (12,296 bytes) and `CRITTER.DAT` `ART/CRITTERS/HAPOWRAA.FRM` frames 1 and 2 at 40 × 73 (2,160 and 2,204 bytes). The CRITTER first frame contained 1,118 transparent and 1,802 opaque pixels, with 609 opaque pixels using non-grayscale RGB values; frame 2's decoded PNG pixels differed from frame 1. The save dialog was canceled in a separate UI check; no output file was created and the selected preview/export action remained available. These are automated UI and pixel-data checks, not human visual inspection against the original game.

The PNG feature's Release build succeeded with 0 warnings and 0 errors; the full test suite passed 80 tests, 0 failed, 0 skipped. The application startup/close smoke test also passed.

For the animation-player change, the Release solution build succeeded with 0 warnings and 0 errors, and the complete suite passed: 73 tests, 0 failed, 0 skipped. The desktop app startup smoke test confirmed that the preview window launches with no archive selected.

## Fallout 2 FRM preview milestone

### Reused behavior and game-specific palette lookup

The shared `FrmDecoder` was retained without relaxing its bounds checks. It accepts the same versions 3 and 4 already allowed by FAE, validates the complete header and frame records before allocating pixel arrays, enforces the 32 MiB input and 8 Mi-pixel limits, and requires exact frame-data boundaries. Earlier read-only checks decoded 19 real Fallout 2 FRMs from `master.dat` (6), `critter.dat` (6), `patch000.dat` (1), and `f2_res.dat` (6); this supports reusing the existing combined-FRM decoder for those samples, not every Fallout 2 asset variant.

The palette resolver now accepts the shared `IGameArchive` contract. It first checks only the active archive's root `COLOR.PAL`; then a configured loose root `color.pal` or `MASTER.DAT`; then, for `CRITTER.DAT`, a same-game adjacent `MASTER.DAT`. A candidate master archive is structurally opened and rejected as a fallback if its game differs from the active archive. It never selects a contextual `.PAL` by FRM basename. These lookup rules let an opened Fallout 2 `critter.dat` find the root palette in its sibling Fallout 2 `master.dat`, without allowing a Fallout 1 master to color a DAT2 resource.

Fallout 2 Community Edition's [`color.cc`](https://github.com/alexbatalov/fallout2-ce/blob/main/src/color.cc) loads `color.pal` as 256 RGB triples, accepts channels through `0x3F`, and reads the following 32 KiB color lookup table (with optional `NEWC` tables). Its [`art.cc`](https://github.com/alexbatalov/fallout2-ce/blob/main/src/art.cc) FRM draw path calls `blitBufferToBufferTrans`; [`draw.cc`](https://github.com/alexbatalov/fallout2-ce/blob/main/src/draw.cc) implements this by leaving destination pixels unchanged when the source palette index is zero. This corroborates the 6-bit palette channel range and index-zero sprite transparency policy used by FAE. FAE reuses its existing channel-to-RGBA `<< 2` conversion; however, no trusted in-game screenshot or pixel reference was available to validate the rendered Fallout 2 RGB output visually. The UI explicitly labels the Fallout 2 palette as not visually verified. Static preview also does not reproduce gamma/runtime palette changes, cycling, or special scene palettes.

### Verification scope and remaining limits

Deterministic tests cover a version-4 FRM record, loading `COLOR.PAL` from a synthetic DAT2 archive, and resolving a synthetic same-game adjacent DAT2 master for a critter archive. They require no game installation. A separate, temporary read-only smoke test in this milestone re-opened all four locally available Fallout 2 archives, resolved the default palette, decoded and rendered the first 6 size-eligible FRMs in `master.dat`, `critter.dat`, and `f2_res.dat` plus the single FRM in `patch000.dat` (19 total). It passed; the temporary game-path-specific test was removed and no assets were added to the repository. The existing malformed/truncated-data tests continue to exercise decoder bounds; no decoder validation limits were weakened. Real archive checks are reported separately from the synthetic suite. This milestone enables combined Fallout 2 FRM preview and PNG export when the default palette is available; it does not implement DAT2 resource overlays, split `.FR0`–`.FR5` routing, contextual palette selection, palette cycling, or validated in-game color matching.
