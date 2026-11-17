using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class DeleteManagedSlabModulesCommand : IExternalCommand
    {
        private sealed class FloorFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Floor;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Floor> floors = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Floor>().Distinct().ToList();
                if (floors.Count == 0)
                    floors = ui.Selection.PickObjects(ObjectType.Element, new FloorFilter(),
                        "Chọn sàn để xóa các family module do KStruct tạo")
                        .Select(reference => doc.GetElement(reference)).OfType<Floor>().Distinct().ToList();
                if (floors.Count == 0) return Result.Cancelled;

                HashSet<long> floorIds = new HashSet<long>(floors.Select(floor => floor.Id.Value));
                List<FamilyInstance> managed = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType().Cast<FamilyInstance>()
                    .Where(instance =>
                    {
                        long? hostFloorId = PlaceSlabModulesCommand.ReadManagedModuleFloorId(instance);
                        return hostFloorId.HasValue && floorIds.Contains(hostFloorId.Value);
                    }).ToList();
                if (managed.Count == 0)
                {
                    TaskDialog.Show("KStruct - Module sàn", "Không có family module do KStruct quản lý trên các sàn đã chọn.");
                    return Result.Cancelled;
                }

                TaskDialogResult answer = TaskDialog.Show("KStruct - Xóa module sàn",
                    "Xóa " + managed.Count + " family module do KStruct tạo trên " + floors.Count + " sàn đã chọn?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                if (answer != TaskDialogResult.Yes) return Result.Cancelled;

                using (Transaction tx = new Transaction(doc, "KStruct - Xóa module sàn đã quản lý"))
                {
                    tx.Start();
                    doc.Delete(managed.Select(instance => instance.Id).ToList());
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận xóa các module KStruct.");
                }
                TaskDialog.Show("KStruct - Module sàn", "Đã xóa " + managed.Count + " family module KStruct.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Module sàn", ex.Message);
                return Result.Failed;
            }
        }
    }
}
