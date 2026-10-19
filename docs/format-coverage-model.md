# Format coverage data model

`FormatCoverageCatalog` in Core is the single source of coverage assessments for DAT1, DAT2, FRM, MAP, MSG, INT, PRO, LST, and PAL. Its `AuditBaselineCommit` pins the source snapshot used for the assessments. This catalog is curated data, not an automatic source-code auditor.

Each format has at most one capability per category. A capability records its category weight, implementation status, rationale, confidence, and repository evidence references. Evidence identifies its source (production code, test, or documentation) and whether the relevant verification used real game data, synthetic data, or remains unverified.

The calculator uses the fixed weights: Parsing 20, Structure 20, Preview 15, Editing 15, Save/Export 15, Validation/Integrity 10, and Tests 5. `Implemented` earns 1.0, `Partial` 0.5, and `Missing` 0. `NotApplicable` contributes neither earned nor applicable points. The percentage is the earned points divided by applicable points, multiplied by 100, then rounded once to one decimal place. A format with no applicable categories has no percentage.

Confidence and evidence verification are displayed independently and do not affect the score. A future dashboard should read the catalog and calculated results rather than maintain a second set of percentages.
