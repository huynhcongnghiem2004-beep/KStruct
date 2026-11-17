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
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class SelectRebarByMarkCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Rebar seed = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().FirstOrDefault();
                if (seed == null)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new RebarFilter(), "Chọn một thanh thép để chọn các thanh cùng mã");
                    seed = doc.GetElement(picked) as Rebar;
                }
                if (seed == null) return Result.Cancelled;
                Parameter markParameter = seed.get_Parameter(BuiltInParameter.REBAR_ELEM_SCHEDULE_MARK);
                string mark = markParameter != null && markParameter.HasValue ? markParameter.AsString() : null;
                if (string.IsNullOrWhiteSpace(mark))
                {
                    TaskDialog.Show("KStruct - Chọn thép", "Thanh thép đã chọn chưa có Mã thống kê.");
                    return Result.Cancelled;
                }

                List<ElementId> matches = new FilteredElementCollector(doc)
                    .OfClass(typeof(Rebar)).WhereElementIsNotElementType().Cast<Rebar>()
                    .Where(rebar =>
                    {
                        Parameter parameter = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_SCHEDULE_MARK);
                        return parameter != null && parameter.HasValue &&
                            string.Equals(parameter.AsString(), mark, StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(rebar => rebar.Id).ToList();
                ui.Selection.SetElementIds(matches);
                TaskDialog.Show("KStruct - Chọn thép", "Đã chọn " + matches.Count + " thanh có mã " + mark + ".");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Chọn thép", ex.Message);
                return Result.Failed;
            }
        }
    }
}