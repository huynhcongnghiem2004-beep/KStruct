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
    public sealed class SyncStructuralColumnsCommand : IExternalCommand
    {
        private sealed class ColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                element?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Reference sourceRef = ui.Selection.PickObject(ObjectType.Element, new ColumnFilter(),
                    "Chọn cột kết cấu mẫu để đồng bộ");
                FamilyInstance source = doc.GetElement(sourceRef) as FamilyInstance;
                if (source == null) return Result.Cancelled;
                LocationPoint sourceLocation = source.Location as LocationPoint;
                if (sourceLocation == null)
                    throw new InvalidOperationException("Chỉ hỗ trợ cột dạng điểm; cột slanted chưa được đồng bộ.");

                IList<Reference> targetRefs = ui.Selection.PickObjects(ObjectType.Element, new ColumnFilter(),
                    "Chọn các cột đích; vị trí XY của từng cột sẽ được giữ nguyên");
                List<FamilyInstance> targets = targetRefs.Select(r => doc.GetElement(r)).OfType<FamilyInstance>()
                    .Where(e => e.Id != source.Id).Distinct().ToList();
                if (targets.Count == 0) return Result.Cancelled;

                int changed = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Đồng bộ cột kết cấu"))
                {
                    tx.Start();
                    foreach (FamilyInstance target in targets)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                if (target.GetTypeId() != source.GetTypeId())
                                    target.ChangeTypeId(source.GetTypeId());
                                LocationPoint targetLocation = target.Location as LocationPoint;
                                if (targetLocation == null)
                                    throw new InvalidOperationException("Cột đích không có LocationPoint.");
                                CopyParameter(doc, source, target, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
                                CopyParameter(doc, source, target, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                                CopyParameter(doc, source, target, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                                CopyParameter(doc, source, target, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                                CopyParameter(doc, source, target, BuiltInParameter.INSTANCE_STRUCT_USAGE_PARAM);
                                double rotationDelta = sourceLocation.Rotation - targetLocation.Rotation;
                                if (Math.Abs(rotationDelta) > 1e-9)
                                {
                                    XYZ pivot = targetLocation.Point;
                                    targetLocation.Rotate(Line.CreateBound(pivot, pivot + XYZ.BasisZ), rotationDelta);
                                }
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không hoàn tất đồng bộ cột.");
                                changed++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Cột " + target.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (changed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Đồng bộ cột", "Không cập nhật được cột nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các thay đổi cột.");
                }
                string report = "Đã đồng bộ type, ràng buộc level/offset, structural usage và góc xoay từ cột " +
                    source.Id.Value + " cho " + changed + "/" + targets.Count + " cột. Vị trí XY và Mark của từng cột được giữ nguyên.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " cột:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Đồng bộ cột", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Đồng bộ cột", ex.Message);
                return Result.Failed;
            }
        }

        private static void CopyParameter(Document doc, Element source, Element target, BuiltInParameter id)
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
                    if (value == ElementId.InvalidElementId || doc.GetElement(value) != null)
                        targetParameter.Set(value);
                    break;
            }
        }
    }
}
