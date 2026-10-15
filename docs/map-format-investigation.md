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

The header is followed sequentially, not by a directory of section offsets: global variables, local variables, tile grids for elevations not marked absent by flags `0x2`, `0x4`, and `0x8`, scripts, then objects. Each variable is a 32-bit integer. A present elevation contributes 10,000 32-bit square-grid values (40,000 bytes). Both CE loaders iterate elevation slots in ascending order, skip absent slots, and read one fixed-size 32-bit integer list for each present grid (`square_load` in Fallout 1 and `_square_load` in Fallout 2). Their APIs load each word into a signed 32-bit `int`; Fallout 1's `fileReadInt32` and Fallout 2's `db_freadInt32` read a big-endian word. Thus the serialized grid position is a linear sequence of 10,000 words; the loaders' runtime storage is indexed in 100 × 100 dimensions. The inspector preserves each 32-bit bit pattern as `uint` raw data.

### Packed floor/roof cell values (Fallout 2 CE)

In the pinned CE revision, `tileRenderFloorsInRect` reads the low 16 bits of each cell as the floor word, while `tileRenderRoofsInRect` shifts the cell right by 16 and reads the high 16 bits as the roof word. Both render paths use the low 12 bits (`0x0FFF`) as the tile art ID and test bit 0 of the high flag nibble (`0xF000`) before drawing: when that bit is set, the corresponding draw is skipped. In the original 32-bit cell these are floor ID mask `0x00000FFF`, floor flag nibble `0x0000F000`, roof ID mask `0x0FFF0000`, and roof flag nibble `0xF0000000`; the draw-gate bits are raw bits 12 and 28 respectively. The remaining bits in each flag nibble are preserved as raw flags here and are not assigned meanings by the diagnostic.

The roof renderer explicitly skips roof tile ID `1`. The map-save empty-grid check also treats ID `1` in either half as no tile; however, the floor render loop passes its extracted ID to `tileRenderFloor` without an explicit ID-1 check. The diagnostic therefore identifies ID `1` as the CE empty-grid value but does not claim an equivalent floor-render skip branch. Zero decodes to zero IDs and zero flag nibbles; no empty-cell meaning is inferred for it. CE's MAP load path clears bit `0x1000` in the extracted roof word at runtime (raw cell bit 28), so the read-only diagnostic reports the on-disk value without applying that normalization. These observations are from Fallout 2 CE [`tileRenderRoofsInRect`, `tileRenderFloorsInRect`, and `tile_fill_roof` in `src/tile.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc) and the grid load/save paths in [`src/map.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/map.cc); they describe this pinned CE revision, not all retail or modded maps.

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

## Square, hex, and screen coordinate relationships

