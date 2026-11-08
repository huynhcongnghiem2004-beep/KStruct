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
    public sealed class ReinforceBubbleSlabsCommand : IExternalCommand
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
                            "Chọn sàn có hình Bubble KStruct để bố trí thép hai phương")
                        .Select(reference => doc.GetElement(reference)).OfType<Floor>().Distinct().ToList();
                if (floors.Count == 0) return Result.Cancelled;

                List<Floor> bubbleFloors = floors.Where(floor => HasManagedBubble(doc, floor.Id)).ToList();
                if (bubbleFloors.Count == 0)
                    throw new InvalidOperationException("Không tìm thấy hình Bubble KStruct trên sàn đã chọn. Chạy ‘Tạo hình Bubble slab’ trước.");
                List<ElementId> ids = bubbleFloors.Select(floor => floor.Id).ToList();
                ui.Selection.SetElementIds(ids);
                return new ReinforceRectangularSlabCommand().Execute(data, ref message, elements);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thép sàn Bubble", ex.Message);
                return Result.Failed;
            }
        }

        private static bool HasManagedBubble(Document doc, ElementId floorId)
        {
            string dataId = floorId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).Cast<DirectShape>()
                .Any(shape => string.Equals(shape.ApplicationId, "KStruct.BubbleSlab", StringComparison.Ordinal) &&
                    string.Equals(shape.ApplicationDataId, dataId, StringComparison.Ordinal));
        }
    }
}
