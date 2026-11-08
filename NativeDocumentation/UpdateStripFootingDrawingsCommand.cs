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
    public sealed class UpdateStripFootingDrawingsCommand : IExternalCommand
    {
        private sealed class FootingFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) { return element is WallFoundation; }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<WallFoundation> footings = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<WallFoundation>().Distinct().ToList();
                if (footings.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FootingFilter(),
                        "Chọn các móng băng có bản vẽ KStruct cần cập nhật");
                    footings = picked.Select(x => doc.GetElement(x)).OfType<WallFoundation>().Distinct().ToList();
                }
                if (footings.Count == 0) return Result.Cancelled;

                int updated = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Cập nhật bản vẽ móng băng"))
                {
                    transaction.Start();
                    foreach (WallFoundation footing in footings)
                    {
                        string prefix = "KStruct - MC " + footing.Id.Value + " - T" + footing.WallId.Value;
                        ViewSection view = new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<ViewSection>()
                            .FirstOrDefault(x => !x.IsTemplate && x.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                        if (view == null)
                        {
                            skipped.Add("Móng " + footing.Id.Value + ": không tìm thấy mặt cắt KStruct (view có thể đã đổi tên hoặc chưa được tạo).");
                            continue;
                        }
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                UpdateCrop(view, footing);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận vùng crop mới.");
                                updated++;
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
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Cập nhật móng băng", "Không cập nhật được view nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                }

                string report = "Đã cập nhật crop của " + updated + "/" + footings.Count + " mặt cắt. Sheet và annotation được giữ nguyên.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " móng:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Cập nhật móng băng", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cập nhật móng băng", ex.Message);
                return Result.Failed;
            }
        }

        private static void UpdateCrop(ViewSection view, WallFoundation footing)
        {
            BoundingBoxXYZ source = footing.get_BoundingBox(null);
            BoundingBoxXYZ oldCrop = view.CropBox;
            if (source == null || oldCrop == null) throw new InvalidOperationException("Không đọc được bounding box móng hoặc view.");
            Transform transform = oldCrop.Transform ?? Transform.Identity;
            Transform sourceTransform = source.Transform ?? Transform.Identity;
            var points = new List<XYZ>();
            for (int ix = 0; ix < 2; ix++) for (int iy = 0; iy < 2; iy++) for (int iz = 0; iz < 2; iz++)
            {
                XYZ local = new XYZ(ix == 0 ? source.Min.X : source.Max.X,
                    iy == 0 ? source.Min.Y : source.Max.Y, iz == 0 ? source.Min.Z : source.Max.Z);
                points.Add(sourceTransform.OfPoint(local));
            }
            XYZ origin = transform.Origin;
            XYZ x = transform.BasisX.Normalize(), y = transform.BasisY.Normalize(), z = transform.BasisZ.Normalize();
            double margin = UnitUtils.ConvertToInternalUnits(300, UnitTypeId.Millimeters);
            double minX = points.Min(p => (p - origin).DotProduct(x)) - margin;
            double maxX = points.Max(p => (p - origin).DotProduct(x)) + margin;
            double minY = points.Min(p => (p - origin).DotProduct(y)) - margin;
            double maxY = points.Max(p => (p - origin).DotProduct(y)) + margin;
            double minZ = points.Min(p => (p - origin).DotProduct(z)) - margin;
            double maxZ = points.Max(p => (p - origin).DotProduct(z)) + margin;
            view.CropBox = new BoundingBoxXYZ
            {
                Transform = transform,
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
            view.CropBoxActive = true;
        }
    }
}
