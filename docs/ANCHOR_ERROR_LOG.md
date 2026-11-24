# Anchor containment diagnostics

When the anchor preflight fails its solid/cover probes, the column editor opens
a read-only error table instead of the generic direction/length warning.
It includes column mark/ID, foundation ID, timestamp, Ln, cover, bar diameter,
anchor direction, column-bottom Z and bend-centerline Z.

Each row identifies a **planned** bar index (not a Revit ElementId), horizontal
foot or vertical embedment, probe side, first uncovered interval endpoints in
Revit internal-model XYZ coordinates converted to mm, and interval length.
Left/right are relative to the horizontal foot direction; X/Y are column-local.
All failing probes are collected. A row reports the first gap on that probe,
not the total uncovered length or a perpendicular cover deficit. Additional
gaps on that same probe may exist. The CSV export is UTF-8 with BOM, chosen by
the user, and quotes text fields. Preview recalculation does not open dialogs
or write files. No rebar/model elements are created by the diagnostic window.

The checks and tolerances remain in force; this does not automatically shorten
Ln, change cover, or redirect anchors. Errors before solid containment (such as
missing/ambiguous foundations) retain their existing messages.

Validation: full Revit 2024 build; ColumnRebarLayout regression tests cover gaps
at the beginning/end, internal voids, overlapping nested-solid intervals, empty
intersection and tolerated seams. Test in Revit with the B/6 corner footing:
compare the reported bar/probe coordinates with geometry, export CSV, close the
table, adjust inputs and rerun. Model-specific runtime validation is pending.
