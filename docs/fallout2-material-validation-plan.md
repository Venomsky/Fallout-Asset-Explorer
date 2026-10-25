# Fallout 2 PRO `material` validation plan

## Status and scope

This document defines a repeatable evidence plan for `material`; it does not approve the field, change its current interpretation, or authorize editing. The production field registry must remain empty and resolver-derived fields must remain read-only until a separate review accepts evidence for an exact schema.

The field is currently decoded by FAE as a signed big-endian 32-bit integer at record-relative offset `0x24` for Item and Scenery records. That describes current project behavior, not an independently established retail format contract.

The plan covers four distinct questions:

1. **Serialized layout:** exact offset, width, and byte order.
2. **Semantics:** what each value means in-game or in the Fallout 2 mapper.
3. **Domain and rules:** accepted values, special values, and type/subtype/version constraints.
4. **Read/write behavior:** whether the value survives an actual supported read/edit/write/reopen path without changing unrelated bytes.

Evidence for one question must not be used as a substitute for evidence for another. In particular, correctly decoding a sample does not prove its interpretation, complete value range, or write compatibility.

## Existing evidence and limitations

The evidence and sample reports are summarized in [fallout2-prototype-editor-model.md](./fallout2-prototype-editor-model.md). Current knowledge relevant to this plan:

| Claim | Existing support | Limitation |
|---|---|---|
| Offset `0x24`, width 4 | FAE reads four bytes there for Item and Scenery. CE-derived documentation/source describe the same mapping. Historical format material reports a four-byte material value. | CE and FODEV are one lineage; historical Fandom credits most content to TeamX. No independently verified retail record currently establishes the mapping. |
| Big-endian | FAE decodes the field as `int32 BE`; CE read/write behavior and source-derived documentation agree. User-reported positive samples contain `00 00 00 01` and `00 00 00 07`. | The hashes and bytes in those reports have not been independently checked. Synthetic tests establish FAE behavior only. |
| Signedness | Current FAE model uses `int32`. | Positive values cannot distinguish signed from unsigned storage; negative/sentinel behavior is unknown. |
| Values `0..7` | Historical documentation lists Glass, Metal, Plastic, Wood, Dirt, Stone, Cement, and Leather, respectively. User reports include value `1` (`Metal`) and `7` (`Leather`). | The documentation lineage is not independent of TeamX, and the reported samples are unverified. These observations do not establish every mapping or prove that `0..7` is the complete domain. |
| Applicable records | User reports cover Item Armor (`0`), Weapon (`3`), and Ammo (`4`). FAE also decodes `material` for Item and Scenery. | Item subtypes `1`, `2`, `5`, and `6`, and all Scenery subtypes, lack independently checked samples in the current evidence record. |
| Write compatibility | The model and in-memory draft builder can represent a validated 4-byte patch. | FAE has no full PRO serializer or disk-write path. A draft patch is not evidence of game-compatible write behavior. |

The current schema distinguishes game, archive format, PID type, and subtype, but not a specific game release or patch. Record release/version in every evidence manifest. If evidence shows version-dependent behavior, do not generalize it to the current schema: resolve the scope and any required schema-versioning work separately before considering approval.

## Evidence collection protocol

This is a future, explicitly authorized workflow; it does not request or perform local archive scanning in this stage.

1. **Select a bounded sample set.** Obtain individual PRO records from a named Fallout 2 distribution. Do not enumerate or scan local archives; select a known entry directly, or use an individually supplied or published sample with traceable provenance. Do not copy full game archives into the repository. Record the archive identity and hash where available, plus a hash of each extracted record.
2. **Establish record identity independently.** Verify PID type, subtype, record path, and the relevant `proto/items/items.lst` or `proto/scenery/scenery.lst` association using a second method or a separately reviewed source. Preserve whether the sample was list-resolved or merely selected by path.
3. **Capture the bytes without interpretation.** Record the complete record length and SHA-256. Record the four bytes at `0x24` exactly as hex. Do not initially label them signed, unsigned, or a material name.
4. **Verify layout separately.** Use a source or tool outside the FAE parser and outside the same documentation/code lineage. Compare the proposed byte range with the independently identified record and subtype. Include records whose byte patterns distinguish big-endian from little-endian interpretations.
5. **Verify semantics separately.** Confirm observed values using an independent authoritative description, Fallout 2 mapper view, or game behavior. Capture exactly which value and record were checked; do not infer unobserved labels.
6. **Investigate the domain.** Seek a source that enumerates accepted values or validates them in the game/editor. Probe any proposed sentinel or negative values only with provenance-backed records or an independently documented rule. Do not infer a complete domain from the values present in ordinary records.
7. **Check read/write behavior only through a supported path.** Compare an independent reader/writer or a trusted game/editor round trip. Reopen the result and verify the field value, the record identity, and preservation of every unrelated byte. Until FAE has a full serializer, its in-memory draft tests cover only patch construction, not PRO round-trip compatibility.
8. **Review evidence before registry changes.** Confirm each claim's scope, provenance group, contradictions, and unresolved limitations. Registry inclusion is a separate decision; this plan itself authorizes no production definition.

