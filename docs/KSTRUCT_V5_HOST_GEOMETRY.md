# KStruct V5 – Host Geometry Resolver (read-only preview)

## Purpose
Provide **local coordinate frames** for structural hosts before implementing BeamRebar, ColumnRebar and FoundationRebar V5. This iteration does **not** create, move, delete, update or tag reinforcement. Existing V4 commands remain unchanged.

## New code
- `Core/V5/HostCoordinateFrame.cs` – pure vector and orthonormal-coordinate-frame operations. Handles straight and sloped location lines, transform-based instances, world/local projections, validation and serialization, with no Revit API dependency.
- `Core/V5/RevitHostGeometryResolver.cs` – Revit 2024 adapter. For straight framing, canonicalize the `LocationCurve` endpoints for diagnostics and project the Family `BasisY` onto its normal plane as a transverse reference; the resulting frame reflects a reference to section rotation but does not certify the concrete solid. An axis parallel to the beam is rejected. For columns/foundations, use the FamilyInstance transform and validate handedness, orthogonality and scale.
- `NativeDocumentation/AuditModelV2Command.cs` – for every eligible host, export `hostLocalFrame`. For each Rebar whose host frame can be resolved, report `fromHostLocalMm` and `toHostLocalMm` on each exported centerline segment.

## Interpretation
- Coordinate values are in **millimeters**; axes are unitless vectors.
- For a beam, X is its straight location-axis direction; Y/Z are construction coordinate axes. They **do not certify beam physical top, bottom, side, cover** or location-line alignment relative to concrete.
- For a column/foundation, the Family instance basis is used; this does not certify which Solid is concrete, its faces or its real cross-section.
- Scaled, skewed, curved, unhosted and degenerate instances are **not silently assigned a frame**: `validatedOrthonormal=false` and `reason` are emitted.
- Candidate Solid world bounds from Audit V2 remain diagnostics, not automatic pile-cap classification.
- Local bar curve coordinates alone do not validate hooks, end cover, support joints or lap splice.
- Valid mirrored Family transforms retain signed left-handed coordinates and `handedness=mirrored`; this is still a diagnostic frame, not approval for Rebar placement.
- All frames have `placementApproved=false`. Projecting actual rebar onto the frame never authorizes creating or updating steel.
- `planarFaceSamples` (max 24 planar faces per candidate Solid) reports area, normal and orientation `±X/Y/Z-face-candidate` or `oblique-candidate`. These faces have `classification=unverified-planar-face`. No cover offset, top/bottom or pile-cap classification is inferred.

## Revit 2024 smoke-test acceptance
1. Open a **copy** of Mr.Khoa RVT in Revit 2024.
2. Run Audit V2 and save JSON + CSV to a new filename.
3. Confirm JSON and CSV record counts match and 528 Rebar are represented if unchanged from the earlier model.
4. Find a horizontal beam, a rotated column and a foundation; verify `hostLocalFrame` values against Revit's location curve / transform.
5. For at least 5 sample Rebar elements, compare world centerline and local projection with the host's actual position and orientation.
6. Check warnings separately; `validatedOrthonormal=false` is an unsupported geometry case, **not necessarily a model error**.
7. Confirm the project modified flag and all original Rebar are unaffected. Do not merge to main until approved.

## Subsequent work
- Host solid-face resolver for cover offsets and slab/beam intersecting support geometry.
- Mirrored instance policy with explicit signed frames and handedness metadata.
- Curved beam tangent frames along stations, not a single global basis.
- Multi-level column stack and joint segmentation.
- Foundation cap-vs-pile geometry and real top/bottom concrete faces.
- Real runtime acceptance on several models before enabling Rebar creation/update V5.

## Known blockers before creating steel
1. Validate physical cross-section against real concrete solids, not just the Family section axis.
2. Determine physical support start/end (canonical lexicographic line direction is diagnostic only).
3. Resolve actual concrete faces and cover offsets, pile-versus-cap identity and nested geometry transforms.
4. Resolve column stacks, structural joints, host ownership, hooks and lap lengths.
5. Pass real-model sample checks and Revit API transaction rollback tests before activating any V5 write action.

## Read-only Structural Joint Candidate Resolver
- New `Core/V5/StructuralJointCandidatePolicy.cs`: finite/degenerate-safe 3D line-vs-AABB segment clipping with normalized [0..1] entry/exit fractions and optional explicitly provided padding.
- New column-stack candidate check: vertical interface separation within 20 mm, XY projections touch/overlap within 10 mm; returns *actual* XY overlap area in mm².
- Audit V2 pre-indexes structural columns and foundations by world bounding boxes, then emits per-beam `jointCandidates` with `otherHostId`, `otherHostCategory`, `entryFraction`, `exitFraction`. For columns, emits possible adjacent columns with relative direction and XY overlap area.
- At most 100 candidates per host are exported; `jointCandidateLimitReached` signals traversal truncation. All records use classifications `unverified-bbox-joint-candidate` / `unverified-column-stack-candidate`.
- **Critical limitations:** Bounding-box intersection does not prove concrete solids intersect or transfer load. Nested geometry, offset axes, rotated column cross sections, beams near columns, linked structures and physical load paths require stronger checks. Column extension/overlap is not a substitute for splice or confinement calculations. No candidate is approved for Rebar creation.
- The new diagnostic output is not yet verified on a new Mr.Khoa RVT export. Validate candidate pairs against Revit visual inspection before using these results downstream.
