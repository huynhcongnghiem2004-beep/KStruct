# Beam editor UI verification

This harness compiles the **actual** [beam editor](../../NativeRebar/BeamRebarEditorWindow.cs)
with minimal Revit data fixtures, opens its modal WPF window, exercises controls,
and renders PNGs at 1648×928, 1280×760 and 1024×640 logical pixels.

```powershell
dotnet build tests/BeamRebarUi/BeamRebarUi.csproj --configuration Release
$process = Start-Process tests/BeamRebarUi/bin/Release/net48/BeamRebarUi.exe -ArgumentList beam-ui-previews -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { Get-Content beam-ui-previews/render-error.txt; exit $process.ExitCode }
```

Checks cover action visibility, accordion navigation, editing and undoing counts,
undoing presets and group deletion, zoom preservation during resize, and basic input validation.
GitHub Actions publishes the rendered images as **KStruct-Beam-UI**.

The fixtures are not an engineering engine. They do not create or validate Revit
elements; the anchorage calculation fixture throws if called. The complete add-in
is separately built against the Revit 2024 API. Final checks for hosting,
Shape/Hook compatibility, anchorage and model creation still require Revit.

The new layout retains the advanced editor for secondary layers, hooks and TCVN
settings. The undo history is local to the quick editor (maximum 100 states);
it does not undo model creation or previously saved defaults.