### Bounded evidence collection without archive scanning

Possible future collection paths that do not require scanning local archives:

- A user selects a known `.PRO` entry by exact archive path in an existing browser and provides that one extracted record, its full-record hash, and the exact game distribution details. Do not enumerate other archive entries.
- Use a published standalone `.PRO` sample only when its author, game release, path/PID, and sample digest are traceable. A sample with no provenance is useful only as an unverified observation.
- Ask for an independently captured report of one known entry, including the source archive identity and archive hash if available. Independently verify its PID/list association without searching for more records.
- Check a specific known record in a version-identified Fallout 2 mapper or game build to observe the material label or behavior. Record the selected record and operation; do not infer other values from it.
- Consult pinned, publicly accessible source revisions and documentation to identify testable claims and source lineage. These may support implementation or semantic claims but cannot replace a provenance-backed retail record when the claim concerns retail bytes.

If the archive hash cannot be obtained, record why and provide another immutable source identifier (such as publisher/distribution ID or a canonical archived URL and capture date). An unidentifiable source must remain `needs more evidence`.

### Coverage matrix

The registry schema is subtype-specific. Treat these as separate applicability questions, not as one automatically universal Item/Scenery field:

| PID type | Subtypes to cover | Existing user-reported observations |
|---|---|---|
| Item (`0`) | Armor `0`, Container `1`, Drug `2`, Weapon `3`, Ammo `4`, Misc `5`, Key `6` | Armor `0`, Weapon `3`, Ammo `4`; reports remain unverified |
| Scenery (`2`) | Door `0`, Stairs `1`, Elevator `2`, Ladder Up `3`, Ladder Down `4`, Generic `5` | None independently checked |

A claim that `material` is a shared common field may justify a common layout only if an independent source explicitly establishes that scope. Otherwise, collect record evidence for each subtype to which a future definition would apply. A sample from one subtype must not silently authorize another.

## Evidence manifest

Use one UTF-8 JSON manifest per record, stored separately from any full game archive. Keep machine-specific absolute paths out of the manifest. A minimal template is:

```json
{
  "manifestVersion": 1,
  "sampleId": "fallout2-material-<unique-id>",
  "game": {
    "title": "Fallout 2",
    "editionOrDistribution": "<retail / release / distribution>",
    "versionOrPatch": "<exact value or unknown>",
    "language": "<value or unknown>",
    "platform": "<value or unknown>"
  },
  "source": {
    "archiveName": "<for example master.dat>",
    "sourceReference": "<publisher/distribution ID, canonical URL, or controlled evidence ID>",
    "acquisitionProvenance": "<physical media, trusted URL, or other chain>",
    "acquiredOn": null,
    "archiveHash": {
      "algorithm": "SHA-256",
      "value": null
    },
    "memberPath": "proto/ITEMS/<record>.PRO",
    "extractionTool": "<name and version>",
    "extractionMethod": "<direct entry extraction, independent of FAE if available>",
    "recordArtifactReference": "<immutable reference to this standalone record, or re-extraction instructions>"
  },
  "record": {
    "sha256": "<hash of the complete extracted PRO record>",
    "lengthBytes": null,
    "pidRaw": "<8-digit hex>",
    "pidType": null,
    "subtype": null,
    "listAssociation": {
      "listPath": "<list path>",
      "lineNumber": null,
      "lineText": "<exact entry or unknown>",
      "verificationMethod": "<tool/source and version>"
    }
  },
  "fieldObservation": {
    "name": "material",
    "recordRelativeOffset": "0x24",
    "widthBytes": 4,
    "rawBytesHex": "<exactly eight hex digits>",
    "candidateInterpretations": [
      {
        "representation": "int32 big-endian",
        "value": null
      },
      {
        "representation": "uint32 big-endian",
        "value": null
      }
    ],
    "semanticLabel": null
  },
  "independentVerification": [
    {
      "claim": "<layout, meaning, domain, or round-trip behavior>",
      "method": "<specific procedure>",
      "toolOrSource": "<name, version/commit, and location>",
      "provenanceGroup": "<lineage identifier>",
      "verifiedOn": "<YYYY-MM-DD>",
      "result": "<confirmed, contradicted, or inconclusive>",
      "scope": "<exact record types, subtypes, values, and versions covered>",
      "notes": "<limitations or conflicts>"
    }
  ]
}
```

