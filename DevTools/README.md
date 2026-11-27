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
The installer copies the extension to pyRevit's default third-party extension directory for the current user. Revit/pyRevit runtime validation remains pending; do not treat this branch as a production release.

## WPF diagnostic capture (development)

Install the extension with `Install.bat` and reload pyRevit. On the locally installed pyRevit 7.1, `%APPDATA%\pyRevit\Extensions` is its default third-party extension directory; other versions or custom configurations should be checked in pyRevit settings. The installer refuses to overwrite an existing extension. It does not install or replace KStruct.dll or pyRevit; build and deploy the matching KStruct add-in separately. `Uninstall.bat` removes only the extension after confirmation. These scripts have not yet been validated in a Revit session.

In Revit, choose **WPF Capture > Enable capture**, open and close a column, beam, or footing editor, then choose **Show last capture**. Disable capture after use. The most recent snapshot is held only in memory and reset on enabling or restarting Revit. A window from an earlier capture session cannot overwrite a later session. Capture failures report only the exception type, not potentially sensitive error text. The snapshot reports DPI and window size in WPF units, visual/control/binding counts, active binding and validation errors, canvas/viewport child counts and aggregate click/text/selection event counts. It does not record entered values or write to the model. It does not verify geometry, event correctness, transient binding errors, inactive tabs, or preview/model correspondence; counts are not a substitute for a visual or model check. WPF binding error tracing is separately exercised by the automated beam and footing harnesses.

## Local pyRevit test launch

`Launch-Local-Test.bat` starts a **test session** of Revit 2024 with the current-user KStruct manifest and `DevTools/pyRevit-local`, if those have already been prepared on this computer. The local clone is machine-specific, excluded from Git locally, and is not shipped in the repository. The clone's .NET Framework `System.Management` binding was adjusted to avoid selecting an incompatible .NET library. The launcher creates a temporary journal folder and leaves the normal shared pyRevit attachment untouched. Revit journal mode can omit other third-party add-ins, so this is not a replacement for the user's normal Revit setup. Close the test session normally when finished. It does not remove the temporary folder automatically.

The local copy and the KStruct/pyRevit test startup were verified on this workstation; WPF Capture could be enabled. A full capture and comparison against a newly created model were not tested. A normal Revit startup still uses the shared pyRevit attachment and may encounter the original conflict until the shared installation is repaired.
