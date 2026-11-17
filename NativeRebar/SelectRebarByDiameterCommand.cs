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
    public sealed class SelectRebarByDiameterCommand : IExternalCommand
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
                Rebar sample = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().FirstOrDefault();
                if (sample == null)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new RebarFilter(), "Chọn một thanh thép làm mẫu đường kính");
                    sample = doc.GetElement(picked) as Rebar;
                }
                if (sample == null) return Result.Cancelled;
                RebarBarType sampleType = doc.GetElement(sample.GetTypeId()) as RebarBarType;
                Parameter sampleDiameterParameter = sampleType?.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                if (sampleDiameterParameter == null || !sampleDiameterParameter.HasValue)
                    throw new InvalidOperationException("Không đọc được đường kính của thanh mẫu.");
                double diameter = sampleDiameterParameter.AsDouble();
                double tolerance = UnitUtils.ConvertToInternalUnits(0.01, UnitTypeId.Millimeters);

                List<ElementId> matches = new FilteredElementCollector(doc).OfClass(typeof(Rebar))
                    .WhereElementIsNotElementType().Cast<Rebar>()
                    .Where(rebar =>
                    {
                        RebarBarType barType = doc.GetElement(rebar.GetTypeId()) as RebarBarType;
                        Parameter parameter = barType?.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                        return parameter != null && parameter.HasValue && Math.Abs(parameter.AsDouble() - diameter) <= tolerance;
                    })
                    .Select(rebar => rebar.Id).ToList();
                ui.Selection.SetElementIds(matches);
                double diameterMm = UnitUtils.ConvertFromInternalUnits(diameter, UnitTypeId.Millimeters);
                TaskDialog.Show("KStruct - Chọn thép", "Đã chọn " + matches.Count + " thanh Ø" + diameterMm.ToString("0.##") + " mm.");
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