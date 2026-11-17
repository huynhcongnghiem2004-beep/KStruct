using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class SelectViewSpecificElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null) return Result.Cancelled;

            View view = ui.ActiveView;
            var ids = new FilteredElementCollector(ui.Document, view.Id)
                .WhereElementIsNotElementType()
                .Where(x => x.OwnerViewId == view.Id)
                .Select(x => x.Id)
                .ToList();

            if (ids.Count == 0)
            {
                TaskDialog.Show("KStruct", "View hiện tại không có đối tượng 2D riêng theo view.");
                return Result.Succeeded;
            }

            ui.Selection.SetElementIds(ids);
            TaskDialog.Show("KStruct", "Đã chọn " + ids.Count + " đối tượng 2D thuộc view hiện tại.");
            return Result.Succeeded;
        }
    }
}