This is a typed JSON template: numeric fields remain numbers or `null`, hexadecimal byte values remain strings, and a placeholder string must not be substituted for an unknown number. Replace all explanatory placeholders before recording a sample. `recordArtifactReference` must identify a retrievable standalone record or explain how to re-extract the exact record from the identified source; it must not be a machine-specific absolute path.

Never invent a game version, list association, hash, or semantic label. Hash the source archive only when its exact source bytes are available and doing so is authorized. If that hash is unavailable, record the reason and the immutable alternative source reference. A full archive need not be stored in the repository. If a future data-backed test needs the complete record, use a separately approved, narrowly scoped PRO fixture or controlled external evidence store, and retain its manifest and digest. If neither the archive nor the standalone record can be re-acquired or verified, the manifest is an observation record, not reproducible evidence for acceptance.

## Comparing samples and interpreting their strength

Compare samples only after confirming their release, archive provenance, record hash, PID/type/subtype, and extraction method. Then compare the raw four-byte sequences at the same record-relative offset.

- Two independently verified records with identical bytes confirm that byte sequence in those records; they do **not** establish a complete range.
- Two records with different bytes can support a layout claim if both are independently located and the offset/width are corroborated. They support a semantic mapping only when each value is independently identified.
- The same value in multiple records is repeated evidence of occurrence, not evidence that no other value is valid.
- Observing all labels `0..7` would support those eight observed mappings, but alone would not exclude values such as `-1`, `8`, or other sentinels.
- A complete-domain claim needs an explicit validating specification, exhaustive source-level constraints, or equivalent evidence that accounts for exceptional values and subtype/version rules. A set of ordinary records is not exhaustive evidence.
- Multiple records from one archive are multiple samples, but share one distribution lineage. Two tools reading the same sample can independently validate parsing; they do not make the underlying game-data provenance independent.
- Agreement between a fork, port, copied documentation, or tool derived from the same source does not count as independent corroboration. Record lineage explicitly rather than counting URLs or project names.

## Source independence rules

Classify evidence by origin and derivation, not by where it is hosted:

- **Original game record:** a record with a documented distribution/version, archive identity, reproducible extraction, record hash, PID/subtype, and independently checked list association. Multiple records from one archive remain one distribution lineage.
- **Independent specification:** a source with a traceable author/version and its own provenance for the claim. A source that transcribes TeamX, CE, or another document belongs to that source's lineage.
- **Independent implementation:** a reader/writer whose format logic was developed from a distinct evidence path. A fork, port, wrapper, or implementation explicitly based on CE/RE is not an independent implementation for corroboration.
- **Runtime/editor verification:** an independently identified game or mapper behavior can support semantic meaning and accepted values. Record the exact version, operation, and outcome; a UI label alone does not prove serialized layout.
- **Project code and tests:** FAE resolver code confirms what FAE currently reads. Synthetic tests confirm only the code's behavior. Neither is external format evidence.

For the current source record, CE and FODEV are one lineage; CE states it is based on RE; and the archived Fandom article credits most of its content to TeamX. Treat these relationships as dependencies, not as multiple independent confirmations. The historic TeamX archive has not been inspected, so its specific claims must remain unverified until the source itself is reviewed.

The current `CreateReviewed` registry path requires evidence claims to be marked independently confirmed and to use an allowed evidence kind, but it does not determine whether two claims share a lineage or whether a source is genuinely independent. Review `provenanceGroup` and source derivation manually; the enum/status check is not a provenance audit.

