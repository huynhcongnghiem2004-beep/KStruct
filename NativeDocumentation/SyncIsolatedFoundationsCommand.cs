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
    public sealed class SyncIsolatedFoundationsCommand : IExternalCommand
    {
        private sealed class FoundationFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                element?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Reference sourceRef = ui.Selection.PickObject(ObjectType.Element, new FoundationFilter(),
                    "Chọn móng đơn mẫu để đồng bộ");
                FamilyInstance source = doc.GetElement(sourceRef) as FamilyInstance;
                if (source == null) return Result.Cancelled;
                LocationPoint sourceLocation = source.Location as LocationPoint;
                if (sourceLocation == null)
                    throw new InvalidOperationException("Chỉ hỗ trợ móng dạng điểm.");

                IList<Reference> targetRefs = ui.Selection.PickObjects(ObjectType.Element, new FoundationFilter(),
                    "Chọn các móng đích; vị trí XY của từng móng sẽ được giữ nguyên");
                List<FamilyInstance> targets = targetRefs.Select(r => doc.GetElement(r)).OfType<FamilyInstance>()
                    .Where(e => e.Id != source.Id).Distinct().ToList();
                if (targets.Count == 0) return Result.Cancelled;

                int changed = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Đồng bộ móng đơn"))
                {
                    tx.Start();
                    foreach (FamilyInstance target in targets)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                if (target.Location is not LocationPoint)
                                    throw new InvalidOperationException("Móng đích không có LocationPoint.");
                                if (target.GetTypeId() != source.GetTypeId())
                                    target.ChangeTypeId(source.GetTypeId());
                                CopyParameter(source, target, BuiltInParameter.FAMILY_LEVEL_PARAM);
                                CopyParameter(source, target, BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                                LocationPoint targetLocation = target.Location as LocationPoint;
                                if (targetLocation == null)
                                    throw new InvalidOperationException("Móng đích mất LocationPoint sau khi đổi type.");
                                double rotationDelta = sourceLocation.Rotation - targetLocation.Rotation;
                                if (Math.Abs(rotationDelta) > 1e-9)
                                {
                                    XYZ pivot = targetLocation.Point;
                                    targetLocation.Rotate(Line.CreateBound(pivot, pivot + XYZ.BasisZ), rotationDelta);
                                }
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không hoàn tất đồng bộ móng.");
                                changed++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Móng " + target.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (changed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Đồng bộ móng", "Không cập nhật được móng nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các thay đổi móng.");
                }
                string report = "Đã đồng bộ type, level/cao độ và góc xoay từ móng " + source.Id.Value +
                    " cho " + changed + "/" + targets.Count + " móng. Tọa độ XY và Mark được giữ nguyên.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " móng:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Đồng bộ móng", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Đồng bộ móng", ex.Message);
                return Result.Failed;
            }
        }

        private static void CopyParameter(Element source, Element target, BuiltInParameter id)
        {
            Parameter sourceParameter = source.get_Parameter(id);
            Parameter targetParameter = target.get_Parameter(id);
            if (sourceParameter == null || targetParameter == null || !sourceParameter.HasValue ||
                targetParameter.IsReadOnly || sourceParameter.StorageType != targetParameter.StorageType)
                return;
            switch (sourceParameter.StorageType)
            {
                case StorageType.Double:
                    targetParameter.Set(sourceParameter.AsDouble());
                    break;
                case StorageType.Integer:
                    targetParameter.Set(sourceParameter.AsInteger());
                    break;
                case StorageType.String:
                    targetParameter.Set(sourceParameter.AsString() ?? "");
                    break;
                case StorageType.ElementId:
                    ElementId value = sourceParameter.AsElementId();
                    if (value == ElementId.InvalidElementId)
                        targetParameter.Set(value);
                    else if (source.Document.GetElement(value) != null)
                        targetParameter.Set(value);
                    break;
            }
        }
    }
}
