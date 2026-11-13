# Column V4 / Core V5 — KStruct-branded reference UI acceptance specification

Status: implementation specification; UI and runtime acceptance **not yet complete**.
Reference: four screenshots supplied in conversation on 2026-11-13. No BIMSpeed brand assets to be copied. Keep KStruct title, icon, navy/earth palette; reproduce functional layout and control placement inside the window.

## Window structure
- Four tabs, in order: Hình học & bố trí thép; Thép đai; Thép móng; Cài đặt chung.
- Persistent bottom command bar: Lưu cấu hình, Mở cấu hình, Chạy, Đóng.
- Support scrolling and DPI scaling without clipping; align section headings, label/input rows, checkbox groups, illustrations to the supplied screenshots.

## Tab 1 — Hình học & bố trí thép
- Left narrow column: Tất cả cột tree/list and longitudinal schematic with selected host dimensions and bar/tie annotation.
- Top center: Cx, Cy, main bar type, primary tie type, auxiliary tie type, total count, As and reinforcement ratio, apply settings to remaining floors.
- Top right: section/tie configuration selector, hooked/closed tie radio controls, remove all, dynamically updated section canvas.
- Bottom: beam/slab height, structural framing classification, top and other cover values.
- Bar types must be real project RebarBarType values; section dimensions and counts from selected hosts; ratio calculated from actual geometry, not static placeholders.

## Tab 2 — Thép đai
- Left: stop ties at beam/slab bottom or maximum beam/slab elevation; closed tie arrangement by spacing or number; first offset.
- Right: A1/A2 zones, three checked criteria (minimum length, clear column height / 4, section height), optional full lap zone, independent top zone, two overlapping U ties, hook type.
- Bottom left: auxiliary ties same spacing as primary or separate spacing; auxiliary hook type.
- Do not infer engineering compliance from a selected preset. Joint-tie suppression remains an explicit opt-in with warnings.

## Tab 3 — Thép móng
- Enable foundation starters; optionally split bars into two pieces; tie spacing, Hm and Lb, L-bar schematic.
- Four direction buttons and reverse direction; transform directions through column local axes including rotated/mirrored hosts.
- Validate cover, bend radius, actual foundation geometry and project shapes before Rebar creation.

## Tab 4 — Cài đặt chung
- Bend top bars by length or xD; top-floor bend vs continue starters.
- Assign elevation/partition; lap length and anchor length by xD; stagger joints; grouping by mark.
- Bend/cut criteria; lap position from column base or middle; 1/2 splice positions and spacing in xD.
- Beam/slab source: manually select, auto-detect framing, auto-detect floors, or explicit default height.
- All values are persisted, validated, and reflected in preview. Do not silently assume TCVN design adequacy.

## Integration and safety gates
1. Keep V4 command and proven writer operational until the Core V5 engine passes real Revit 2024 tests.
2. Add an explicit UI-to-domain configuration adapter; preserve backward-compatible V3/V4 saved settings.
3. V5 audit/preflight must be read-only, distinguish verified solid/cover from bounding-box heuristics, and block unsupported hosts.
4. Rebar writes use transaction rollback on failure, stable ownership and idempotent regeneration; never delete unrelated user bars.
5. Tests: parameter round-trip, per-floor override, Cx/Cy and As, hooked/closed and auxiliary ties, beam/surface detection, rotation/mirror, starters and anchors, staggered lap, duplicate prevention, cancellation/rollback.
6. Build Windows Revit 2024 DLL, inspect exact-head CI logs, test in a COPY of RVT, review screenshots at target DPI; only then request merge to main.

## Implementation checklist
- [ ] Inspect existing ColumnRebarV3Window XAML and code-behind and identify control bindings
- [ ] Implement all four reference tabs in WPF with KStruct branding
- [ ] Connect each active control to a typed config and V4/Core V5 adapter
- [ ] Add UI/geometry regression tests
- [ ] Pass exact-commit Windows build and run Revit 2024 model tests
- [ ] Review UI side-by-side against four reference screenshots
- [ ] Merge only after acceptance
