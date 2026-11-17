using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class UpdateIsolatedFootingDrawingSheetsCommand : IExternalCommand
    {
        private sealed class FootingFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                FamilyInstance instance = element as FamilyInstance;
                return instance != null && instance.Category != null &&
                    instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation &&
                    instance.Location is LocationPoint;
            }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> footings = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(IsFooting).Distinct().ToList();
                if (footings.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FootingFilter(),
                        "Chọn móng đơn có sheet KStruct cần cập nhật");
                    footings = picked.Select(x => doc.GetElement(x)).OfType<FamilyInstance>().Where(IsFooting).Distinct().ToList();
                }
                if (footings.Count == 0) return Result.Cancelled;

                var skipped = new List<string>();
                int updated = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Cập nhật hồ sơ móng đơn"))
                {
                    tx.Start();
                    foreach (FamilyInstance footing in footings)
                    {
                        string prefix = "KStruct - MĐ " + footing.Id.Value + " - MC";
                        List<ViewSection> views = new FilteredElementCollector(doc).OfClass(typeof(ViewSection))
                            .Cast<ViewSection>().Where(v => !v.IsTemplate && v.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
                        if (views.Count == 0)
                        {
                            skipped.Add("Móng " + footing.Id.Value + ": chưa có view KStruct hoặc tên view đã đổi.");
                            continue;
                        }
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                foreach (ViewSection view in views) UpdateCrop(view, footing);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận crop mới.");
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
                        TaskDialog.Show("KStruct - Cập nhật móng đơn", "Không cập nhật được mặt cắt nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                }

                string report = "Đã cập nhật crop của " + updated + " mặt cắt móng đơn. Sheet và annotation được giữ nguyên.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " móng:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Cập nhật móng đơn", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cập nhật móng đơn", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsFooting(FamilyInstance instance)
        {
            return instance != null && instance.Category != null &&
                instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation &&
                instance.Location is LocationPoint;
        }

        private static void UpdateCrop(ViewSection view, FamilyInstance footing)
        {
            BoundingBoxXYZ source = footing.get_BoundingBox(null);
            BoundingBoxXYZ prior = view.CropBox;
            if (source == null || prior == null) throw new InvalidOperationException("Không đọc được móng hoặc vùng crop.");
            Transform cropTransform = prior.Transform ?? Transform.Identity;
            Transform sourceTransform = source.Transform ?? Transform.Identity;
            var points = new List<XYZ>();
            for (int ix = 0; ix < 2; ix++) for (int iy = 0; iy < 2; iy++) for (int iz = 0; iz < 2; iz++)
                points.Add(sourceTransform.OfPoint(new XYZ(ix == 0 ? source.Min.X : source.Max.X,
                    iy == 0 ? source.Min.Y : source.Max.Y, iz == 0 ? source.Min.Z : source.Max.Z)));
            XYZ origin = cropTransform.Origin;
            XYZ x = cropTransform.BasisX.Normalize(), y = cropTransform.BasisY.Normalize(), z = cropTransform.BasisZ.Normalize();
            double margin = UnitUtils.ConvertToInternalUnits(300, UnitTypeId.Millimeters);
            var crop = new BoundingBoxXYZ
            {
                Transform = cropTransform,
                Min = new XYZ(points.Min(p => (p - origin).DotProduct(x)) - margin,
                    points.Min(p => (p - origin).DotProduct(y)) - margin,
                    points.Min(p => (p - origin).DotProduct(z)) - margin),
                Max = new XYZ(points.Max(p => (p - origin).DotProduct(x)) + margin,
                    points.Max(p => (p - origin).DotProduct(y)) + margin,
                    points.Max(p => (p - origin).DotProduct(z)) + margin)
            };
            view.CropBox = crop;
            view.CropBoxActive = true;
        }
    }
}
