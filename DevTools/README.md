# KStruct DevTools V1.0

Target: Revit 2024, pyRevit IronPython 2.7, KStruct .NET Framework 4.8.

Status: development scaffold, not released.

## Scope
- Read-only host geometry, local coordinates, column and beam rebar, foundation anchor geometry, sampled cover, duplicate candidates, parameters, view/sheet and JSON diagnostics.
- WPF diagnostics: window/DPI, controls, bindings, events, validation, preview geometry, preview/model comparison, performance, configuration and JSON.
- Modal WPF instrumentation requires explicit C# integration; pyRevit cannot reliably inspect a modal dialog from another ribbon command.

## Acceptance gates
- Preserve existing commands and model data; no automatic merge.
- Test Python syntax and pure geometry helpers in CI; verify Revit API calls in Revit 2024 on a disposable model.
- Test rotated/mirrored hosts, excluded rebar positions, curved bars, reruns and sheet overlap.
- Test WPF at 100/125/150% scaling, invalid inputs, cancel, preview and creation consistency.
- Geometry diagnostics are not structural design certification. Unknown/unsupported cases must be reported explicitly.
- Export only user-selected diagnostic data locally; review before sharing externally.

## Installation
Pending implementation and runtime validation. Do not treat this branch as a production release.