This section distinguishes the MAP's two serialized grids from the engine's viewport-dependent screen coordinates. Source references are pinned to the commits listed in [Scope and evidence](#scope-and-evidence); line links identify the implementation being discussed.

### Serialized grid coordinates

Fallout 1 [`map_defs.h`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/map_defs.h#L8) and Fallout 2 [`map_defs.h`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/map_defs.h#L8) define a 100 × 100 square grid and a 200 × 200 hex grid (the width/height/size constants are adjacent). Both are bounded index spaces with three elevation slots.

The square-grid serialization formula remains:

```text
squareX = 99 - (squareIndex % 100)
squareY = floor(squareIndex / 100)
squareIndex = 100 * squareY + 99 - squareX
```

Fallout 1 implements it in `square_coord` in [`src/game/tile.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/tile.cc#L1011); Fallout 2 does so in `squareTileToScreenXY` in [`src/tile.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc#L1097). Both inverse screen-picking paths form the serialized index as `x + width * y` after reversing the square x coordinate: Fallout 1 `square_num` / `square_xy` ([`tile.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/tile.cc#L1075), [`tile.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/tile.cc#L1105)); Fallout 2 `squareTileFromScreenXY` / `squareTileScreenToCoord` ([`tile.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc#L1161), [`tile.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc#L1176)).

The engine uses the same reversed-x convention for hex indices:

```text
hexX = 199 - (hexIndex % 200)
hexY = floor(hexIndex / 200)
hexIndex = 200 * hexY + 199 - hexX
```

This follows from Fallout 1 `tile_coord` and `tile_num` in [`src/game/tile.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/tile.cc#L636) and [`src/game/tile.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/tile.cc#L674), and Fallout 2 `tileToScreenXY` and `tileFromScreenXY` in [`src/tile.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc#L675) and [`src/tile.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc#L718). For example, hex index `201` is `(198, 1)`, and the inverse gives `200 * 1 + 199 - 198 = 201`. Index `0` is `(199, 0)`; index `39,999` is `(0, 199)`.

These are engine grid coordinates, not screen coordinates. In Fallout 1, direction/neighbor operations select direction offsets using `(tile % grid_width) & 1` (`tile_dist` in [`src/game/tile.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/tile.cc#L752)); Fallout 2's `tileDistanceBetween` uses `(tile % gHexGridWidth) & 1` in [`src/tile.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc#L797). Since the stored column is `199 - hexX` and 199 is odd, this parity is the opposite of `hexX & 1`. Screen projection itself branches on `hexX & 1`. These parity-dependent steps are part of the hex layout and prevent treating the hex grid as an ordinary square lattice.

### Engine screen projection and inverse picking

Both CE engines convert a valid hex index to `(hexX, hexY)` as above, then project relative to mutable viewport state: the centered hex coordinate, screen offsets, and the selected center tile. In both `tile_coord` (Fallout 1) and `tileToScreenXY` (Fallout 2), increasing `hexY` by one adds `(16, 12)` screen pixels. The horizontal contribution uses integer division by two and an additional branch for odd `hexX`: an odd column left of or at the center receives `(-16, +12)`, while one right of center receives `(+32, 0)`. An even-column step of two contributes `(48, -12)`. The exact expressions are in the linked functions above; integer division truncates toward zero. Therefore, a single fixed affine formula without parity and view-origin terms is not an accurate screen mapping.

`tile_set_center` in Fallout 1 [`src/game/tile.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/tile.cc#L496) and `tileSetCenter` in Fallout 2 [`src/tile.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc#L537) set those viewport origins. They normalize the centered hex x coordinate down to even when needed and derive square-grid origins from that center. The square-grid projection in `square_coord` / `squareTileToScreenXY` adds `(48, -12)` per square x step and `(32, 24)` per square y step, relative to its separately maintained screen origin. If the selected center hex row is odd, the center setter also adjusts the square screen offsets. This establishes how the grids are aligned for a particular engine view; it does not make their serialized indices interchangeable.

For a worked projection example, choose normalized center `(hexX, hexY) = (100, 100)` and express positions relative to the engine's screen offsets. The CE parity branch places `(101, 100)` at `(+32, 0)`; `(102, 100)` is `(+48, -12)`; and `(100, 101)` is `(+16, +12)`. These displacements follow directly from `tile_coord` / `tileToScreenXY`; absolute pixels still depend on the actual window offsets and chosen center.

The reverse hex operation (`tile_num` / `tileFromScreenXY`) is a screen-pixel hit test, not a simple algebraic inverse: after coarse division into 64-by-12 pixel regions, it uses the engine's `tile_mask` lookup and parity-dependent corrections, then rejects coordinates outside `[0, 199]` on either axis. Square picking follows a separate inverse (`square_xy` / `squareTileScreenToCoord`, linked above) and bounds check. Roof picking compensates for the roof's vertical screen displacement: Fallout 1 `square_coord_roof` / `square_xy_roof` ([`tile.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/tile.cc#L1042), [`tile.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/tile.cc#L1127)) and Fallout 2 `squareTileToRoofScreenXY` / `squareTileScreenToCoordRoof` ([`tile.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc#L1128), [`tile.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc#L1198)) place it 96 pixels above the floor-grid projection and add those 96 pixels back during inverse picking.

The projection functions accept an elevation argument, but the coordinate math in these functions does not branch on it. Elevation selects a distinct square/object layer elsewhere; these findings do not claim that every drawing path has no elevation-specific visual offsets. The explicit roof displacement is `-96` screen pixels in both inspected engines.

### What the engines establish about square-to-hex correspondence

There is no direct formula in the inspected MAP tile-index functions that converts a square index to a unique hex index, or vice versa. In particular, dividing hex coordinates by two is used to derive the **viewport center** for the square grid; it is not an established per-cell conversion rule.

There is, however, an explicit engine operation that relates an object's hex to square floor data. Fallout 1 `check_gravity` in [`src/game/anim.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/anim.cc#L3314) obtains the object's screen anchor with `tile_coord`, then looks up `square_num(x + 2, y + 8, elevation)` in `square[elevation]->field_0`. Fallout 2 `_check_gravity` does the corresponding operation with `tileToScreenXY` and `squareTileFromScreenXY` in [`src/animation.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/animation.cc#L3275). This confirms a deterministic **operation-specific sample point**: under the current viewport and elevation, the engine samples the square floor grid at an offset from the object's projected hex anchor. It does not establish that this is the only square cell geometrically associated with a hex, nor a general bijection between the grids.

For floor/roof rendering, the engine obtains a square-grid index through its square projection/picking routines and reads the corresponding packed value from that elevation's square array; Fallout 2's `tile.cc` render path indexes `gTileSquares[elevation]->field_0[squareTile]` ([`src/tile.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc#L1474)). The tile value is therefore addressed by a square index, while object placement and gameplay use hex tile indices. Object screen anchors, viewport state, parity, and the chosen screen sample all matter when an engine operation relates the two.

**Conclusion and confidence:** high confidence in the index formulas, grid dimensions, parity rules, viewport-relative projection, inverse hit-testing behavior, and the operation-specific gravity lookup because they are explicit in both pinned CE implementations. High confidence that the two index spaces must not be indexed interchangeably. A universal square-cell-to-hex-cell mapping, a one-to-one correspondence, and a complete screen/world orientation independent of viewport state are **not established**. The gravity sample point is engine behavior, not evidence that every object or visual overlay should use that point.

### Fallout versions and real-sample cross-check

The tile loading and coordinate routines cited above contain no MAP-version-specific coordinate branch. The same formulas therefore apply to Fallout 1 CE's supported v19 maps and Fallout 2 CE's supported v19/v20 maps at the engine-code level. Fallout 1 CE accepts v19; Fallout 2 CE accepts v19 and v20. This does not establish compatibility for retail variants or arbitrary modded files beyond the corresponding CE loaders.

The four installed archive entries previously documented above were re-read in this task through the application's existing archive readers and MAP header/grid decoders, without modifying the game archives. Their header-derived tile ranges remained within the extracted entry, and every present grid decoded to exactly 10,000 raw words:

| Read-only sample | Version | Entry bytes | Tile section `[start, end)` | Present grids | Header entering hex index → `(x, y)` |
| --- | ---: | ---: | ---: | --- | --- |
| Fallout 1 `MASTER.DAT`, `MAPS/BRODEAD.MAP` | 19 | 111,808 | `[236, 40,236)` | elevation 0 | `21,302` → `(97, 106)` |
| Fallout 1 `MASTER.DAT`, `MAPS/BROHD12.MAP` | 19 | 417,760 | `[320, 80,320)` | elevations 0, 1 | `20,100` → `(99, 100)` |
| Fallout 2 `master.dat`, `maps/arbridge.map` | 20 | 96,836 | `[240, 40,240)` | elevation 0 | `16,686` → `(113, 83)` |
| Fallout 2 `master.dat`, `maps/arcaves.map` | 20 | 526,972 | `[320, 120,320)` | elevations 0, 1, 2 | `28,906` → `(93, 144)` |

The entering indices are within the 40,000-cell hex range and the listed coordinates are derived with `hexX = 199 - (index % 200)`, `hexY = floor(index / 200)`. This sample check corroborates the square-grid section extents, elevation-slot ordering, fixed grid count, and the header's entering-hex range for real v19/v20 files. It does **not** independently validate square-to-hex object placement: this inspector does not decode the MAP object section, and no independently labeled object/landmark coordinate set was used. Thus the cross-grid relationship above rests on engine source, not on a visual pattern inferred from these sample tile values.

### Remaining questions and next milestone

- The projection routines establish screen coordinates relative to a moving viewport, but this investigation does not define a stable world-to-screen origin or fully audit every per-elevation drawing offset.
- The source-grounded gravity lookup identifies the square sample used by that operation; it does not enumerate every square cell touched by a hex or define a renderer's tile footprint.
- No sample object records or independently documented landmarks were correlated with square-grid cells. This remains an empirical cross-check opportunity, not a reason to invent a direct arithmetic transform.
- CE sources are strong evidence for the corresponding engines. They do not prove every detail of the original retail executables, saved-map variants, or third-party map editors.

**Milestone status:** bounded object-placement decoding and a minimal diagnostic hex-grid overlay have since been added. The overlay keeps square-grid decoding separate and uses the parity-aware projection documented above; it does not implement the engine's inverse picking masks or a full renderer.

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

The inspector implements `MapHeaderDecoder` and `MapTileGridDecoder` in Core and reads a selected `.MAP` through the existing resource panel. Header decoding accepts version 19 for Fallout 1 and versions 19/20 for Fallout 2. It decodes documented header fields, preserves the exact 16 name bytes and 176 reserved bytes, and derives global/local variable and tile-section byte ranges. The last-visit field is exposed as raw unsigned bits because its signedness differs between the Fallout 1 and Fallout 2 engine structures. Grid decoding revalidates the header against the entry, bounds-checks each fixed-size grid, decodes present grids in elevation-slot order, and exposes exactly 10,000 raw 32-bit values per grid. Values retain their on-disk bit patterns. The pinned CE sources confirm index `i` maps to square coordinate `(99 - (i % 100), floor(i / 100))`; the inspector visualizes selected elevations as a neutral matrix, without assigning cell colors to values or implying terrain, hex/object positions, or complete world orientation. Unknown flag bits and location values remain unvalidated/raw. Script records are structurally skipped to locate objects; object decoding is limited to the confirmed subset described below.

Synthetic tests cover supported and unsupported game/version pairs, short headers, variable-count and tile-section truncation, oversized counts, known metadata offsets and values, raw reserved/name preservation, elevation-presence calculation, grid byte order and serialization, all-absent and present elevations, truncated data, inconsistent metadata, and raw word preservation. The four real samples in the table above were validated read-only through the archive readers; the tile decoder consumed the bounds for all present grids and returned their expected fixed counts. No game files or sample assets are part of the repository.

## MAP object-placement inspector implementation

### Engine-confirmed section and record structure

The object section begins after the five script lists. In both engines the object reader consumes a total object count, then repeats this operation for each elevation in ascending order: read that elevation's object count, then read exactly that many top-level object records. The per-elevation counts are therefore interleaved with their records, not stored together in a separate three-count array. Fallout 2 `objectLoadAllInternal` then reads each top-level object's inventory; when `fixMapInventory` is disabled, `_obj_load_obj` reads inventory children recursively and assigns their owner. This order is explicit in Fallout 1 `obj_load_func` in [`src/game/object.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/object.cc) and Fallout 2 `objectLoadAllInternal` in [`src/object.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/object.cc). The map loaders call the script reader before the object reader: Fallout 1 `map_load_file` calls `scr_load` and then `obj_load` in [`src/game/map.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/map.cc); Fallout 2 `mapLoad` calls `scriptLoadAll` and then `objectLoadAll` in [`src/map.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/map.cc).

Both object base readers (`obj_read_obj` and `objectRead`) consume 18 big-endian 32-bit fields in the same order: object id, hex tile index, x/y offsets, screen x/y values, frame, rotation, FID, flags, stored elevation, PID, CID, light distance/intensity, an unretained field at offset 0x74, script id, and one further script field. The on-disk hex index is thus base field 1 (byte offset 4 within the base record); PID is base field 11 (byte offset 44). `PID_TYPE(pid)` is the high PID byte in both engines. Hex indices in `[0, 39,999]` decode with the engine-confirmed mapping already documented above: `x = 199 - (index % 200)`, `y = floor(index / 200)`. The decoder preserves the original signed 32-bit values and derives the PID type from the unsigned high byte. An index of `-1` is retained and presented as "no tile assigned"; any other out-of-range index is retained as an invalid tile index with a warning.

The per-object data reader first consumes three inventory metadata words (length, capacity, and the serialized pointer placeholder). A critter then has 11 fixed payload words in both CE implementations. Other types have a data-flags word, but item and scenery subtype payloads depend on the corresponding PRO prototype. Fallout 2 `objectDataRead` in [`src/proto.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc) demonstrates the item and scenery subtype branches, including version-dependent ladder data. Fallout 1 performs the equivalent work in `proto_read_protoUpdateData` in [`src/game/proto.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/proto.cc). Exit-grid misc PIDs (`0x05000010` through `0x05000017`) add four words in both implementations. A positive inventory length introduces nested object records; the current decoder follows them recursively and retains each child's own tile field together with its parent, depth, and quantity metadata.

Scripts are skipped using the engine's five script-list counts, 16-record extents, and two-word extent trailer. Each slot has 16 base words plus two words for a spatial script or one for a timed script. The script SID type is its high byte. The supporting readers are Fallout 1 `scr_load`/`scr_read_ScriptNode` in [`src/game/scripts.cc`](https://github.com/alexbatalov/fallout1-ce/blob/0609bcfd0ec40ff0571d0f57fab2821eb461dc8b/src/game/scripts.cc) and Fallout 2 `scriptLoadAll`/`scriptListExtentRead` in [`src/scripts.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/scripts.cc). The implementation validates extents and every consumed byte range before advancing.

### Implemented subset and limits

`MapObjectPlacementDecoder` revalidates the supplied header metadata, checks a bounded top-level object count and per-elevation counts, validates byte ranges, and exposes raw base fields, raw PID, raw hex index, derived coordinates when valid, inventory metadata, and raw payload words. It fully advances through critter records, exit-grid misc records, records whose only confirmed trailing data is the flags word, and Fallout 2 Door, Stairs, Elevator, Ladder Up/Down, GENERIC, and supported Item records after validating the corresponding PRO prefix. Prototype resolution is limited to the active Fallout 2 DAT2 archive; it bounds list and PRO entry sizes, validates one-based list indices and safe filenames, and checks the embedded PID and subtype field. Supported payload layouts are validated against the pinned CE evidence below. Other scenery subtypes, Fallout 1 PRO-dependent records, and unknown PID types remain unsupported and stop parsing. The stopping record's validated raw fields are retained in the result, along with all preceding records. This is intentionally a partial view, not a general MAP object loader; a partial list may omit later records and is labeled with parsed/declared counts and an unsupported reason.

For non-tile art FID lists, CE treats space, comma, semicolon, carriage return, tab, and line feed as filename separators while retaining one list slot per input line ([`artReadList` in pinned Fallout 2 CE `src/art.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/art.cc#L392-L422)). The resolver applies those separators before validating the selected filename; empty rows remain addressable, unsafe paths are still rejected, and FID indices remain direct zero-based list indices. Tile lists retain their dedicated CE-compatible parser.

The UI places a read-only object list and selected-record detail view in the MAP inspector. Object parse failure does not discard already decoded header or grid data. Ordinary users see a concise malformed-data status; the full exception diagnostic is emitted to the application debug output. Raw fields are retained in the selected object details rather than assigning unverified meaning to unknown values.

Synthetic tests cover the engine's index boundaries and invalid-index rejection, confirmed critter records for Fallout 1 v19 and Fallout 2 v19/v20, multiple elevation groups, script extents before objects, raw field preservation, supported scenery and standalone Item PRO resolution/payload handling, both ladder types/versions, malformed/truncated data, unsupported PRO-dependent layouts, nested inventory, invalid indices, inconsistent counts, and stale header metadata. In the last read-only validation against the locally installed Fallout 2 `master.dat` before Door support, `maps/arbridge.map` parsed 54 of 614 top-level records and stopped at record 53 (PID `0x000000F7`, item), while `maps/arcaves.map` parsed 133 of 4,528 and stopped at record 132 (PID `0x020003A6`, scenery subtype 0 / door). Generic records 3 and 76 had already been traversed. Door support covers the `arcaves.map` stopping record's confirmed one-word layout; current post-change stopping points in the real samples have not been revalidated. Item decoding has synthetic fixture coverage only; neither complete parsing nor local-sample validation is claimed. Fallout 1 object records have not been validated against local archive samples in this task.

Fallout 1 CE accepts MAP version 19; Fallout 2 CE accepts versions 19 and 20. The object-section order and the implemented base/critter/exit-grid layouts are the same in the inspected source revisions. Fallout 2 `DOOR` scenery has one additional `openFlags` word, `STAIRS` has two destination words, `ELEVATOR` has two runtime fields, `LADDER_UP`/`LADDER_DOWN` have version-dependent destination fields, and `GENERIC` has none after data flags. All six identified scenery types are supported for Fallout 2 as specified by the layout table below; this does not imply complete MAP parsing.

### Targeted PRO-dependent MAP record lengths

Fallout 2 CE does not store a record byte length in the MAP object. `objectRead` in [`src/object.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/object.cc#L412) reads the 18-word base, then calls `objectDataRead` in [`src/proto.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L554). The data reader consumes the three inventory words and, for non-critters, the data-flags word. For item and scenery PIDs it calls `protoGetProto(pid, ...)`, then branches on the resolved prototype subtype to read additional MAP-state fields. The next record begins immediately after those fields and any recursively serialized inventory children.

For scenery specifically, `proto->scenery.type` determines the number of additional 32-bit MAP words after `data flags`:

| PRO `scenery.type` | CE subtype | MAP-state words after flags | Fields read by `objectDataRead` |
| ---: | --- | ---: | --- |
| 0 | Door | 1 | `openFlags` |
| 1 | Stairs | 2 | `destinationBuiltTile`, `destinationMap` |
| 2 | Elevator | 2 | `type`, `level` |
| 3 | Ladder up | 1 for MAP v19; 2 for v20 | v19 `destinationBuiltTile`; v20 `destinationMap`, then `destinationBuiltTile` |
| 4 | Ladder down | 1 for MAP v19; 2 for v20 | v19 `destinationBuiltTile`; v20 `destinationMap`, then `destinationBuiltTile` |
| 5 | Generic | 0 | No additional MAP-state word |

For Item objects, Fallout 2 CE selects additional MAP fields using `proto->item.type`. The confirmed subtype word counts are:

| PRO `item.type` | Item subtype | MAP-state words after flags | Fields read by `objectDataRead` |
| ---: | --- | ---: | --- |
| 0 | Armor | 0 | None |
| 1 | Container | 0 | None |
| 2 | Drug | 0 | None |
| 3 | Weapon | 2 | `ammoQuantity`, `ammoTypePid` |
| 4 | Ammo | 1 | `quantity` |
| 5 | Misc | 1 | `charges` |
| 6 | Key | 1 | `keyCode` |

The consecutive item subtype names and values are defined in the pinned [`src/proto_types.h`, lines 26–35](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto_types.h#L26-L35). Fallout 2 CE `objectDataRead` reads the listed Weapon words in order and one word for Ammo, Misc, or Key; its default branch reads no additional Item payload ([pinned `src/proto.cc`, lines 579–599](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L579-L599)). `item.type` is read from the PRO common prefix at byte offset 32 ([pinned `src/proto.cc`, lines 1663–1685](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L1663-L1685)). The decoder preserves these MAP words raw rather than interpreting them.

The numeric subtype mapping is the consecutive `SceneryType` enum in [`src/proto_types.h`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto_types.h#L37). The engine's PRO reader `protoRead` in [`src/proto.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L1663) reads the scenery subtype at byte offset 32: three common 32-bit fields (`pid`, message id, FID), then five scenery 32-bit fields through `type`. This field is a discriminator in the PRO file; the variable MAP-state words are in the MAP stream. Other PRO scenery payload fields (`field_2C`, `field_34`, and subtype union data) are prototype data, not extra words consumed from the MAP object at that point.

The implemented prototype subtype readers do not decode the complete PRO format. They:

1. Resolve the PID low 24 bits as the 1-based line number in the type-specific scenery (`proto\\scenery\\scenery.lst`) or Item (`proto\\items\\items.lst`) list, following `_proto_list_str` in pinned [`src/proto.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L201-L248).
2. Resolve that list entry to its `.PRO` resource. `_proto_load_pid` obtains this name, opens the file, and passes it to `protoRead` in [`src/proto.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L1948).
3. The MAP record-length path needs only the fixed prefix through byte 35 to verify that the embedded PRO PID exactly matches the MAP PID and has the expected object type, then validate the subtype value.
4. Advance only for the confirmed scenery and Item layouts in their tables. Stop rather than guessing on missing/malformed resources or unsupported subtype values.

The shared prototype resolver implements these checks for Fallout 2 DAT2 archives and caches list data, resolved locations, and raw prototype bytes during one decode operation. Read failures retain full exception diagnostics in debug output while the object status remains concise. MAP Scenery state and Item instance payload words are preserved raw in source stream order; the separate selected-object `.PRO` diagnostics interpret only the confirmed prototype fields documented below. Prototype-dependent Fallout 1 layouts remain unsupported. Resource names are selected by the type-specific list rather than inferred from PID digits.

### Scenery PRO field diagnostics

The selected-object `.PRO` diagnostics separately expose confirmed Scenery and Item prototype fields; they do not change MAP decoding. The pinned CE `protoRead` reads the Scenery common fields in order as 32-bit values, and the FODev PRO reference specifies big-endian numeric fields unless noted otherwise ([FODev PRO format](https://fodev.net/files/fo2/pro.html); [pinned CE `proto.cc`, `protoRead`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L1663-L1708)):

| PRO offset | Size/type | Displayed field | Qualification |
| ---: | --- | --- | --- |
| `0x04` | 4-byte `int32` BE | `messageId` | Common Scenery field |
| `0x08` | 4-byte `int32` BE | `fid` | Displayed as its full hexadecimal identifier |
| `0x0C` | 4-byte `int32` BE | `lightDistance` | Common Scenery field |
| `0x10` | 4-byte `int32` BE | `lightIntensity` | Common Scenery field |
| `0x14` | 4-byte `int32` BE | `flags` | Displayed as a raw bitmask; individual bits are not decoded |
| `0x18` | 4-byte `int32` BE | `extendedFlags` | Displayed as a raw bitmask; individual bits are not decoded |
| `0x1C` | 4-byte `int32` BE | `sid` | `-1` is displayed as “No script” |
| `0x20` | 4-byte `int32` BE | `type` / subtype | Discriminator values 0–5 are Door, Stairs, Elevator, Ladder Up, Ladder Down, and Generic ([pinned CE `proto_types.h`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto_types.h#L37-L45)) |
| `0x24` | 4-byte `int32` BE | `material` | CE exposes `field_2C` as `SCENERY_DATA_MEMBER_MATERIAL`; the FODev material table maps 0–7 to Glass, Metal, Plastic, Wood, Dirt, Stone, Cement, and Leather ([pinned CE `proto.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L1218-L1245); [FODev PRO format](https://fodev.net/files/fo2/pro.html)) |
| `0x28` | 1-byte `uint8` | `field_34` | CE reads and writes this byte as `scenery.field_34`; its semantics are not confirmed, so display it as uninterpreted raw data. |
| `0x29` | 4-byte `int32` BE | Subtype payload | Fields below are read by CE's `protoSceneryDataRead` ([pinned CE `proto.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L1629-L1656)) |
| `0x2D` | 4-byte `int32` BE | Second subtype payload word, where present | Only present for Door, Stairs, and Elevator |

The subtype payload is displayed with CE's field names: Door has `openFlags` at `0x29` and `keyCode` at `0x2D`; Stairs has `stairs.field_0` and `stairs.field_4`; Elevator has `elevator.type` and `elevator.level`; Ladder Up/Down has `ladder.field_0`; Generic has `generic.field_0`. CE explicitly reads and writes `generic.field_0` as one 32-bit value at `0x29`; it is a serialized subtype field, not alignment padding. Its meaning is not established, so the parser marks it as uninterpreted and preserves its raw value. The inspected Generic record contains `0xCCCCCCCC` in those four serialized bytes; no semantics have been confirmed for that value. Other placeholder fields and Elevator values are likewise shown as raw numeric/hex values without further semantic mapping. The byte at `0x28` is named `field_34` by CE and stored as `uint8`; its semantics are not confirmed, so the parser displays it as uninterpreted raw data. The nominal serialized Scenery structure sizes are `0x31` bytes for subtypes 0–2 and `0x2D` bytes for subtypes 3–5 ([FODev type-size table](https://fodev.net/files/fo2/pro.html); pinned CE [`protoSceneryDataRead`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L1629-L1659)). CE's `protoRead` returns after reading those subtype fields without checking for end-of-file, so trailing bytes are ignored by its reader. The pinned `protoWrite` Scenery case also falls through into Wall serialization, and `_proto_save_pid` writes that result to the resource file; this can append 24 bytes after the nominal Scenery structure ([`protoRead`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L1663-L1709), [`protoWrite` and `_proto_save_pid`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L1848-L1942)). Whether that fall-through is intentional is unclear, but it demonstrates that a longer Scenery resource can be produced by the pinned writer and accepted by its reader. The resolver therefore retains and accepts trailing bytes, reports truncation for short resources, and displays only complete fields with unconfirmed meaning explicitly marked. The complete raw-byte dump is retained for every case.

### Item PRO field diagnostics

The selected-object Item diagnostic now displays the confirmed Item common fields and subtype payload. Fallout 2 CE reads the common Item fields in order in pinned [`src/proto.cc`, `protoRead` at lines 1663–1685](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L1663-L1685); the subtype readers and their field order are at lines 1553–1660 of the same file. The Item subtype values are defined in [`src/proto_types.h`, lines 26–35](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto_types.h#L26-L35), and Item is PID type 0 in [`src/obj_types.h`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/obj_types.h). FODev specifies big-endian signed 32-bit numeric values unless a byte is explicitly marked, documents the common Item prefix and its payload start at `0x39`, and lists the subtype sizes and fields ([FODev PRO format](https://fodev.net/files/fo2/pro.html)).

| PRO offset | Size/type | Field | Qualification |
| ---: | --- | --- | --- |
| `0x04` | 4-byte `int32` BE | `messageId` | Common field |
| `0x08` | 4-byte `int32` BE | `fid` | Displayed as a hexadecimal identifier |
| `0x0C` | 4-byte `int32` BE | `lightDistance` | Common field |
| `0x10` | 4-byte `int32` BE | `lightIntensity` | Common field |
| `0x14` | 4-byte `int32` BE | `flags` | Raw bitmask; individual bits are not decoded |
| `0x18` | 4-byte `int32` BE | `extendedFlags` | Raw bitmask; individual bits are not decoded |
| `0x1C` | 4-byte `int32` BE | `sid` | `-1` is displayed as “No script” |
| `0x20` | 4-byte `int32` BE | `type` / subtype | Shown in the existing subtype metadata; values 0–6 are Armor, Container, Drug, Weapon, Ammo, Misc, and Key |
| `0x24` | 4-byte `int32` BE | `material` | Values 0–7 use the documented material names |
| `0x28` | 4-byte `int32` BE | `size` | Common Item field |
| `0x2C` | 4-byte `int32` BE | `weight` | Common Item field |
| `0x30` | 4-byte `int32` BE | `cost` | Common Item field |
| `0x34` | 4-byte `int32` BE | `inventoryFid` | Displayed as a hexadecimal identifier |
| `0x38` | 1-byte `uint8` | `field_80` | Displayed as uninterpreted raw data; its semantics are not confirmed. |

Each subtype payload starts at `0x39`. All listed fields below are 4-byte `int32` BE except `weapon.soundCode`, which is 1-byte `uint8`, as read by CE's `fileReadUInt8`.

| Subtype | Total PRO size | Confirmed payload fields and offsets |
| --- | ---: | --- |
| Armor (`0`) | `0x81` | `armorClass` `0x39`; `damageResistance[0..6]` `0x3D`, `0x41`, `0x45`, `0x49`, `0x4D`, `0x51`, `0x55`; `damageThreshold[0..6]` `0x59`, `0x5D`, `0x61`, `0x65`, `0x69`, `0x6D`, `0x71`; `perk` `0x75`; `maleFid` `0x79`; `femaleFid` `0x7D` |
| Container (`1`) | `0x41` | `maxSize` `0x39`; `openFlags` `0x3D` (raw bitmask) |
| Drug (`2`) | `0x7D` | `stat[0..2]` `0x39`, `0x3D`, `0x41`; `amount[0..2]` `0x45`, `0x49`, `0x4D`; `duration1` `0x51`; `amount1[0..2]` `0x55`, `0x59`, `0x5D`; `duration2` `0x61`; `amount2[0..2]` `0x65`, `0x69`, `0x6D`; `addictionChance` `0x71`; `withdrawalEffect` `0x75`; `withdrawalOnset` `0x79` |
| Weapon (`3`) | `0x7A` | `animationCode` `0x39`; `minDamage` `0x3D`; `maxDamage` `0x41`; `damageType` `0x45`; `maxRange1` `0x49`; `maxRange2` `0x4D`; `projectilePid` `0x51`; `minStrength` `0x55`; `actionPointCost1` `0x59`; `actionPointCost2` `0x5D`; `criticalFailureType` `0x61`; `perk` `0x65`; `rounds` `0x69`; `caliber` `0x6D`; `ammoTypePid` `0x71`; `ammoCapacity` `0x75`; `soundCode` `0x79` (`uint8`) |
| Ammo (`4`) | `0x51` | `caliber` `0x39`; `quantity` `0x3D`; `armorClassModifier` `0x41`; `damageResistanceModifier` `0x45`; `damageMultiplier` `0x49`; `damageDivisor` `0x4D` |
| Misc (`5`) | `0x45` | `powerTypePid` `0x39`; `powerType` `0x3D`; `charges` `0x41` |
| Key (`6`) | `0x3D` | `keyCode` `0x39` |

The subtype-specific sizes match the FODev Item table. A size mismatch is reported against the selected subtype; the resolver retains all raw bytes and emits only fields whose complete byte ranges are present. It displays the byte at `0x38` as uninterpreted `field_80`. The separate Weapon payload byte at `0x79` is named `soundCode` by CE and remains decoded as that byte field. Numeric enums such as damage type, caliber, perks, or stat IDs are not mapped to names.

For Items, the `item.type` enum values 0–6 are Armor, Container, Drug, Weapon, Ammo, Misc, and Key respectively ([pinned `src/proto_types.h`, lines 26–35](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto_types.h#L26-L35)). In Fallout 2 CE `objectDataRead`, Weapon reads `ammoQuantity` then `ammoTypePid`; Ammo reads `quantity`; Misc reads `charges`; Key reads `keyCode`; Armor, Container, and Drug fall through the default branch without extra words ([pinned `src/proto.cc`, lines 579–599](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L579-L599)). `protoRead` reads `item.type` at byte offset 32 ([pinned `src/proto.cc`, lines 1663–1685](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L1663-L1685)). The decoder consumes only the corresponding MAP payload word counts and does not infer size from PRO file length.

For the Stairs layout, Fallout 2 CE `objectDataRead` switches on the resolved `proto->scenery.type` and, for `SCENERY_TYPE_STAIRS`, reads `destinationBuiltTile` then `destinationMap` as two consecutive 32-bit words ([pinned `src/proto.cc`, lines 609–612](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L609-L612)). The enum identifies subtype 1 as `SCENERY_TYPE_STAIRS` ([pinned `src/proto_types.h`, lines 37–44](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto_types.h#L37-L44)). This establishes both the count and order of the additional MAP words; the decoder preserves them without assigning further semantics.

For Elevator, the same `objectDataRead` switch handles `SCENERY_TYPE_ELEVATOR` by reading `obj->data.scenery.elevator.type` followed by `obj->data.scenery.elevator.level`, each as one 32-bit word ([pinned `src/proto.cc`, lines 613–616](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L613-L616)). The enum orders `SCENERY_TYPE_ELEVATOR` as subtype 2 ([pinned `src/proto_types.h`, lines 37–44](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto_types.h#L37-L44)). The MAP fields are thus not inferred from the Stairs layout; their distinct names and read order are explicit in CE source. This decoder preserves both values raw rather than interpreting their runtime meaning.

For both `SCENERY_TYPE_LADDER_UP` (subtype 3) and `SCENERY_TYPE_LADDER_DOWN` (subtype 4), Fallout 2 CE `objectDataRead` checks `gMapHeader.version == 19`. In that branch it reads exactly one 32-bit `destinationBuiltTile`; otherwise it reads `destinationMap` followed by `destinationBuiltTile`, each as 32-bit words ([pinned `src/proto.cc`, lines 617–632](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto.cc#L617-L632)). `SCENERY_TYPE_LADDER_UP` and `SCENERY_TYPE_LADDER_DOWN` are enum values 3 and 4 respectively ([pinned `src/proto_types.h`, lines 37–45](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/proto_types.h#L37-L45)). The MAP version is taken from the header metadata after the decoder revalidates it against the MAP bytes; v20 is the only other accepted Fallout 2 version. This decoder preserves those words raw in the order read.

Nested inventory is a separate boundary dependency. The three MAP words are `inventory.length`, `inventory.capacity`, and a serialized pointer placeholder; CE does not use the serialized pointer to locate children. In `objectLoadAllInternal`, a positive length is followed by exactly `length` quantity words and child objects; `_obj_load_obj` in [`src/object.cc`](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/object.cc#L3517) recursively reads each child's own object data and inventory. Capacity is used for runtime allocation, not as the number of serialized children. Therefore, the presence of nested inventory is detectable immediately from the MAP length, but the child sequence cannot be skipped with a fixed-size seek. Safe traversal requires the same bounded recursive decoder for each child, including subtype-specific PRO lookup, validation of nonnegative length and sensible capacity, plus explicit maximum depth, object-count and byte-range limits. The `settings.mapper.fix_map_inventory` branch in `objectLoadAllInternal` is a runtime configuration path that reads inventory children with `objectRead` instead of the normal recursive helper; it does not establish a universal on-disk child size and should not be imitated as a general skip rule.

### Cross-check against local Fallout 2 samples

The installed `master.dat` was read without modifying it. Both MAPs are version 20. Before PRO lookup, the parser stopped at elevation 0 record 3 in `maps/arbridge.map` and record 76 in `maps/arcaves.map`; including the partially decoded stopping record, those results were 4/614 and 77/4,528 respectively. Subsequent read-only checks showed that generic scenery at those records can be advanced. A later production check, before Door and Stairs support, parsed 54/614 top-level records in `arbridge.map` and stopped at record 53, PID `0x000000F7` (item), and parsed 133/4,528 in `arcaves.map` before stopping at record 132, PID `0x020003A6` (Door). Door and Stairs layouts are now implemented, but these MAPs have not been rechecked against the current decoder; do not treat those historical stopping points as current. Counts include the retained stopping record.

| MAP | Stopping record | MAP PID | PRO resolved through scenery list | PRO PID / `scenery.type` | Inventory length | Implication |
| --- | ---: | ---: | --- | --- | ---: | --- |
| `maps/arbridge.map` | 3, byte 41,612 | `0x02000031` | `proto\\scenery\\00000050.pro` | `0x02000031` / 5 (`GENERIC`) | 0 | No MAP payload follows data flags; the record can be advanced without other PRO fields. |
| `maps/arcaves.map` | 76, byte 132,356 | `0x02000158` | `proto\\scenery\\00000345.pro` | `0x02000158` / 5 (`GENERIC`) | 0 | Same confirmed zero-extra-word case. |

Both matched PRO resources are 45 bytes in this installation, and the `scenery.type` word at offset 32 is `5`. This directly cross-checks that the next MAP object can follow after the data-flags word. A separate bounded, source-guided traversal applying the full CE scenery subtype word counts advanced past those records and then encountered nested inventory at record 60 in `arbridge.map` (PID `0x01000004`) and record 614 in `arcaves.map` (PID `0x000000FA`). That traversal is diagnostic only: it additionally advances scenery subtypes and Item layouts beyond the supported standalone Item subset. Recursive inventory remains a future stopping condition if preceding layouts are implemented.

### Smallest safe follow-up and remaining limits

The targeted Fallout 2 PRO-list/prototype lookup now advances validated Door, Stairs, Elevator, Ladder Up/Down, and GENERIC scenery records, plus Item records with the confirmed subtype layouts above, without parsing unrelated PRO fields. The selected-object Scenery diagnostic additionally displays the confirmed PRO fields above while leaving unknown fields raw. The decoder recursively reads the inventory of each record whose inventory-length field is positive and preserves the raw `tile` value of every nested record; unsupported object/subtype layouts still stop safely. Unknown subtype/PID combinations fail closed.

This research verifies Fallout 2 CE at the pinned revision and the local Fallout 2 samples. It does not independently establish identical PRO list or subtype behavior for Fallout 1 CE, third-party archives/mods, malformed PRO data, or all MAP versions. Do not reuse Fallout 2's exact resource-resolution assumptions for Fallout 1 without checking its corresponding pinned source and samples.

**Next milestone:** validate the supported Item subset against real MAP samples, then separately investigate recursive inventory with explicit child-boundary, depth, object-count, and allocation limits. Do not interpret a partial top-level list as a complete map object set.

## Diagnostic object hex-grid view

The first object-placement view is a diagnostic overlay separate from the 100 × 100 square tile-grid view. It consumes `MapObjectPlacement.HexX`, `HexY`, and `ElevationGroup` already produced by the decoder; it does not derive object coordinates from square-grid indices. Objects with missing or out-of-range hex coordinates are retained in the placement list but omitted from the overlay.

The relative point projection follows Fallout 2 CE at the pinned revision. `tileToScreenXY` converts the serialized tile into `v3 = 199 - tile % 200` and `v4 = tile / 200`, then applies the center-relative x/y and odd-column branches ([`src/tile.cc`, lines 677–709](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/tile.cc#L677-L709)); the grid bounds are 200 × 200 ([`src/map_defs.h`, lines 8–25](https://github.com/alexbatalov/fallout2-ce/blob/e97087b9582f37075db347a89898887320753f8b/src/map_defs.h#L8-L25)). The view normalizes the center to the even column `(100, 100)` and applies the source's relative pixel deltas:

```text
dx = hexX - 100
dy = hexY - 100
screenX = 48 * trunc(dx / 2) + 16 * dy
screenY = 12 * trunc(dx / -2) + 12 * dy
if hexX is odd and hexX <= 100: screenX -= 16; screenY += 12
if hexX is odd and hexX > 100:  screenX += 32
```

The projected points are uniformly scaled and centered to fit the diagnostic control. This provides a parity-aware coordinate visualization, not the game's tile/sprite renderer or exact inverse hit-testing. Camera scrolling, art bounds, animation, roof offsets, and runtime picking masks are intentionally not reproduced. Repeated clicks on a marker cycle among records at that exact hex; the existing object list remains the direct way to select a particular record. Selecting a record in the list switches the overlay to that record's elevation.
