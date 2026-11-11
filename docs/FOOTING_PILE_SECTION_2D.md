# KStruct – Cọc 2D in footing Detail-X / Detail-Y

## Goal
Add one `BS_FM_Detail Cọc 2D` view-based Detail Item **at each actual pile intersection with the detail cut plane**, preserving model piles. Do not infer pile layout from equal spacing or from a screenshot.

## Workflow
1. Load the requested `BS_FM_Detail Cọc 2D` family and type into the project (the screenshot shows the type parameters `Cut Coc`, `D Coc`, `Dai Coc`, `Dai Thep`, `Goc`, `x`).
2. In footing drawing settings, enable **Đặt Detail Component Cọc 2D** and choose the matching point-placeable `Detail Item` Type.
3. KStruct creates Detail-X / Detail-Y normally, then enumerates actual Structural Foundation pile FamilyInstances in the project.
4. It uses each pile's **solid mesh vertices** to find the real horizontal center, top elevation and extent across the **ViewSection cut plane**. A pile is included only when its center lies within the footing footprint, its solid reaches the bottom of the footing, and its geometry intersects the plane.
5. The insertion point is the projected *physical pile head center*, without any manual X/Y offsets. The supplied Family's view insertion origin must be aligned with its graphical centerline. Revit runtime verification of the RFA reference planes is required to certify the displayed centerline.
6. Each inserted instance is marked with KStruct ownership plus source pile UniqueId. Update moves the symbol if the pile moved and removes only KStruct-owned instances whose piles no longer intersect the cut. User-created annotations remain.
7. The selected original Type remains unchanged. KStruct reads each modeled pile's explicit length-typed diameter parameter, duplicates or reuses a size-specific Family Type as necessary, writes Type Parameter `D Coc` on the duplicate and validates its final value against the physical diameter (within 0.1 mm). If diameter is missing, non-Length, or the Family formula prevents assignment, the footing update fails safely instead of inventing a size. The custom 2D symbol's graphical origin/reference planes still require verification in Revit; manual offsets are deliberately disabled.

## Non-destructive behavior and constraints
- Existing sessions and profiles are off by default; this feature activates only for pile-cap views when configured.
- Only view-based point-placeable `OST_DetailComponents` FamilySymbols are accepted; other placement types report a validation error.
- Only Structural Foundation piles whose Family/Type contains `cọc`, `coc` or `pile` are considered. A custom pile with another naming scheme requires expanding the pile-identification rule.
- Missing solid geometry, missing symbol or no intersecting pile is never silently replaced by an invented location. No matching pile produces a warning.
- The source model pile is not hidden or changed; the Detail Items are 2D overlays in the two detail views.
- To validate: place two pile centers on cut X and three on cut Y, rotate the cap, offset cut planes, and move/delete one real pile. Confirm counts, head elevation, center alignment and Update behavior in Revit 2024. Also verify note/Detail Item overlap and Type parameters shown in screenshot. Check two real piles of different diameters on the same detail: each 2D instance must retain the correct D Coc without modifying the shared original Type.
- CI compiles the code against Revit 2024 references. It cannot open Revit or inspect the actual RFA's insertion origin, nested family geometry or formulas, so production drawing approval requires a Revit smoke test.
