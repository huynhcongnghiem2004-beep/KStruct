# KStruct V5 - Phase 1: Model Audit V2 & Core Policies

## Scope and version separation
- V4 remains unchanged and active. The existing `KStruct_AuditModel` V1 remains available.
- New Ribbon item: **KStruct > KStruct Hồ sơ > Audit V2 Rebar & Geometry** (`KStruct_AuditModelV2`).
- V2 is read-only (`TransactionMode.ReadOnly`), and uses no Revit model Transaction.
- `Core/V5/AuditV2Policy.cs` and `AuditOwnershipPolicy.cs` have no Revit API dependencies so they can be unit-tested without Revit.
- This stage does **not** modify beam, column or foundation Rebar creators, and does not implement V5 Rebar creation.

## Audit data
- JSON schema: `kstruct.model-audit.v2`; CSV lists matching successful records.
- Structural foundation/column/framing/floor/wall, Rebar, Detail Components, Views, Viewports, CAD links/imports and Revit links.
- Each record: ElementId/UniqueId, HostId when available, instance and type parameters, dimensions, LocationPoint and BoundingBox (global values in mm).
- Rebar: real bar type diameter, ShapeId, start/end HookType ids, physical positions, first existing bar position's curve segments, length and direction *candidates*, shape-driven layout rule/array length if available.
- Detail/Section Views: origin and basis axes, CropBox transform, scale, template and known KStruct footing-drawing metadata.
- Foundation instances: visible solid volume candidates including nested geometry. These are for diagnosis **only**; nested piles may be present and are not automatically identified as concrete cap.
- Errors and unsupported API access are explicit warning entries. An unknown result is not silently classified as a valid result.

## Safety and privacy
Run on a **copy** of the RVT. Save exports to a local folder you control. Output may include private document paths, element names and Family details; review before sharing.

Revit project is not modified, but the command writes/overwrites JSON/CSV on disk after confirmation. It writes temporary files before promoting completed outputs. If overwriting an existing pair, the promotion is not fully atomic across both files; keep copies of important reports.

## Phase 1 acceptance gate
1. Repository tests for pure JSON, unit conversion, finite geometry, bar orientation candidates and Ownership classification pass.
2. Revit 2024 compile and Ribbon registration (94 commands, 7 panels) pass.
3. In Revit 2024 open a copy of `Mr.Khoa - pa2.rvt`; export Audit V2 twice and verify no model edits.
4. Compare JSON/CSV row counts and inspect several known Rebar HostId, bar diameter, bar positions, Shape/Hook and centerline direction against Properties/Rebar Editor.
5. Review Detail-X/Y View axes, CropBox and 2D Family Type/Instance parameters. Do not certify cover or graphic offsets from bounding boxes alone.
6. Confirm unloaded CAD/Revit links and missing geometry yield warnings rather than a crash.
7. Phase 1 is **not complete at runtime** until real Revit smoke tests (steps 3-6) are signed off.

## Known limitations and backlog
- Some Rebar can be free-form, varying, multiplanar or have several bar positions with distinct centerlines. V2 records *only the first existing bar position's* centerline; do not use it for complete fabrication quantities.
- Rebar direction is a segment-level X/Y/Z heuristic, not a beam/column local-axis classifier. TOP/BOT must be determined using Host geometry and cover rules in subsequent V5 Core releases.
- Nested solids may count graphics that do not belong to concrete cap. The correct physical cap solid requires Family-specific relationship/geometry validation.
- ShapeDriven layout may be unavailable for freeform bars; warnings are expected.
- Known KStruct footing View ownership metadata is read where available. Other module ownership protocols need separate read-only adapters, not name matching.
- Avoid relying on Type names as proof of a pile's actual diameter or reinforcement capacity.
