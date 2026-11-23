# KStruct Revit template workflow

## Source template

`KStruct_Column_Template_MrKhoa.rte` was created from the opened Revit 2024 project:

`Mr.Khoa - pa2.rvt`

The template is stored at the repository root. It is a source-derived template and currently retains the source project's views, sheets, schedules and model content. Use a copy for new work and remove project-specific model elements before treating it as a clean office standard.

## Intended use

1. Start a new Revit 2024 project from the `.rte` file.
2. Keep the loaded Title Block, Section/Detail types, View Templates, Dimension Types, TextNoteTypes, Rebar Tags and Break Line family available to KStruct.
3. Open a graphical Structural Plan or 3D view.
4. Run `Tạo thép cột V4`, select the target columns, and open the deployment settings.
5. Select the template's deployment types and save the project-specific settings before creating the sheet.

## Deployment identity

KStruct stores the selected deployment types in a project-specific XML file. New settings record the Revit `ElementId` together with `UniqueId`, `FamilyName`, `TypeName`, `Category` and `ViewType`. When a project is created from this template, loading first tries the stable identity fields and only falls back to the legacy `ElementId` value. Old XML files remain readable.

The deployment dialog still requires the operator to save the choices once in the new project. Automatic selection of a named office profile is a separate follow-up; this prevents an unrelated project family with the same short type name from being selected silently.

## Expected deployment asset groups

- Title Block
- Cross-section and longitudinal Section/Detail View Family Types
- Cross-section and longitudinal View Templates
- Cross-section and longitudinal Linear Dimension Types
- Cross-section and longitudinal TextNoteTypes
- Main-bar and stirrup Rebar Tags
- Cross-section Multi-Rebar Annotation Type
- Longitudinal Break Line Detail family
- `BS_Rebar_Detail 2D` Detail Item family for the three-row reinforcement table
- Rebar bar, hook and shape types required by the column configuration

The source-of-truth implementation is [V3DeploymentControl.xaml.cs](../ColumnRebarPro/V3/V3DeploymentControl.xaml.cs) and the generated sheet layout is [V3DrawingLayout.cs](../ColumnRebarPro/V3/V3DrawingLayout.cs).

## Column drawing names

- Longitudinal view: `<Mark> MCD1`
- Cross-section view: `MC-<index>-<index>`
- Sheet name: `<Mark>` when `UseMarkForSheetName` is enabled, otherwise the configured `SheetName`
- Sheet number: the configured `SheetNumber`, incrementing a trailing numeric suffix for additional stacks
