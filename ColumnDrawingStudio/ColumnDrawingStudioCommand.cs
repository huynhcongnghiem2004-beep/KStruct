using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace ColumnRebarPro.V3
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ColumnDrawingStudioCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            IndependentColumnDrawingService.ExecuteV3(data, ref message, false, true);
    }
}