# KStruct V5 — Structural execution safety gate (2026-11-12)

## Verified scope in this change
- Added read-only `Core/V5/V5HostSolidInspector.cs`, called by the V5 WPF Workspace for one selected Host.
- Walks visible geometry and nested Family geometry with bounded recursion and Solid count.
- Counts positive-volume Solid candidates, planar/nonplanar faces, unknown/mixed material Solid candidates, and faces without a readable material.
- Marks a Solid as **concrete-material candidate** only when all inspected faces have structural assets classified `StructuralAssetClass.Concrete`. This does not certify load-bearing concrete host geometry; nested piles, voids and duplicates are not yet resolved.
- Added `Core/V5/V5ExecutionGatePolicy.cs`: six independent fail-closed blockers shown in Workspace. Added pure regression checks.
- The current Workspace still does not execute V5 Rebar transactions: `V5InputReview.CanCreateRebar` remains hardcoded `false`.
- Existing V4 and `main` are untouched.

## Open implementation items — DO NOT claim complete
1. Concrete solid ownership: distinguish host concrete from nested pile, rebars, void geometry, duplicate Family solids and in-place geometry; compare with actual Family in multiple RVTs. Determine usable outer surfaces.
2. Cover: verify host Rebar Cover parameters and real physical faces; classify beam TOP/BOTTOM/SIDES, column perimeter and cap layers with correct units, offsets and tolerances.
3. Joints: replace diagnostic AABB candidates with Solid intersections, verified supports and host sequencing. Resolve column stacks, beam supports, confinement zones; design approval required for exceptions.
4. Anchors and lap splices: implement real bar bend radius, shape/hook compatibility, overlapping reinforcement and approval policy from project design documents, never infer from diagnostic geometry.
5. Write transactions: introduce revision-safe Rebar ownership identifiers, create/update/delete with atomic TransactionGroup rollback; test repeated update, cancellation, failure injection and nonowned Rebar preservation.
6. Drawings: automated view, tag, dimension, detail, viewport and sheet generation with collision checks and rollback, checked in real Revit 2024 models.

## Acceptance checklist
- [ ] Exact-head GitHub Actions Windows Revit 2024 compile and CoreV5 tests PASS.
- [ ] Open a copy of a real RVT with one standard beam, column, footing, mirrored instance and nested cap/pile family.
- [ ] Confirm reported positive-volume Solid and material classifications against Revit Properties and material structural asset.
- [ ] Confirm unsupported/material-less/curved/mirrored Families do not imply successful engineering verification.
- [ ] Confirm no Rebar element is created/modified and V4 remains callable.
- [ ] No production activation until all six gates are verified by dedicated Revit runtime tests, not manual UI flags.

## Engineering caveats
Material classification is only an evidence candidate; it is not a concrete-cover determination. Rebar layouts, anchors, lap lengths, and detailing cannot be certified by compilation. Review by a qualified structural designer is required for construction documents.