## Claim-specific evidence thresholds

| Question | Minimum evidence to support the claim | What is insufficient by itself |
|---|---|---|
| Offset `0x24` | Exact record-relative byte position independently checked in provenance-backed records, plus an independent format/source path that identifies the slot for the relevant schema. Confirm applicable subtype scope. | FAE's offset, a synthetic fixture, a user-reported offset without raw record verification, or an in-memory struct offset. |
| Width 4 | Four-byte field boundaries confirmed from record layout and an independent parser/specification; check record lengths and neighboring fields for every authorized schema. | A single 4-byte read by FAE or a coincidental sequence of four bytes. |
| Endian | A discriminating raw byte pattern (not merely a symmetric value) and an independent decoder or source path that yields the same result. | A label from FAE alone, or positive sample reports whose bytes have not been checked. |
| Signedness | A source or behavior that distinguishes signed from unsigned interpretation, including treatment of sign-bit-set values and any negative/sentinel values. | Positive values `0..7`; the C# type selected by the current resolver. |
| Meanings of `0..7` | Each claimed mapping supported by a traceable specification or independently observed game/mapper interpretation tied to the same record/value. | A repeated enum copied across dependent docs, or labels inferred from one or two samples. |
| Complete domain and special values | Evidence that enumerates or validates the full accepted set, including sentinels, invalid values, subtype constraints, and release-specific behavior. | Observing eight values, assuming an enum is exhaustive, or setting min/max from the labels alone. |
| Read/write compatibility | Independent read/write or trusted game/editor reopen test on a provenance-backed record; field value remains correct and all unrelated bytes remain unchanged. | FAE synthetic tests or the in-memory draft builder alone. |

## Decision outcomes

These outcomes describe the evidence review only; none automatically changes code or editability.

- **Accept:** only the specific claim is supported at a clearly stated scope by the evidence type required for that claim. A serialized-layout claim requires reproducible, provenance-backed original records or an independently traceable format specification, with independent corroboration where available; a semantic claim requires a verified mapping to game/editor behavior or an independent semantic source; a domain claim requires evidence that enumerates or validates the domain; and a read/write claim requires a verified reopen/round-trip path. No unresolved contradiction affects the accepted claim. An `accept` for one subtype or observed value does not approve other subtypes or the complete domain. Synthetic fixtures and FAE tests alone can never accept a retail-format claim.
- **Reject:** reproducible evidence directly contradicts the claim for the same documented game version and schema, or the evidence is shown to be misidentified, corrupted, or non-reproducible. Record the contradictory bytes/source and scope. A mere lack of evidence is not rejection.
- **Needs more evidence:** provenance is incomplete, only dependent sources agree, samples cover too few schemas/values, signedness/domain is unresolved, or read/write compatibility is untested. This is the default outcome for any claim that does not meet its threshold.

Even after evidence is accepted, production registration requires a separate review of schema scope, minimum/maximum bounds, representation, cross-field rules, tests, and registry policy. The existing registry requires independently confirmed claims and its schema currently has no game-version field; evidence must not be broadened beyond what the modeled schema can safely express.

## Future validation tests

When evidence collection is authorized, add tests only after the sample manifests have been reviewed:

1. Data-backed parsing fixtures assert the exact four source bytes, offset, record identity, subtype, and expected decoded value; fixture digests must match the manifest.
2. Separate cases cover each Item/Scenery subtype proposed for authorization, or explicitly justify shared coverage with an independent common-layout source.
3. Endian and signedness tests include discriminating/sign-bit-set examples only when those values are supported by evidence; synthetic vectors may test implementation mechanics but cannot supply format evidence.
4. Semantic mapping tests cover each individually substantiated value. Domain tests cover documented special values and invalid values without treating unobserved values as valid or invalid by default.
5. A future read/write test must reopen output through an independent implementation or trusted game/editor and compare all untouched bytes. Do not present draft-builder tests as serializer or retail compatibility tests.

Until those reviews are complete, `material` remains an inspection-only decoded field; no production registry definition, PRO writer, or file-save behavior is authorized by this plan.

## Stage 73 — single-record acquisition outcome

**Outcome: no suitable standalone retail `.PRO` record was obtained.** No local game archive was accessed, and no archive or game-data hash was computed in this stage.

