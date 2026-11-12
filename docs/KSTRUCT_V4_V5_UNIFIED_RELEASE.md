# KStruct V4 + V5 unified add-in (Revit 2024)

## Merge strategy

The production add-in is **one** `KStruct.dll` with **one** KStruct Ribbon tab. The existing V4 commands remain the authoritative write/create/update/drawing commands. The V5 Audit, Core geometry diagnostics and WPF Structural Workspace are additive and read-only in the unified production release.

- V4: create and update column/beam/footing rebar using its current commands; create drawings, tags, dimensions and sheets using their respective commands.
- V5: Audit V2, host geometry and material candidates, read per-host Rebar Cover Type, validate input, show a schematic preview, inspect blockers.
- V5 does **not** transfer the Workspace dimensions and bar parameters to V4. Existing V4 dialogs and settings remain authoritative.
- V5 experimental `V5ColumnPilotWriter` remains source-available but its UI is disabled by `V5PilotReleaseGate.Enabled=false`, and a second command-side check prevents execution.
- This is **not** a new certified fully automatic structural detailing engine. Existing V4 feature limitations still apply.

## Revit UI entry points

1. Ribbon **KStruct → Thép cột → Tạo thép cột V4** for current column reinforcement.
2. **KStruct → KStruct Dầm → Thép dầm** for current beam reinforcement.
3. **KStruct → Thép móng → Thép móng cọc & đơn** for existing foundation workflows.
4. **KStruct → KStruct Hồ sơ → Audit V2 Rebar & Geometry** for read-only model audit.
5. **KStruct → KStruct Hồ sơ → V5 Workspace Nhập liệu kết cấu** to select host and inspect geometry, cover and safe input.

## Compatibility and acceptance

- Confirm 95 commands in 7 panels. Keep all legacy V4 IDs and handlers unchanged.
- Build `KStruct.csproj` Release x64 against pinned Revit 2024 API references, run CoreV5, WPF and Ribbon tests, upload `KStruct-Revit2024`.
- Verify the actual integrated DLL in Revit 2024 using a **copy** of a representative RVT: startup Ribbon, all V4 dialog entry points, V4 create/update/delete ownership and drawing/tag/dimension workflow, V5 Audit V2 and Workspace with no selected Host, beam, column, footing and selection cancellation.
- Run regression tests for changes to nested or mirrored Families; the V5 diagnostics do not grant permission for automatic Rebar writes.
- The unified release is only technically merged/build-verified until these live Revit acceptance steps have been performed. Structural design and detailing still require qualified professional review.

## Rollback

Before upgrading, retain a copy of the previously functioning DLL and any `.addin` manifest. Revit must be closed when replacing an installed DLL. On failure restore the previous DLL, restart Revit, and report full stack trace or reproduction steps. Never overwrite or delete existing Rebar from a V5 diagnostic workflow.
