using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReinforceUBootSlabsCommand : IExternalCommand
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
                    floors = ui.Selection.PickObjects(ObjectType.Element, new FloorFilter(), "Chọn một sàn U-Boot đã bố trí module")
                        .Select(reference => doc.GetElement(reference)).OfType<Floor>().Distinct().ToList();
                if (floors.Count != 1)
                    throw new InvalidOperationException("Lệnh thép U-Boot xử lý một sàn mỗi lần. Hãy chọn đúng một Floor.");
                Floor floor = floors[0];
                List<FamilyInstance> modules = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(instance => instance.Symbol != null &&
                        ((instance.Symbol.FamilyName ?? "").IndexOf("UBoot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         (instance.Symbol.Name ?? "").IndexOf("UBoot", StringComparison.OrdinalIgnoreCase) >= 0))
                    .Where(instance => PlaceSlabModulesCommand.ReadManagedModuleFloorId(instance) == floor.Id.Value ||
                        (instance.Host != null && instance.Host.Id == floor.Id))
                    .ToList();
                if (modules.Count == 0)
                    throw new InvalidOperationException("Không tìm thấy family U-Boot trên sàn. Nạp family, rồi dùng ‘Bố trí module sàn’ trước.");

                ui.Selection.SetElementIds(new[] { floor.Id }.Concat(modules.Select(module => module.Id)).ToList());
                return new ReinforceRectangularSlabCommand().Execute(data, ref message, elements);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thép sàn U-Boot", ex.Message);
                return Result.Failed;
            }
        }
    }
}
