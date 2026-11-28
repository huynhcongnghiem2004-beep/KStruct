# KStruct Column Drawing Studio V1.0

Branch: `feature/column-drawing-ui-v1`. Target: Revit 2024 / .NET Framework 4.8 / WPF.

## Scope

- New WPF window with longitudinal and cross-section schematic previews, settings and project annotation pickers.
- Reuse existing `V3DeploymentControl` to load available Revit Dimension Types, Rebar Tags, Section Types, View Templates, break lines and Title Blocks.
- Reuse `IndependentColumnDrawingService.ExecuteV3` for selected column-stack validation, transactional drawing generation, and safe sheet updates.
- Preserve existing `CreateColumnDrawingsCommand` and `UpdateColumnDrawingsCommand`; expose separate Ribbon entry for the Studio.
- The preview is **illustrative only**, with example values such as 4Ø18. It is not derived from selected model geometry; it cannot be relied on for fabrication.

## Run

1. Build the existing KStruct solution on a Windows environment with Revit 2024 API installed.
2. Install KStruct add-in as usual and open a project with structural columns reinforced by the KStruct V3/V4 workflow.
3. Ribbon KStruct > Thép cột > `Column Drawing Studio`.
4. Choose actual View Family Types, templates, tags, dimensions and Title Block loaded from the project.
5. Click `Tạo bộ bản vẽ`, then select columns if no structural columns were preselected.
6. Review the generated draft sheets in Revit, verifying bar geometry, lengths, annotations and layout before any issue.

## Known limitations

- Studio V1 adds a configurable interface; the actual Section/Tag/Sheet generation is still done by the existing KStruct engine.
- The available drawing feature set and actual output depend on existing `V3DrawingBuilder`, settings and project families.
- No independent implementation of bending-detail drawing, language choice, per-row checkboxes or live Revit geometry preview in this version.
- Revit build and end-to-end execution must be verified on a Windows/Revit 2024 installation before release.
- The original application workflows remain untouched.