### Existing user-reported candidate

The project evidence log reports `proto/ITEMS/00000001.PRO`, PID `0x00000002`, Item Armor subtype `0`, record size 129 bytes, and bytes `00 00 00 01` at offset `0x24`. It also records an exported-record SHA-256 supplied by the user. The raw record is not available with this report; its hash was not recomputed, the `items.lst` association was not independently checked, and the exact game release/language/patch is unknown. This remains a reported observation, not a reproducible sample.

### Public candidates checked

- [BGforge MLS fixture at pinned commit `96ff0d0a1dcfe425780178a74e77b6d56cfccc13`](https://github.com/BGforgeNet/BGforge-MLS/blob/96ff0d0a1dcfe425780178a74e77b6d56cfccc13/client/testFixture/proto/items/00000079.pro.json) is a structured JSON fixture. It reports Item/Weapon subtype `3` and `materialId: 1`, but contains no original record bytes at `0x24`, source archive identity, retail release, or record provenance. Its Git blob identifier is not a hash of the original PRO. It can be a lead for a semantic cross-check, but does not establish the serialized four-byte field.
- [IanOut file at pinned commit `9986728f487e2f2d48e8bbb4c565a098943b2c89`](https://github.com/sztupy/IanOut/blob/9986728f487e2f2d48e8bbb4c565a098943b2c89/_Output/mod/1984/proto/critters/00000001.pro) is XML for an IanOut module, not a Fallout binary PRO record. The repository [README](https://github.com/sztupy/IanOut/blob/9986728f487e2f2d48e8bbb4c565a098943b2c89/README.markdown) describes IanOut as a standalone engine with text-file assets that requires a legally acquired Fallout 2 copy; it does not establish this file as an original Fallout 2 record.
- GitHub code search for the known Item filenames did not return an indexed standalone binary PRO candidate. This is a bounded search result, not proof that no public sample exists; binary files may not be indexed.

### What is established and next action

No sample establishes that a retail record contains `00 00 00 01` (or any other bytes) at `0x24`; the existing report and JSON fixture only identify candidate values. Even a future verified record would establish only its own bytes and schema identity. It would not, by itself, establish that those bytes mean `material`, the complete value domain, signedness, applicability to other subtypes, or safe serialization.

The precise next action is to request one standalone original record for the already reported path `proto/ITEMS/00000001.PRO`, without requesting or scanning the containing archive. The submission should include the game distribution/version, source archive identity and SHA-256 if available, exact record path, complete-record SHA-256, extraction method, and independently verified PID/list association. A reviewer can then verify the record bytes at `0x24` and add a manifest entry. Until that artifact and provenance are checked, the outcome remains **needs more evidence**.

## Stage 74 — standalone sample verification workflow

The PRO Editor now has an inline **Verify one standalone PRO sample** panel. Use **Select and inspect .PRO** to choose exactly one file; the picker is restricted to `.PRO` files and does not use the archive browser, prototype resolver, or filename as a record identity. The selected stream is read in a bounded manner up to the existing 1 MiB inspection limit. The original file is never modified, and no output file is written.

Optional source/revision, edition, record identity, acquisition method, and expected-PID details are user-reported metadata. The panel labels supplied values `Reported` and absent values `Unknown`; comparing the embedded PID with a supplied expected PID checks consistency only and does not independently authenticate the source or identity. File length, SHA-256, available PID bytes, and bytes at `0x24` are observations of the selected input. The `0x24` display keeps the four raw bytes separate from an unsigned big-endian numeric view; it does not establish that the bytes are `material`, its signedness, valid domain, subtype coverage, or correct serialization.

The structural result checks only basic plausibility using current FAE PID/subtype conventions. It does not establish canonical subtype-specific lengths or release compatibility. Synthetic tests verify the workflow's handling and display of supplied bytes only; they are not evidence about retail Fallout 2 records. Continue to require independently reviewed provenance and claim-specific evidence before changing field status, registry authorization, or any read/write behavior.

### Stage 76 — verifier status behavior

The verifier now marks prototype list index zero unsupported, matching the existing FAE resolver convention; this remains an implementation consistency check, not independent format verification. A valid reported expected PID is preserved as normalized hexadecimal and marked `UnableToCompare` when the embedded PID is unavailable; malformed expected-PID input is reported as invalid even for short samples. Editing any provenance or expected-PID input, or cancelling the picker, clears the previous verification result so it cannot be confused with the current inputs.

## Stage 81 — pinned RPU standalone sample observation

The existing bounded standalone verifier was used through the FAE UI on the individually selected file [`data/proto/items/00000031.pro`](https://github.com/BGforgeNet/Fallout2_Restoration_Project/blob/2c5aff7c413271236e030d302c44cf006637e034/data/proto/items/00000031.pro) from `BGforgeNet/Fallout2_Restoration_Project`, pinned to commit `2c5aff7c413271236e030d302c44cf006637e034` (RPU v2.4.34 release context). The downloaded standalone file was reported as 81 bytes. This identifies the public repository sample examined; it does not authenticate it as a retail-original asset.

### Direct verifier observations

- SHA-256: `3E67FAFB875D396709B82CFAF50F47008B524CDD7E680A0F137775A4AD8A3BA1`.
- Embedded PID bytes: `0000001F`; interpreted under current FAE conventions as PID `0x0000001F`, PID type `0`, prototype-list index `31`.
- The user-entered expected PID was `0x0000001F`; the verifier reported `MatchesReportedExpectedPid`.
- Subtype was interpreted under current FAE conventions as `4`.
- Structure status: `PlausibleHeader`. The verifier warned that this indicates plausibility under current FAE parsing conventions only; subtype-specific total length and release compatibility are not validated.
- Raw bytes at offset `0x24`: `00000001`. The separate big-endian numeric display was `1` (`0x00000001`).

These are observations reported by the verifier for this selected file. The PID, PID type, prototype index, subtype, and numeric display use current FAE conventions; they are not independent format validation. Raw bytes and their numeric display do not establish a field name or meaning.

### Repository-derived context and provenance limits

At the same pinned commit, the repository's `data/proto/items/items.lst` places `00000031.pro` at line 31. Under FAE's current one-based Item-list convention, this suggests the expected PID `0x0000001F`; it is repository-derived context, not independent authentication of the record/list relationship. The expected PID and its reference were supplied by the user for comparison. Any provenance details absent from the verifier UI remained `Unknown`; the matching comparison does not authenticate those details or the source. The RPU repository and pinned commit identify where this sample was obtained, not whether its bytes are unchanged retail data.

This single RPU sample does not establish:

- that bytes at `0x24` mean `material`;
- field signedness or the valid value domain;
- subtype-wide or release-wide compatibility;
- complete subtype-specific record length; or
- serializer correctness or retail/game round-trip behavior.

### Independent validation goal

The next evidence goal is to obtain another provenance-backed sample or an authoritative, independently sourced format reference that can corroborate the proposed field semantics. Prefer a distinct provenance group, or original binary documentation with traceable authorship, version, and evidence. For another sample, independently establish its record identity and relevant list association, and document its source release and complete-record digest where available. Evaluate serialized layout, semantic interpretation, domain, and write compatibility as separate claims; this sample alone cannot satisfy those evidence thresholds.

A second repository copy is not independent confirmation merely because it has a different URL or maintainer. If it copied the same upstream asset, or derives its field labels and mappings from the same documentation, code, or sample lineage, the evidence is dependent. Record that shared provenance group and do not count the copy as corroboration.

The production field registry remains empty, and decoded `material` fields remain `ReadOnly`. This observation authorizes no production field definition, editability, serializer, or write path.

## Stage 84 — reverse-engineered engine evidence

This section records implementation evidence from two pinned repositories. These sources support interpretations made by those implementations; neither directly verifies the RPU sample's original retail provenance or establishes a version-pinned retail binary format contract.

### Fallout 2 Reference Edition

The [Fallout 2 Reference Edition repository](https://github.com/alexbatalov/fallout2-re) at revision `b135fc46ef40c4aecd156f3cebcf88ec531bb8ac` is a reverse-engineered engine reconstruction.

- [`src/game/proto.c`](https://github.com/alexbatalov/fallout2-re/blob/b135fc46ef40c4aecd156f3cebcf88ec531bb8ac/src/game/proto.c) contains Item material read and write paths. Their record order supports a four-byte integer field at the current source-derived `0x24` position.
- [`src/game/proto_types.h`](https://github.com/alexbatalov/fallout2-re/blob/b135fc46ef40c4aecd156f3cebcf88ec531bb8ac/src/game/proto_types.h) defines named material categories. The Scenery structure labels `field_2C` as material.
- [`src/game/gsound.c`](https://github.com/alexbatalov/fallout2-re/blob/b135fc46ef40c4aecd156f3cebcf88ec531bb8ac/src/game/gsound.c) uses material categories when selecting impact sounds.
- [`src/plib/db/db.c`](https://github.com/alexbatalov/fallout2-re/blob/b135fc46ef40c4aecd156f3cebcf88ec531bb8ac/src/plib/db/db.c) provides the integer read and write helpers used by these paths.

Together, these are **moderate, reverse-engineered engine evidence** that the Item field represents a material category in the reconstructed engine. They do not independently confirm the field position or behavior against an original, version-pinned retail binary.

### Falltergeist parser

At revision `32cbee70af05eab32cd3907f9387c64ce66d1366`, the [Falltergeist repository](https://github.com/falltergeist/falltergeist) reads the Item material with `stream.uint32()` in [`src/Format/Pro/File.cpp`](https://github.com/falltergeist/falltergeist/blob/32cbee70af05eab32cd3907f9387c64ce66d1366/src/Format/Pro/File.cpp) and declares `_materialId` as `uint32_t` in [`src/Format/Pro/File.h`](https://github.com/falltergeist/falltergeist/blob/32cbee70af05eab32cd3907f9387c64ce66d1366/src/Format/Pro/File.h). Its [`src/Format/Dat/Stream.h`](https://github.com/falltergeist/falltergeist/blob/32cbee70af05eab32cd3907f9387c64ce66d1366/src/Format/Dat/Stream.h) and [`src/Format/Dat/Stream.cpp`](https://github.com/falltergeist/falltergeist/blob/32cbee70af05eab32cd3907f9387c64ce66d1366/src/Format/Dat/Stream.cpp) define the stream's big-endian behavior.

This provides additional parser evidence for a four-byte big-endian interpretation. The parser's independence and underlying analysis methodology have not been established sufficiently for it to be definitive proof. Its unsigned type also differs from RE's signed `int`; format-level signedness therefore remains unresolved.

### Claim matrix

| Claim | Confidence | Evidence and basis | Unresolved evidence needed |
|---|---|---|---|
| Semantic field name | **Moderate** | RE names the Item member `material`, defines material categories, and uses them in impact-sound selection. | Independent, traceable format documentation or binary/game analysis confirming the meaning for identified retail records. |
| Offset and width | **Moderate** | RE's Item record read/write order and integer helpers support a four-byte field at source-derived offset `0x24`; Falltergeist reads a four-byte value in the corresponding Item sequence. | Byte-level confirmation from version-pinned retail records with independently established record identity and layout. |
| Endianness | **Moderate** | RE integer helpers perform byte-order conversion; Falltergeist's stream implementation specifies big-endian reads. | Independent retail-record bytes or authoritative documentation confirming the byte order for applicable releases. |
| Signedness | **Unresolved** | RE models the field as signed `int`; Falltergeist stores it as `uint32_t`. Existing small category values do not distinguish these interpretations. | Evidence of the original format's signedness and behavior for values with the high bit set, if such values are valid. |
| Value domain | **Moderate, limited** | RE's named material categories support the listed category interpretation. | Evidence establishing the exhaustive valid binary domain, treatment of unknown values, and whether the same domain applies across subtypes and releases. |
| Subtype applicability | **Moderate, limited** | RE has an Item path and a Scenery path; the Scenery structure labels `field_2C` as material. | Version-pinned record evidence covering the relevant Item and Scenery subtypes; no wider applicability is established. |
| Retail/version compatibility | **Unresolved** | The evidence is from implementations, not a release-by-release comparison with retail files. | Independently identified retail samples or authoritative documentation for each claimed release, including any version-specific layout differences. |

The Stage 81 RPU sample remains a repository sample observation and is not made retail-original by these implementation sources. This evidence does not establish complete subtype-specific record lengths or serializer correctness and retail/game round-trip behavior.

### Decision

Keep the production field registry empty and decoded `material` fields `ReadOnly`. Do not describe the candidate as fully validated or use this evidence alone to authorize a production field definition, editing, or serialization.
