using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace ColumnRebarPro.V3
{
    [Transaction(TransactionMode.Manual)]
    public sealed class PreviewColumnRebarCommand : IExternalCommand
    {
        private sealed class ColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance &&
                element.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            try
            {
                var filter = new ColumnFilter();
                var selected = ui.Selection.GetElementIds().Select(ui.Document.GetElement)
                    .Where(filter.AllowElement).Cast<FamilyInstance>().ToList();
                if (selected.Count > 1)
                {
                    TaskDialog.Show("KStruct Dev", "Chọn một cột để xem trước tuyến cột tương ứng.");
                    return Result.Cancelled;
                }
                var column = selected.SingleOrDefault() ?? (FamilyInstance)ui.Document.GetElement(
                    ui.Selection.PickObject(ObjectType.Element, filter, "Chọn cột chữ nhật để xem trước; Esc để hủy"));
                // Validate rectangular geometry before constructing the existing editor.
                V3GeometryReader.Read(column);
                var stack = ColumnStackFinder.Find(ui.Document, column);
                var sizes = new FilteredElementCollector(ui.Document).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().Select(t => V3Units.Mm(t.BarNominalDiameter)).ToList();
                var window = new ColumnRebarV3Window(stack, sizes, ui.Document, previewOnly: true);
                new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
