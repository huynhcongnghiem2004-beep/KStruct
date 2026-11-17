using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ShowRebarRoundedLengthCommand : IExternalCommand
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
            try
            {
                Document doc = ui.Document;
                List<Rebar> rebars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (rebars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(), "Chọn Rebar để xem chiều dài đã làm tròn");
                    rebars = picked.Select(doc.GetElement).OfType<Rebar>().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;

                var report = new StringBuilder();
                foreach (Rebar rebar in rebars)
                {
                    double length = GetRoundedLength(rebar);
                    report.Append("Rebar ").Append(rebar.Id.Value).Append(": ")
                        .Append(UnitUtils.ConvertFromInternalUnits(length, UnitTypeId.Millimeters).ToString("0.##"))
                        .AppendLine(" mm");
                }
                TaskDialog.Show("KStruct - Chiều dài thép", report.ToString());
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Chiều dài thép", ex.Message);
                return Result.Failed;
            }
        }

        private static double GetRoundedLength(Rebar rebar)
        {
            if (rebar.IsRebarShapeDriven() && rebar.DistributionType == DistributionType.VaryingLength)
            {
                Parameter total = rebar.LookupParameter("Total Bar Length");
                Parameter barLength = rebar.LookupParameter("Bar Length");
                if (total != null && total.HasValue && rebar.Quantity > 0)
                    return RoundSegment(rebar, total.AsDouble() / rebar.Quantity);
                if (barLength != null && barLength.HasValue)
                    return RoundSegment(rebar, barLength.AsDouble());
                return 0;
            }

            double sum = 0;
            try
            {
                Parameter shapeParameter = rebar.get_Parameter(BuiltInParameter.REBAR_SHAPE);
                ElementId shapeId = shapeParameter != null && shapeParameter.HasValue
                    ? shapeParameter.AsElementId() : ElementId.InvalidElementId;
                RebarShape shape = rebar.Document.GetElement(shapeId) as RebarShape;
                RebarShapeDefinitionBySegments definition = shape?.GetRebarShapeDefinition() as RebarShapeDefinitionBySegments;
                if (definition != null)
                {
                    var lengthParameters = new HashSet<ElementId>();
                    for (int i = 0; i < definition.NumberOfSegments; i++)
                    {
                        foreach (RebarShapeConstraint constraint in definition.GetSegment(i).GetConstraints())
                        {
                            if (constraint is RebarShapeConstraintSegmentLength)
                            {
                                ElementId id = constraint.GetParamId();
                                if (id != ElementId.InvalidElementId) lengthParameters.Add(id);
                            }
                        }
                    }
                    foreach (Parameter parameter in shape.Parameters)
                        if (lengthParameters.Contains(parameter.Id) && parameter.StorageType == StorageType.Double && parameter.HasValue)
                            sum += RoundSegment(rebar, parameter.AsDouble());
                }

                RebarBendData bend = rebar.GetBendData();
                if (bend.HookAngle0 > 0)
                    sum += RoundBuiltIn(rebar, BuiltInParameter.REBAR_SHAPE_START_HOOK_LENGTH);
                if (bend.HookAngle1 > 0)
                    sum += RoundBuiltIn(rebar, BuiltInParameter.REBAR_SHAPE_END_HOOK_LENGTH);
            }
            catch
            {
                sum = 0;
            }

            if (sum > 0) return sum;
            Parameter fallback = rebar.LookupParameter("Bar Length");
            return fallback != null && fallback.HasValue ? RoundSegment(rebar, fallback.AsDouble()) : 0;
        }

        private static double RoundBuiltIn(Rebar rebar, BuiltInParameter parameterId)
        {
            Parameter parameter = rebar.get_Parameter(parameterId);
            return parameter != null && parameter.HasValue ? RoundSegment(rebar, parameter.AsDouble()) : 0;
        }

        private static double RoundSegment(Rebar rebar, double internalLength)
        {
            double millimeters = UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Millimeters);
            RebarRoundingManager manager = rebar.GetReinforcementRoundingManager();
            if (manager == null) return internalLength;
            double increment = manager.ApplicableSegmentLengthRounding;
            if (increment == 0) increment = 1;
            switch (manager.ApplicableSegmentLengthRoundingMethod)
            {
                case RoundingMethod.Up: millimeters = Math.Ceiling(millimeters / increment) * increment; break;
                case RoundingMethod.Down: millimeters = Math.Floor(millimeters / increment) * increment; break;
                default: millimeters = Math.Round(millimeters / increment, 0, MidpointRounding.AwayFromZero) * increment; break;
            }
            return UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);
        }
    }
}
