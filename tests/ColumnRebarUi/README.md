# Column WPF fixture

This Windows-only fixture opens the actual Release column editor with sample column dimensions and `document = null`, using the installed Revit 2024 API libraries. It checks four tabs, quick-to-detail selection events, tie-plan rendering, and WPF binding errors. It does not create a Revit document, validate a model, compare design geometry, or simulate multiple monitor DPIs. It reads the existing KStruct last-used column profile if one exists. The separate fixture process exits without closing the dialog because the production close handler saves preferences; it does not intentionally write that profile.

Build the add-in first, then build and run the fixture (Revit 2024 must be installed):

```powershell
dotnet build KStruct.csproj --configuration Release
dotnet build tests/ColumnRebarUi/ColumnRebarUi.csproj --configuration Release
$env:PATH = 'C:\Program Files\Autodesk\Revit 2024;' + $env:PATH
$report = Join-Path $env:TEMP 'kstruct-column-ui-result.txt'
$p = Start-Process tests/ColumnRebarUi/bin/Release/net48/ColumnRebarUi.exe -ArgumentList ('"' + $report + '"') -WindowStyle Hidden -Wait -PassThru
Get-Content $report
if ($p.ExitCode -ne 0) { exit $p.ExitCode }
```

CI compiles the fixture with pinned API references but does not run it: the runner does not contain the native Revit runtime dependencies. Run the separate fixture on an installed Revit workstation and test actual model behavior in Revit.
