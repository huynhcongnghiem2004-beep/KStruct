# KS-COL-SD-SRS-001 v1.0 — Column Rebar V4 Shop Drawing

Status: **functional baseline / NOT accepted for construction**. Target: Revit 2024, existing **Thép cột** Ribbon Panel. Do not create another Panel.

## Eight delivery stages

| Stage | Scope | Evidence required |
|---|---|---|
| 1 | Host/level/geometry/cover/project rebar types preflight | Deterministic rejection of missing inputs |
| 2 | Verify longitudinal bars, ties, joints, splices, couplers, clearances | Approved design checks + model interrogation |
| 3 | Longitudinal, cross-section and 3D views | Actual Revit views verified at intended elevations |
| 4 | Partition, numbering, tags, annotation, dimensions | References point to real model Rebar elements |
| 5 | Shape/bending details, schedule and reconciled quantities | Element-by-element schedule reconciliation |
| 6 | Owned sheet, title block, viewports and layout | Re-run without clobbering other-user sheets |
| 7 | Change detection and controlled update | No orphaned/duplicated elements |
| 8 | Publication validation, PDF/DWG and revision control | Visual/technical sign-off of exported sheets |

## 13 acceptance cases (all required)

- TC-01 Rectangular column: geometry, tags and quantities match model.
- TC-02 Round column: circular ties and distributed longitudinal bars.
- TC-03 Changing column cross-section: proper transition detailing.
- TC-04 Changes of bar quantity/diameter between stories: reviewed anchorage/splices.
- TC-05 Three tie regions: actual counted positions and spacings.
- TC-06 Multiple beams/slabs at column joints: no silent omission or duplication.
- TC-07 Couplers: model, placement, tags and schedule.
- TC-08 Equal bar geometry: stable numbering/partition and placement IDs.
- TC-09 Story height change: views, dimensions, schedules update.
- TC-10 Column cross-section/cover change: clearances and constraints revalidated.
- TC-11 Deleted/replaced Rebar: stale references detected.
- TC-12 Repeated drawing generation: no duplicated/foreign views, sheets or schedules.
- TC-13 Export PDF/DWG: scale, font, annotations and crop visually verified.

**Pass rule:** all 13 cases verified on actual Revit 2024 project models; passing CI, unit tests, or DLL build alone is insufficient.

## Repository review (2026-11-11)

- `App.cs` already registers the V4 column command and create/update drawing commands; preserve these.
- `V3DrawingLayout.cs` creates longitudinal/one cross-section per story and places on a sheet; not proof of separate base/mid/node views or a 3D view.
- `V3DrawingBuilder.Validate` deliberately blocks automatic splice / transition drawings. Do not remove until approved geometry detailing exists.
- `V3RebarSetCreator.CreateTies` uses a geometric intersection exclusion that may omit ties at beam/slab joints. This is **not** a reinforcement design rule. All exclusions require structural detailing review.
- This branch adds validation against invalid exclusion ranges and silently removing **every** tie. It does **not** resolve compliant joint confinement/tie detailing.
- Existing CI `.github/workflows/revit2024-build.yml` runs `tests/ColumnRebarLayout` and compiles the add-in using pinned Revit references. Revit runtime/shop-sheet inspection remains separate.

## Acceptance tracking

| Case | Static-code evidence | Revit 2024 runtime acceptance |
|---|---|---|
| TC-01 | Partial existing implementations | NOT RUN |
| TC-02 | Separate round-column command; not verified with V4 sheets | NOT RUN |
| TC-03 | Drawing validation rejects transitions | BLOCKED |
| TC-04 | Drawing validation rejects splices/stacked transitions | BLOCKED |
| TC-05 | Tie-planning tests and newly added invalid-exclusion tests | NOT RUN |
| TC-06 | Added total-tie-removal guard, joint behavior remains unapproved | BLOCKED |
| TC-07 | No end-to-end evidence | NOT RUN |
| TC-08 | Partition logic present, full schedule reconciliation unverified | NOT RUN |
| TC-09 | No change-propagation evidence | NOT RUN |
| TC-10 | No clearance revalidation evidence | NOT RUN |
| TC-11 | Ownership/update guards present, end-to-end unverified | NOT RUN |
| TC-12 | Sheet ownership logic present, runtime re-run unverified | NOT RUN |
| TC-13 | Build/export is not PDF/DWG visual acceptance | NOT RUN |

## Release gate

Do not merge as a production-complete Shop Drawing feature or label any TC PASS without test evidence. Keep this PR as a guarded incremental fix pending Revit model-based validation and design decisions for joint ties, transitions and connections.

## Implementation update — 2026-11-11 (PR #53)

User-approved decisions:
- `Đai nút khung` option: checked retains tie sets at beam/slab intersections; unchecked omits intersecting positions. Default checked. Neither choice constitutes structural approval.
- Keep current sheet layout handling: overflow/overlap remains DRAFT with a warning in sheet name, not a transaction failure.
- Open verified `BottleneckToUpper + ConnectionPlan` paths; unsupported splice/stagger and missing plans remain blocked.
- Mandatory configured main/tie tags and cross dimensions must not fail silently.
- Regeneration rejects sheets with manually added sheet-level notes/annotations.
- Generate BASE/MID/NODE cross-sections per storey and one native 3D rebar view on the sheet; longitudinal reference numbers match MID views.

**Unfinished acceptance gates**
- The combined column-line bending schedule is still not created/placed automatically. Existing generic Rebar Schedule command is not scoped to the selected stack. Never place an unfiltered project-wide schedule on a column-specific sheet.
- Three-level cut views, 3D visibility, multipart reinforcement connections, sheet annotation preservation and dimension references require an actual Revit 2024 project run.
- Default joint-tie geometry is a presentation/detailing choice only; node reinforcement requires structural engineer approval.
- Revit runtime TC-01..TC-13 status remains NOT ACCEPTED until instrumented RVT tests are recorded.
