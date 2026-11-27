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

## WPF diagnostic capture (development)

Install the extension with `Install.bat`, add `%APPDATA%\pyRevit\Extensions` to pyRevit extension paths if needed, and reload pyRevit. The installer refuses to overwrite an existing extension. It does not install or replace KStruct.dll or pyRevit; build and deploy the matching KStruct add-in separately. `Uninstall.bat` removes only the extension after confirmation. These scripts have not yet been validated in a Revit session.

In Revit, choose **WPF Capture > Enable capture**, open and close a column, beam, or footing editor, then choose **Show last capture**. Disable capture after use. The most recent snapshot is held only in memory and reset on enabling or restarting Revit. It reports DPI and window size in WPF units, visual/control/binding counts, validation errors, canvas/viewport child counts and aggregate click/text/selection event counts. It does not record entered values or write to the model. It does not verify geometry, event correctness, binding errors, inactive tabs, or preview/model correspondence; counts are not a substitute for a visual or model check. WPF binding error tracing is separately exercised by the automated beam and footing harnesses.
