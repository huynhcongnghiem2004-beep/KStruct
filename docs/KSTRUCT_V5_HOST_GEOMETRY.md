# KStruct V5 – Host Geometry Resolver (read-only preview)

## Purpose
Provide **local coordinate frames** for structural hosts before implementing BeamRebar, ColumnRebar and FoundationRebar V5. This iteration does **not** create, move, delete, update or tag reinforcement. Existing V4 commands remain unchanged.

## New code
- `Core/V5/HostCoordinateFrame.cs` – pure vector and orthonormal-coordinate-frame operations. Handles straight and sloped location lines, transform-based instances, world/local projections, validation and serialization, with no Revit API dependency.
- `Core/V5/RevitHostGeometryResolver.cs` – Revit 2024 adapter. For straight framing, use a `LocationCurve` of type `Line` as local X; derive Y from world-up, Z via right-handed cross product. For columns/foundations, use the FamilyInstance transform and validate handedness, orthogonality and scale.
- `NativeDocumentation/AuditModelV2Command.cs` – for every eligible host, export `hostLocalFrame`. For each Rebar whose host frame can be resolved, report `fromHostLocalMm` and `toHostLocalMm` on each exported centerline segment.

## Interpretation
- Coordinate values are in **millimeters**; axes are unitless vectors.
- For a beam, X is its straight location-axis direction; Y/Z are construction coordinate axes. They **do not certify beam physical top, bottom, side, cover** or location-line alignment relative to concrete.
- For a column/foundation, the Family instance basis is used; this does not certify which Solid is concrete, its faces or its real cross-section.
- **Mirrored**, scaled, skewed, curved, unhosted and degenerate instances are **not silently assigned a frame**: `validatedOrthonormal=false` and `reason` are emitted.
- Candidate Solid world bounds from Audit V2 remain diagnostics, not automatic pile-cap classification.
- Local bar curve coordinates alone do not validate hooks, end cover, support joints or lap splice.

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
