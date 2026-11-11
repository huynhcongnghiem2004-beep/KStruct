# Footing plan Detail Item dimension mapping (Revit 2024)

The new TOP/BOT plan mode creates one owned Detail Item instance per plan, hides model Rebar **only in the generated plan**, and leaves Rebar geometry unchanged. The original Native Rebar mode remains available.

## Family and mapping
Choose loaded TOP and BOT Detail Item types from the current project. The sample family is `BS_FM_Detail Rebar 2D Top & Bot` with types `2D-Top` / `2D-Bot`.

The family must be a **view-based, point-placeable** Detail Item. The add-in does not load or edit the RFA and never edits shared Type parameters. Before creating a plan, confirm the mapping against Family Editor reference planes, formulas and dimension labels; checking only the parameter names is not sufficient.

Mapping inputs for eight instance parameters: `L`, `W`, `L1`, `L2`, `X1`, `X2`, `Y1`, `Y2`. Valid formulas:

- `B`: footing local-X solid extents in mm.
- `H`: footing local-Y solid extents in mm.
- `C`: explicitly entered cover (mm).
- `B-2*C`, `H-2*C`, `B-C`, `H-C`.
- A nonnegative numeric literal in mm, using `.` decimal separator.
- `KEEP` or blank: leave that instance parameter unchanged.

The initial UI suggestions are `L=B`, `W=H`; all other dimensions `KEEP`. **These are not verified family geometry formulas.** Check and change the mapping as needed. The user must explicitly tick the confirmation checkbox. An invalid/read-only/non-double/duplicate named parameter fails the current footing subtransaction; the add-in does not silently fall back to an arbitrary size. The command regenerates and verifies every written value to 0.5 mm. Formula-driven or locked parameters should remain `KEEP` unless they can be safely and reliably assigned.

The mapping is recorded in the drawing profile and is re-applied on update. No existing profile is automatically enabled; old project drawings continue with their original mode.

## Acceptance in Revit
1. Load the provided RFA into a scratch project; verify its instance and type parameters in the **Family Editor**.
2. Create TOP/BOT on a rectangular footing and compare dimensions to the intended graphic geometry, not only the parameter values.
3. Repeat on another size, including non-square, rotated and mirrored footings.
4. Edit footing size; run Update; inspect instance parameter values and printed sheet geometry.
5. Verify independent instances do not modify another footing or the Family Type.
6. Test missing family/type, `KEEP`, negative expressions, locked formulas, and rollback.
7. Test all views and existing manual annotations before release.

**Important remaining limitations:** this mapping does not generate 2D rebar text notes and does not fix the separate update rotation or UpdateCrop/Rebar visibility issues. Build success alone is not acceptance in Revit.
