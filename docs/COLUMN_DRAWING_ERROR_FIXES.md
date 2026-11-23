# Three column workflow errors

## Foundation thickness

The old resolver required an English parameter name and could ignore a valid type
value when an empty instance parameter existed. It also compared world-axis widths
to family-local dimensions, rejecting rotated foundations.

The resolver now reads Revit's built-in foundation thickness on instance/type,
then the legacy name. Without thickness it accepts only a unique shallow prism
whose top contains the column footprint, with matching top/bottom face areas and
volume. Nested deep piles, joined cap/pile geometry and ambiguous candidates are
rejected. The complete anchor containment check still runs before creation.

## Zero MultiTag span

Centerline shape geometry alone is not the placement of a shape-driven set.
The annotation code now applies GetBarPositionTransform to the first/last existing
positions (excluded ends are skipped). Single bars or sets that project to one
point receive the selected main-bar tag, or the MultiTag's tag type. Distinct sets
are no longer suppressed just because quantity, diameter and span match.

## Draw existing reinforcement

The standalone drawing command resolves saved deployment choices through the
same identity resolver as the settings UI, enables drawing for this invocation,
and uses project defaults for missing annotation choices. It no longer requires
the user to visit the rebar creation window or enable its drawing checkbox.
It does not re-run rebar creation/connection preflight or modify existing bars.
Existing KStruct column configuration and partition ownership remain required;
this is not an importer for arbitrary third-party rebar. Required project assets
and sheet ownership are still validated. Auto choices are not saved over the
user's configuration.

## Validation

Run `dotnet run --project tests/ColumnRebarLayout/ColumnRebarLayout.csproj -c Release`
and `dotnet build KStruct.csproj -c Debug` with Revit 2024 references installed.
The existing CI runs the layout tests and full add-in build.

Revit checks still needed on a copy of the user's model:

- Cap with built-in thickness, empty instance/type value, rotated cap, cap without
  thickness, nested piles, joined geometry and ambiguous solids; inspect L bend and cover.
- Rebar 13250351, single corner bar, excluded first/last bar, mirrored/rotated set;
  inspect annotation references and positions without changing the bars.
- Existing KStruct columns with no deployment XML, disabled checkbox and saved
  template choices; ensure drawing creation leaves bar IDs/geometry unchanged.
- Missing family assets, duplicate sheet numbers, update ownership and Undo.
