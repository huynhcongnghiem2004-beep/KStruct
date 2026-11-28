# Column Drawing Studio V2 — approved design
Status: design frozen; implementation and Revit acceptance pending.

Tag leader amendment: [Auto Rebar Tag Free End](../docs/AUTO_REBAR_TAG_FREE_END.md) supersedes the earlier
no-leader rule below. Rebar Tags now use horizontal Free End leaders, paper-based
offsets and collision spacing; P/T type tokens remain the compatibility side rule.
## WPF
Three panels: longitudinal left, cross upper right, other settings lower right. Run/Close. No BBS or sheet schematic in UI. Section note table is not BBS. Real preview must export actual annotated Revit views using production engine, regenerate, export, rollback temporary views, load image with zoom/pan. Never label canvas simulation as actual preview.
## Longitudinal
Representative stirrups: default three existing positions nearest center of each actual spacing zone (or fewer if unavailable); optional all/1/3/5. View-only visibility; never modify physical bars, quantities, spacing, schedules or other views. Stirrup tags use horizontal Free End leaders; one per zone, TOP/MID/BOT. Main bar tags use Free End leaders; bending detail remains further outside. Zone dimensions stay outside the tags; overall dimension is outermost.
## Cross
Two independent opposite edge groups, upper TOP-P and lower BOT-P, not Cx/Cy. Upper MRA extends up then right; lower down then right. Separate actual family/type selection. Stirrup tag MID-P with horizontal leader. Use real references, count and diameter; do not double count corners or omit intermediate bars; flag unsupported grouping rather than invent counts. Stirrup bending detail right. Section dimensions and note table from actual data.
## Shared placement
P right, T left, TOP above, MID center, BOT below, relative to view. Type token overrides family token. MCD stirrup orientation and leader are independent of shared side rules.
## Acceptance
Build Revit 2024; test rotated columns, multiple zones, unequal edge counts and diameters, intermediate bars, family selection, no overlap, representative visibility without quantity changes, preview/production parity and rollback. Do not release until actual Revit tests pass.
