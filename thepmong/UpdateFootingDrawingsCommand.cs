using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class UpdateFootingDrawingsCommand : IExternalCommand
    {
        private sealed class FoundationFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance fi && fi.Category != null &&
                fi.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation && fi.Location is LocationPoint;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> footings = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(IsFoundation).Distinct().ToList();
                var hostIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (FootingDrawingStorage.TryRead(ui.ActiveView, out string activeHost, out string activeRole, out double activeMargin)) hostIds.Add(activeHost);
                foreach (ElementId selectedId in ui.Selection.GetElementIds())
                {
                    Element selected = doc.GetElement(selectedId);
                    View view = selected as View;
                    if (selected is Viewport viewport) view = doc.GetElement(viewport.ViewId) as View;
                    if (FootingDrawingStorage.TryRead(view, out string viewHost, out string viewRole, out double viewMargin)) hostIds.Add(viewHost);
                }
                foreach (string uniqueId in hostIds)
                    if (doc.GetElement(uniqueId) is FamilyInstance markedFooting && IsFoundation(markedFooting)) footings.Add(markedFooting);
                if (footings.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FoundationFilter(), "Chọn móng có hồ sơ KStruct cần cập nhật");
                    footings = picked.Select(x => doc.GetElement(x)).OfType<FamilyInstance>().Where(IsFoundation).Distinct().ToList();
                }
                if (footings.Count == 0) return Result.Cancelled;

                int updated = 0;
                var skipped = new List<string>();
                using (var tx = new Transaction(doc, "KStruct - Cập nhật hồ sơ móng"))
                {
                    tx.Start();
                    foreach (FamilyInstance footing in footings)
                    {
                        List<View> views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                            .Where(v => !v.IsTemplate && FootingDrawingStorage.TryRead(v, out string host, out string role, out double margin) &&
                                host == footing.UniqueId && role != "Sheet").ToList();
                        if (views.Count == 0)
                        {
                            // Legacy sections created before view metadata used the numeric ElementId in their names.
                            string legacy = "KStruct - MĐ " + footing.Id.Value + " - MC";
                            views = new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<ViewSection>()
                                .Where(v => !v.IsTemplate && v.Name.StartsWith(legacy, StringComparison.OrdinalIgnoreCase)).Cast<View>().ToList();
                        }
                        if (views.Count == 0) { skipped.Add("Móng " + footing.Id.Value + ": không tìm thấy view đã tạo."); continue; }
                        using (var sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                foreach (View view in views)
                                {
                                    double margin = 300;
                                    FootingDrawingStorage.TryRead(view, out string ignoredHost, out string role, out margin);
                                    FootingDrawingBuilder.UpdateCrop(view, footing, margin, role);
                                }
                                doc.Regenerate();
                                if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận crop mới.");
                                updated += views.Count;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Móng " + footing.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (updated == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Cập nhật hồ sơ móng", "Không có view nào được cập nhật.\n" + string.Join("\n", skipped.Take(10)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không commit được cập nhật.");
                }
                string report = "Đã cập nhật crop của " + updated + " view. Tag/dimension liên kết và chỉnh sửa tay được giữ nguyên.";
                if (skipped.Count > 0) report += "\nBỏ qua:\n" + string.Join("\n", skipped.Take(10));
                TaskDialog.Show("KStruct - Cập nhật hồ sơ móng", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.ToString(); TaskDialog.Show("KStruct - Cập nhật hồ sơ móng", ex.Message); return Result.Failed; }
        }
        private static bool IsFoundation(FamilyInstance fi) => fi != null && fi.Category != null &&
            fi.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation && fi.Location is LocationPoint;
    }
}


