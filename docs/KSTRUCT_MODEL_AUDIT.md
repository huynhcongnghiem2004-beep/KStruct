# KStruct - Kiểm tra mô hình (Read-only RVT Model Audit)

Command: KStruct > KStruct Hồ sơ > Kiểm tra mô hình.

## Purpose
Export data from an opened Revit project to JSON and CSV without modifying the model. Supports analysis of foundation pile symbols, reinforcement, structural elements, Family Detail Items, Views, Sheets and external links when developing KStruct.

## How to run
1. Open a COPY of the RVT in Revit 2024. Revit 2023 files require a one-way upgrade, so do not upgrade the original.
2. Run KStruct > KStruct Hồ sơ > Kiểm tra mô hình.
3. Choose a JSON file path. A CSV file with the same base name is written alongside it.
4. Share the resulting two files with the developer after reviewing them for private project information.

## Output
- Structural foundation, column, framing, floor, wall, stair instances.
- Rebar records: host ID, type, diameter, shape ID and number of positions (not necessarily physical bar count).
- Detail Component instances, non-template Views, Viewports, RevitLinkInstance and CAD ImportInstance.
- ElementId, UniqueId, Family, Type, Level, world BoundingBox in mm (not actual solid geometry), notes.
- JSON schema kstruct.model-audit.v1; UTF-8; CSV includes BOM for Excel.
- If individual elements are inaccessible, audit records warnings and a failed record count.

## Safety and limitations
TransactionMode.ReadOnly prevents model modifications; no model transaction is started. The command only writes files to the selected output folder. Project paths, Family names and link names may be sensitive; review before sharing. BoundingBox does not prove cut-plane intersection. Family geometry and formula constraints require checking the actual RFA using Revit API / Family Editor.

## Revit runtime acceptance
Verify JSON parses, CSV row count equals summary.total, Rebar hosts and diameters agree with Revit, link status agrees with Manage Links, and canceling the file dialog makes no changes. Repeat on a model copy with Vietnamese paths, link warnings and multiple views. CI compile does not substitute for in-Revit integration.