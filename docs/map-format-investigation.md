# Fallout MAP format investigation

## Scope and evidence

The investigation was read-only and did not alter archive, FRM, palette, or extraction behavior. At the time of investigation, the repository had no MAP decoder; the first header-only parser has since been added as documented at the end of this report. `.MAP` remains recognized as an archive-entry extension for browsing/filtering, and the current inspector reads only the selected entry.

The primary implementation references are Fallout 1 Community Edition's [`map.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/map.cc) and [`map.h`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/map.h), and Fallout 2 Community Edition's [`map.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/map.cc), [`map.h`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/map.h), and [`map_defs.h`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/map_defs.h). The community [MAP format reference](https://fodev.net/files/fo2/map.html) provides field and record explanations; details below that are not independently confirmed by both game engines are called out as such.

Local assets were discovered by inspecting the available Steam game directories, not by assuming a path. No loose `.MAP` files were found there. The existing DAT readers exposed 72 `.MAP` entries in Fallout 1 `MASTER.DAT` and 155 in Fallout 2 `master.dat`; the game folder identifies their origin. Entries were copied to memory read-only for header and section-boundary observations. No game files were changed and no asset bytes were retained.

## Findings confirmed by engine code and local samples

| Property | Fallout 1 | Fallout 2 |
| --- | --- | --- |
| MAP version accepted by the corresponding CE loader | `19` | `19` and `20` |
| Header layout | `MapHeader`; 0xEC bytes | `MapHeader`; 0xEC bytes |
| Integer byte order | Big-endian, via the engine's integer read helpers | Big-endian, via the engine's integer read helpers |
| Elevations | Three elevation slots | Three elevation slots |
| Floor/roof grid | 100 × 100 32-bit square-grid values per present elevation | Same dimensions and serialized value width |
| Grid serialization | Present elevations read in slot order; values read as a sequence of 32-bit integers | Same load pattern |
| Object coordinates | Object tile values refer to the hex grid, not the square tile-array index | Same broad distinction |

The common 0xEC-byte header has a 32-bit version, 16-byte name, entering tile/elevation/rotation, local-variable count, map-script index, flags, darkness, global-variable count, map index, last-visit time, and 44 reserved/unknown 32-bit values. The CE source defines these fields and reads/writes them in the same order. The last-visit field is represented as unsigned in Fallout 2 CE and signed in Fallout 1 CE's struct; its on-disk width and position are the same, but its semantic range should not be normalized without a specific need.

The header is followed sequentially, not by a directory of section offsets: global variables, local variables, tile grids for elevations not marked absent by flags `0x2`, `0x4`, and `0x8`, scripts, then objects. Each variable is a 32-bit integer. A present elevation contributes 10,000 32-bit square-grid values (40,000 bytes). Both CE loaders iterate elevation slots in ascending order, skip absent slots, and read one fixed-size 32-bit integer list for each present grid (`square_load` in Fallout 1 and `_square_load` in Fallout 2). Their APIs load each word into a signed 32-bit `int`; Fallout 1's `fileReadInt32` and Fallout 2's `db_freadInt32` read a big-endian word. Thus the serialized grid position is a linear sequence of 10,000 words; the loaders' runtime storage is indexed in 100 × 100 dimensions. The word has a packed roof/floor interpretation in engine code, but the inspector preserves each 32-bit bit pattern as `uint` raw data and does not currently annotate values with the engine square-grid coordinates established below. Tile id `1` is the engine's default/no-tile value. The engine load path clears roof flag bit `0x1000`; this is runtime normalization, not a reason for a read-only inspector to rewrite source bytes.

The following independently checked samples demonstrate the header versions and that the variable/tile section bounds calculated from the header and elevation flags remain inside each extracted entry. Tile-grid decoding was also checked against all present grids in these entries; it consumed precisely the header-derived byte ranges and returned 10,000 values per present grid.

| Game/archive entry | Bytes | Version | Flags | Global/local vars | Present elevations | Tile section |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Fallout 1 `MASTER.DAT`, `MAPS/BRODEAD.MAP` | 111,808 | 19 | `0x0C` | 0 / 0 | 1 | `236–40,236` |
| Fallout 1 `MASTER.DAT`, `MAPS/BROHD12.MAP` | 417,760 | 19 | `0x08` | 21 / 0 | 2 | `320–80,320` |
| Fallout 2 `master.dat`, `maps/arbridge.map` | 96,836 | 20 | `0x0C` | 1 / 0 | 1 | `240–40,240` |
| Fallout 2 `master.dat`, `maps/arcaves.map` | 526,972 | 20 | `0x00` | 21 / 0 | 3 | `320–120,320` |

The tile ranges are half-open byte offsets, with the end excluded. These are sample observations, not proof that every map, saved-map variant, or modded file follows the same constraints. One inspected Fallout 2 header name has a nonzero byte after its first NUL; a decoder should retain the original 16 bytes and avoid requiring zero-filled name padding.

## Tile-grid ordering and coordinate mapping

### Confirmed in the two Community Edition engines

The MAP loaders read each present 100 × 100 grid as a sequential list into a 10,000-element array. Fallout 1 uses `square_load` in [`src/game/map.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/map.cc); Fallout 2 uses `_square_load` in [`src/map.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/map.cc). Both iterate elevation slots from 0 through 2, skip slots whose absence flag is set, and read one complete grid into the corresponding elevation array. The shared dimensions are defined as width and height 100 in Fallout 1 [`map_defs.h`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/map_defs.h) and Fallout 2 [`map_defs.h`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/map_defs.h).

The engines' square-grid-to-screen functions make the linear index mapping explicit:

- Fallout 1 `square_coord` in [`src/game/tile.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/tile.cc) and Fallout 2 `squareTileToScreenXY` in [`src/tile.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc) both compute `x = width - 1 - index % width` and `y = index / width`.
- Thus for width 100, serialized index `i` maps to engine square-grid coordinate `x = 99 - (i % 100)`, `y = floor(i / 100)`. Index 0 is `(99, 0)`, index 99 is `(0, 0)`, and index 100 is `(99, 1)`. In array terms, the linear sequence advances through each y band while x decreases from 99 to 0. Calling this simply conventional row-major without mentioning the reversed x coordinate would be misleading.
- Both engines' grid renderers address the in-memory array as `y * width + x`. Their screen-coordinate functions then place a grid coordinate relative to camera/scroll offsets: `screenX = offsetX + 48 * (x - originX) + 32 * (y - originY)` and `screenY = offsetY - 12 * (x - originX) + 24 * (y - originY)`. The names of the offset/origin globals differ between games. This confirms the relative isometric screen directions for the square grid; it is not a fixed map-to-screen transform because the offsets and origins depend on engine view state.

The load/index/render pattern is the same in the referenced Fallout 1 and Fallout 2 CE sources. The square-grid load and coordinate functions contain no MAP-version branch, so the CE implementation uses this mapping for Fallout 2 versions 19 and 20 as well as Fallout 1 version 19. This is an engine-source finding, not independent proof for every retail, modded, or saved-map variant.

### What remains unresolved

The engine source distinguishes square-grid indices from hex-grid tile indices used by objects and movement. The inspected square-grid functions establish the square coordinate and its screen placement, but do not establish a direct conversion from `(x, y)` square-grid coordinates to a MAP object's hex tile number, nor which square-grid cell should be considered the spatial center of a particular hex. The four local samples confirm real section boundaries, elevation counts, and decoder consumption, but their bytes alone do not identify landmarks or orient the grid on the world map. No independently verified landmark-to-square-coordinate correspondence or editor export was available in this investigation.

**Confidence:** high for the serialized-index-to-engine-square-coordinate formula and its identical implementation in both pinned CE sources; high for relative screen-axis deltas in those engines; not established for square-to-hex/object coordinate conversion or retail/mod variants. No decoder correctness defect was found, and the existing raw-value decoder remains appropriate.

## Scripts, objects, and resource references

After tiles, the engine loads five script lists and then the object section. The community reference describes scripts as fixed 16-slot extents whose record size varies by script type (system/item/critter, timed, spatial). Thus a count is not simply a byte length; safely skipping a list requires validating counts and extent arithmetic.

The object section starts with a total count and then per-elevation counts. A base object includes identity, hex tile, offsets, frame/rotation, FID, flags, elevation, PID, combat/light state, and script references. Its remainder is not a fixed record size: inventory members are nested, and subtype payload depends on the object's PID and prototype data. Items, scenery (including doors, stairs, ladders and elevators), critters, and exit-grid objects can carry different data. A parser that tries to walk objects therefore needs bounded recursive inventory handling and game-correct PRO subtype knowledge; merely reading the 0x48-byte base object cannot establish the next record boundary.

Resource relationships supported by the engine/reference are:

- Square-grid tile art ids resolve through `art/tiles/tiles.lst`; rendering needs the corresponding tile art, not just the packed grid values.
- Object FIDs identify art resources; PIDs identify prototype records in PRO resources.
- Script indices refer into `scripts.lst` and then to `.INT` programs. MAP script records are runtime instances, with owners related to placed objects.
- Stairs, ladders, elevators and exit grids contain transition-related values; interpreting destinations requires map metadata such as `maps.txt` and target-map validation.

Header metadata, variable arrays, and raw tile ids can be inspected without external resources. Drawing recognizable scenery or interpreting subtype-specific object payloads cannot be done reliably from MAP alone.

## Fallout 1 / Fallout 2 differences and uncertainties

- Fallout 1 CE accepts version 19 only; Fallout 2 CE accepts 19 and 20. Local Fallout 1 samples observed here are version 19 and Fallout 2 samples version 20. The Fallout 2 acceptance of version 19 is an engine fact, but no local Fallout 2 version-19 sample was found in this targeted sample.
- The reference reports a version-dependent ladder payload: version 19 stores a destination built-tile value, while version 20 stores destination map followed by built-tile. Treat this as a meaningful object-layout incompatibility requiring cross-check against both engines before object parsing.
- Both CE loaders use the same header/section order and 100 × 100 square grid model. That does not prove all object subtypes, clean maps, saved maps, mods, or edge-case flags are equivalent.
- Clean `.MAP` files and saved `.SAV` map states share a broad structure but can differ in runtime state, script locals, object flags and critter data. A future loader must explicitly scope which it accepts.
- The exact meaning of the 44 reserved header integers, some tile flag semantics, all object subtype payloads, and behavior of non-CE retail/modded variants remain unverified here. There is no MAP signature: version, archive/game context, and structural validation are needed to recognize candidates.

## Recommended architecture and incremental plan

Keep the format reader in `FalloutAssetExplorer.Core`, next to the archive and FRM readers. A small `MapDecoder` should accept a bounded `ReadOnlyMemory<byte>` plus `FalloutGame` context and produce immutable metadata; it should not depend on Avalonia, load external resources, or mutate archive entries. Use the existing `IGameArchive.CopyEntryToAsync` path to supply selected map bytes. The browser ViewModel can dispatch a selected `.MAP` to a metadata presentation separately from FRM rendering; do not entangle map parsing with the FRM palette or playback pipeline.

1. **First milestone — header/metadata inspector (recommended).** Parse the fixed header, distinguish supported versions using archive game context (Fallout 1: 19; Fallout 2: 19/20), validate the fixed size, and expose raw name bytes plus display name, entering-location fields, flags/elevation availability, variable counts, darkness and map index. Bound the entry before reading; reject unsupported versions, truncated headers and counts that cannot fit safely within the entry. Preserve unknown flags and reserved words. Do not impose extra entering-location or padding constraints until checked against real maps. This is useful immediately and avoids guessing at the variable-length tail.
2. **Structural grid reader (implemented).** Revalidate the supplied header against the actual entry, check each fixed-size grid range before allocation, and expose raw 32-bit values for present elevation slots. The inspector shows the number of grids, dimensions and value count. The linear index-to-square-coordinate mapping is now confirmed in the pinned CE sources; the decoder intentionally keeps raw serialized order.
3. **Diagnostic square-grid view (implemented).** The selected MAP's present elevation slots can be selected in the inspector. A lightweight Avalonia drawing control displays a fixed 100 × 100 neutral grid using `x = 99 - (i % 100)`, `y = i / 100`; hovering reports a transient cell inspection, while a left click retains a separately highlighted selected cell and its raw value. Changing elevation retains the coordinate selection only for the same fixed grid index and refreshes its displayed value from the newly selected grid. Cell appearance does not encode the raw value or imply terrain.
4. **Resource-linked tile rendering (defer).** Before rendering actual art, verify square-to-hex/spatial relationships and tile id/flag interpretation against engine/editor data, then resolve `art/tiles/tiles.lst`, tile FRMs, palette behavior, and compositing. Do not fold scripts or objects into this step.
5. **Script/object inspection.** Separately research and implement bounded scripts/objects, PRO-dependent payload sizes, recursive inventories, version-specific scenery/ladder data, exit destinations, and required LST/INT/PROTO resources. This is substantially larger; do not fold it into the grid diagnostic milestone.

## Testing plan for implementation

- Add synthetic header fixtures for Fallout 1 version 19 and Fallout 2 versions 19 and 20, plus tests that reject a version unsupported by that game context.
- For the first milestone test short headers at each boundary, wrong endian/version, documented out-of-range elevation/rotation, negative and excessive variable counts, and unknown flag bits (preserve or explicitly report them rather than silently reinterpret). Keep entering-tile values raw until edge cases are better established.
- For the grid milestone test zero/one/three present elevations, every elevation-absence flag, exact section boundaries, truncated variables/tiles, count and multiplication overflow, and unknown tile flag preservation.
- Keep object/script parsing out of initial scope. When added, test count caps, 16-slot script extent rounding, variable record widths, malformed extent trailers, object/PID subtype mismatches, truncated recursive inventory, and version-specific ladder records.
- Validate both versions with local real files read-only through the actual DAT archive readers. Do not bundle copyrighted MAP assets: use synthetic fixtures in the ordinary test suite and an opt-in/local integration check or documented manual verification for installed game data. Re-run existing DAT1/DAT2, extraction, FRM, palette and PNG tests unchanged as regressions.

## Original recommended follow-up task (implemented)

The following task was completed: implement only the MAP header/metadata decoder and read-only metadata presentation for a selected `.MAP` entry; support Fallout 1 version 19 and Fallout 2 versions 19/20; retain reserved header fields without assigning speculative meanings; impose entry/header and count bounds; add synthetic tests and validate the four local samples. Scripts, objects, map drawing, and DAT/extraction/FRM/palette behavior remain outside the implementation.

## Header inspector implementation status

The inspector implements `MapHeaderDecoder` and `MapTileGridDecoder` in Core and reads a selected `.MAP` through the existing resource panel. Header decoding accepts version 19 for Fallout 1 and versions 19/20 for Fallout 2. It decodes documented header fields, preserves the exact 16 name bytes and 176 reserved bytes, and derives global/local variable and tile-section byte ranges. The last-visit field is exposed as raw unsigned bits because its signedness differs between the Fallout 1 and Fallout 2 engine structures. Grid decoding revalidates the header against the entry, bounds-checks each fixed-size grid, decodes present grids in elevation-slot order, and exposes exactly 10,000 raw 32-bit values per grid. Values retain their on-disk bit patterns. The pinned CE sources confirm index `i` maps to square coordinate `(99 - (i % 100), floor(i / 100))`; the inspector now visualizes selected elevations as a neutral matrix, without assigning cell colors to values or implying terrain, hex/object positions, or complete world orientation. Unknown flag bits and location values remain unvalidated/raw. Scripts and objects are not parsed.

Synthetic tests cover supported and unsupported game/version pairs, short headers, variable-count and tile-section truncation, oversized counts, known metadata offsets and values, raw reserved/name preservation, elevation-presence calculation, grid byte order and serialization, all-absent and present elevations, truncated data, inconsistent metadata, and raw word preservation. The four real samples in the table above were validated read-only through the archive readers; the tile decoder consumed the bounds for all present grids and returned their expected fixed counts. No game files or sample assets are part of the repository.
