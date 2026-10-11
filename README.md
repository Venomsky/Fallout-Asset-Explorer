# Fallout Asset Explorer

Fallout Asset Explorer (FAE) is a standalone Windows desktop application for browsing Fallout 1 DAT1 and Fallout 2 DAT2 archives. The archive readers remain read-only and can stream individual entries to a caller-provided destination. Safe user-facing extraction is implemented for selected, visible, or all entries with path validation, overwrite choices, and cancellation support.

## Requirements

- Windows 10 or 11
- .NET SDK 9.0 or later

The development workspace has .NET SDK 9.0 installed but does not have the requested .NET 10 SDK. To keep the application buildable and runnable in this environment, this initial shell targets `net9.0`. Once .NET 10 is installed, the target framework can be advanced to `net10.0`.

## Build, run, and test

Run these commands from this directory:

```powershell
dotnet restore FalloutAssetExplorer.sln
dotnet build FalloutAssetExplorer.sln
dotnet run --project src\FalloutAssetExplorer.App\FalloutAssetExplorer.App.csproj
dotnet test FalloutAssetExplorer.sln
```

The application starts with no archive selected. **Open Archive** opens a local file picker and detects DAT1 or DAT2 from the validated archive structure, not its extension. Search, extension filters, sorting, and multi-selection work across either archive type. DAT1 entries use the bounded Fallout LZSS reader; DAT2 entries use bounded raw or zlib reads. Selecting a Fallout 1 or Fallout 2 combined `.FRM` entry decodes and previews its directions and frames; selecting a `.MAP` entry inspects supported header metadata and offers a neutral 100 x 100 diagnostic grid for each present elevation slot. Hovering a cell shows its engine square-grid coordinates and raw 32-bit value; clicking selects it persistently, with hover and selection indicated separately. The grid does not encode terrain, use game artwork, or claim hex/object positions or full world orientation. Use **Play/Pause**, **Loop**, and the 1–30 FPS slider (8 FPS by default) to preview a direction; this adjustable rate is only a viewer setting and does not claim to reproduce Fallout's runtime timing. Manual frame selection pauses playback. The optional checkerboard reveals transparent pixels without changing the sprite. **Export PNG...** saves the selected direction/frame at native pixel dimensions using the resolved root `COLOR.PAL`, with palette index 0 preserved as transparent; export is disabled when only diagnostic grayscale is available. Fallout 2's root `COLOR.PAL` and zero-index sprite transparency are supported, but visual color matching has not been verified against an in-game reference. If the active `CRITTER.DAT` has no default palette, the resolver checks its sibling `MASTER.DAT` of the same game; alternatively, use **File > Set Game Data Folder...** to locate a loose root `color.pal` or a matching game's `MASTER.DAT`. A missing or invalid palette is reported and uses clearly labeled diagnostic grayscale instead.

## Project structure

- `src\FalloutAssetExplorer.App` — Avalonia desktop shell, window, and browser view model.
- `src\FalloutAssetExplorer.Core` — shared archive contract and format detection; DAT1 and DAT2 parsing; bounded raw/LZSS/zlib entry reading; MAP header and raw tile-grid decoding; FRM decoding/PNG export; Fallout PAL parsing/discovery/rendering; and reusable entry filtering.
- `tests\FalloutAssetExplorer.Core.Tests` — xUnit tests with synthetic DAT1, DAT2, MAP header/tile-grid, FRM, PAL, and PNG data for valid behavior and malformed bounds.
- `docs\dat-format-investigation.md` — source-backed DAT1, DAT2, FRM, and PAL findings, parser behavior, and verification notes.

The UI uses Avalonia and its Fluent theme/DataGrid packages. The core and app do not currently use an MVVM toolkit; the small view model uses `INotifyPropertyChanged` directly.

## Current scope and limitations

- The app can start without game files and browse supported Fallout 1 DAT1 and Fallout 2 DAT2 archives selected locally.
- DAT2 support is read-only. The reader validates the footer, flat directory, entry paths, compression flags, offsets and sizes; zlib output is streamed with a 512 MiB per-entry cap. The two archive formats are selected by structural validation, not `.dat` filenames.
- Safe extraction is available through the archive browser: choose a destination folder, select entries or all visible/all entries, and use the overwrite policy chooser to skip existing files, overwrite them, or cancel the operation. Each extracted file preserves its relative path under the chosen output folder; archive-supplied paths are revalidated to block rooted, drive-qualified, UNC, traversal, malformed, and reserved-device paths. Cancellation returns a summary, removes incomplete temporary output where possible, and retains per-entry failure diagnostics for programmatic callers.
- Extraction rejects symbolic links and filesystem junctions found below the selected destination, including the temporary staging folder. This is a best-effort check using .NET filesystem attributes; it is not race-resistant if another process changes directory entries concurrently. The selected destination root itself may be a user-chosen link.
- Combined Fallout 1 and Fallout 2 `.FRM` preview reuses the bounded FRM decoder. Split `.FR0`–`.FR5` critter animations remain unsupported.
- MAP inspection parses the 0xEC-byte header, validates the game-specific version and variable/tile section boundaries, and decodes present 100 x 100 grids as raw big-endian 32-bit values. The diagnostic view maps serialized index `i` to square-grid coordinate `(99 - (i % 100), floor(i / 100))`, uses neutral cells, and exposes raw values on hover; it does not render terrain, game art, or hex/object positions. Scripts and objects are not parsed.
- Palette resolution selects the root `COLOR.PAL` from the active archive, a configured loose file or same-game `MASTER.DAT`, or a same-game adjacent `MASTER.DAT` for `CRITTER.DAT`; it never selects an arbitrary `.PAL` resource. Fallout 2's static preview colors are not visually verified against the game.
- The palette parser validates minimal and extended Fallout PAL resource layouts and renders 6-bit RGB components as 8-bit channels using `channel << 2`.
- `MASTER.DAT` `ART/BACKGRND/BACK1.FRM` and `CRITTER.DAT` `ART/CRITTERS/HAPOWRAA.FRM` have been checked with the local master palette; see the format investigation for the real palette source and limitations.
- DAT1 enforces a 4 GiB archive/address limit, a 65,536-folder limit, a 250,000-entry limit, and 512 MiB per-entry packed/unpacked limits. DAT2 caps its directory at 128 MiB, entries at 250,000, paths at 4,096 bytes, and stored/uncompressed entry sizes at 512 MiB.
- Synthetic tests exercise DAT1 and DAT2 directory parsing, raw/LZSS/zlib data, empty entries, path normalization, truncation, invalid counts and offsets, size limits, unsupported types, and corrupt compression.
- PNG export uses Avalonia's save picker with its overwrite prompt enabled.
- Special cinematic/interface palettes, palette cycling, game-accurate animation timing, and split `.FR0`–`.FR5` critter animation files are not implemented. The game selects some special palettes by runtime context; FAE does not guess that context from an FRM filename.
- Local Fallout 1 archives were previously opened read-only: `CRITTER.DAT` enumerated 5,459 entries and `MASTER.DAT`'s `ART/SCENERY/TEMP.REF` raw payload was read successfully. Local Fallout 2 `master.dat`, `critter.dat`, `patch000.dat`, and `f2_res.dat` were also opened and sampled by the DAT2 reader. These checks do not establish compatibility with every game release or every archive variant.

No game assets are bundled. The app does not make runtime network requests or execute archive contents.

## FRM milestone verification

See `docs\dat-format-investigation.md` for the latest archive-format findings, validation limits, test results, real-archive samples, and known limitations.
