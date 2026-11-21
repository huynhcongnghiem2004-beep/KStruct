# Beam editor UI verification

This harness compiles the **actual** [beam editor](../../NativeRebar/BeamRebarEditorWindow.cs)
with minimal Revit data fixtures, opens its modal WPF window, exercises controls,
and renders PNGs at 1648×928, 1280×760, 1024×640 and 960×540 logical pixels.

The production dialogs open at 50% of the Windows work area. On a 1920×1080
display this is approximately 960×520 (the taskbar reduces the usable height);
the resize grip remains available for a larger preview.

```powershell
dotnet build tests/BeamRebarUi/BeamRebarUi.csproj --configuration Release
$process = Start-Process tests/BeamRebarUi/bin/Release/net48/BeamRebarUi.exe -ArgumentList beam-ui-previews -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { Get-Content beam-ui-previews/render-error.txt; exit $process.ExitCode }
```

Checks cover action visibility, accordion navigation, editing and undoing counts,
undoing presets and group deletion, zoom preservation during resize, and basic input validation.
The beam preview also asserts three section markers per span and verifies that
equal reinforcement quantities reuse the same `MC-*` section name.
The sample also includes a detected `Dầm phụ giao` junction and verifies its
marker is rendered in the elevation preview.
The production quick editor exposes **Chọn dầm phụ giao…**; it returns the
current options to the Revit command, performs a multi-pick with the Revit
selection API, and reopens the editor with the selected beam IDs retained.
The generated main-beam tie stations include an automatic supplementary zone
of `b + 2h` around each selected secondary-beam junction, using the configured
end-zone spacing. The reaction-based required quantity remains an engineering
input/check and is not guessed by the UI.
GitHub Actions publishes the rendered images as **KStruct-Beam-UI**.

The fixtures are not an engineering engine. They do not create or validate Revit
elements; the anchorage calculation fixture throws if called. The complete add-in
is separately built against the Revit 2024 API. Final checks for hosting,
Shape/Hook compatibility, anchorage and model creation still require Revit.

The new layout retains the advanced editor for secondary layers, hooks and TCVN
settings. The undo history is local to the quick editor (maximum 100 states);
it does not undo model creation or previously saved defaults.
