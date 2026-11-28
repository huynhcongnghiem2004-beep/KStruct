# Column Drawing Studio V2.0 — development preview

Revit 2024 / WPF / net48. Select structural columns before opening Studio to preview the first selected column. Reads KStruct partitioned Rebar centerlines and local column geometry. Longitudinal projection and mid-height cross-section centerline intersections are not fabrication drawings; horizontal stirrup geometry is not represented in the cross-section. Batch creation remains the existing V3/V4 transactional engine. JSON export contains selected element IDs and preview status. No model changes during preview.

## Verification
- Build with Revit 2024 API; open Studio with no selection, one rectangular column, and multiple columns.
- Compare preview against model, including distributed bars and missing positions.
- Test cancellation, missing resources, duplicate sheet numbers, and rollback.
- Review generated draft sheets before issue.

## Deferred
Independent bending detail/tag controls, selectable cutting direction, full sheet preview and collision detection, language controls and full DevTools integration remain pending. V2.0 is a development milestone, not a verified production release.
