# KStruct V5 – Structural Workspace (Revit 2024)

## Scope of this delivery
The new Ribbon command `KStruct_V5Workspace` opens a modern, neutral/navy WPF input workspace for **Beam, Column, Foundation**. It is the first independent V5 front end and does **not** alter legacy V4 commands.

### Workflow
1. In a **copy** of a Revit 2024 RVT, select one structural framing/column/foundation FamilyInstance.
2. Click **KStruct > KStruct Hồ sơ > V5 Workspace – Nhập liệu kết cấu**.
3. Choose Beam, Column or Foundation. Check document name and selected Host ID at top.
4. Enter preliminary width, height, cover, bar diameter, tie diameter/spacing and number of main bars, in mm.
5. UI reacts immediately and shows entry errors, basic available geometry and a schematic section. Values are **manual inputs**, not read or certified from Host solid geometry.
6. The workspace reads actual RebarBarType diameter values from the open project and warns about any unavailable bar diameter. It never adds RebarBarTypes.
7. **No Rebar creation, update, deletion or model mutation is permitted.** The Workspace is diagnostic and currently has no write button.

### Validation gates
- Requires a supported selected structural Host; rejects missing project RebarBarType, nonfinite or out-of-range numbers, ambiguous thousands separators, impossible 2D interior bar center dimensions, and unknown module IDs.
- Accepts Vietnamese comma decimals except ambiguous grouped values like `1,500`.
- Preview is schematic only. Cover and type sizes must later be derived from validated real concrete Solid geometry and structural design.
- `V5InputReview.CanCreateRebar` is hardcoded false. It is intentionally not a release-ready Rebar Factory.

### Integration policy
- One `KStruct.dll`, with **95 registered Ribbon commands across 7 panels**.
- V4 beam, column, foundation and documentation commands remain unchanged.
- V5 workspace and Audit V2 remain additive; do not modify ownership of legacy Rebar.
- PR #56 remains Draft until real Revit 2024 smoke testing, code review and end-to-end structural Rebar tests.

### Production roadmap – not yet implemented
1. Revit Host selection wizard, section/solid face verification and physical end supports
2. Rebar catalog, concrete cover, constraints and shape/hook selection
3. Beam, column and foundation Rebar Factory with manual preview, transaction rollback and clear ownership
4. Editable rebar groups, updates, automatic checking and 2D documentation
5. UI usability validation on sample architectural/structural RVTs including Mr.Khoa

Do not infer TCVN approval or structural calculation from the current input checks.
