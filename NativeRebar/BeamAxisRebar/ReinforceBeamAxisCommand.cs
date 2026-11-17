using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReinforceBeamAxisCommand : IExternalCommand
    {
        private sealed class SourceFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Grid || IsBeam(element);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class AxisReference
        {
            public Curve Curve;
            public bool IsGrid;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> sources = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .Where(e => e is Grid || IsBeam(e)).Distinct().ToList();
                if (sources.Count == 0)
                    sources = ui.Selection.PickObjects(ObjectType.Element, new SourceFilter(),
                            "Chọn một hoặc nhiều Grid/dầm làm trục bố trí thép")
                        .Select(reference => doc.GetElement(reference)).Where(e => e != null).Distinct().ToList();
                if (sources.Count == 0) return Result.Cancelled;

                Level activeLevel = (doc.ActiveView as ViewPlan)?.GenLevel;
                ElementId targetLevelId = sources.OfType<FamilyInstance>().Select(beam => beam.LevelId)
                    .FirstOrDefault(id => id != ElementId.InvalidElementId);
                if (targetLevelId == ElementId.InvalidElementId && activeLevel != null) targetLevelId = activeLevel.Id;
                if (targetLevelId == ElementId.InvalidElementId && sources.Any(source => source is Grid))
                {
                    Level nearestLevel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                        .OrderBy(level => Math.Abs(level.Elevation - doc.ActiveView.Origin.Z)).FirstOrDefault();
                    if (nearestLevel != null) targetLevelId = nearestLevel.Id;
                }

                List<AxisReference> axes = sources.Select(SourceAxis).Where(axis => axis != null).ToList();
                if (axes.Count == 0) throw new InvalidOperationException("Chỉ hỗ trợ Grid thẳng và dầm có trục thẳng.");
                List<FamilyInstance> candidates = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming).OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>().Where(IsBeam)
                    .Where(beam => targetLevelId == ElementId.InvalidElementId || beam.LevelId == targetLevelId)
                    .Where(beam =>
                    {
                        Curve axis = (beam.Location as LocationCurve)?.Curve;
                        return axis != null && axes.Any(source => IsOnAxis(axis, source.Curve, source.IsGrid));
                    }).Distinct().ToList();
                if (candidates.Count == 0)
                    throw new InvalidOperationException("Không tìm thấy dầm thẳng cùng tuyến trên Level hiện hành.");

                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                List<RebarHookType> hooks = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType))
                    .Cast<RebarHookType>().OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                ReinforceStraightBeamsCommand.Options defaults = ReinforceStraightBeamsCommand.LoadSettings(doc, types, hooks);
                ReinforceStraightBeamsCommand.Options options = ReinforceStraightBeamsCommand.ChooseOptions(
                    data.Application.MainWindowHandle, types, hooks, defaults, false, "Cốt thép dầm theo trục - KStruct");
                if (options == null) return Result.Cancelled;

                string report = ReinforceStraightBeamsCommand.ApplySettingsToBeams(doc, candidates, beam => options);
                TaskDialog.Show("KStruct - Cốt thép dầm theo trục", "Đã xử lý " + candidates.Count + " dầm cùng trục.\n" + report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cốt thép dầm theo trục", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsBeam(Element element) => element is FamilyInstance beam && beam.Category != null &&
            beam.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;

        private static AxisReference SourceAxis(Element element)
        {
            Curve curve = element is Grid grid ? grid.Curve : (element as FamilyInstance)?.Location is LocationCurve location ? location.Curve : null;
            if (!(curve is Line)) return null;
            return new AxisReference { Curve = curve, IsGrid = element is Grid };
        }

        private static bool IsOnAxis(Curve candidate, Curve source, bool sourceIsGrid)
        {
            if (!(candidate is Line candidateLine) || !(source is Line sourceLine)) return false;
            XYZ a = candidateLine.GetEndPoint(0), b = candidateLine.GetEndPoint(1);
            XYZ s0 = sourceLine.GetEndPoint(0), s1 = sourceLine.GetEndPoint(1);
            XYZ candidateDirection = b - a, sourceDirection = s1 - s0;
            if (sourceIsGrid)
            {
                XYZ cxy = new XYZ(candidateDirection.X, candidateDirection.Y, 0);
                XYZ sxy = new XYZ(sourceDirection.X, sourceDirection.Y, 0);
                if (cxy.GetLength() < 1e-8 || sxy.GetLength() < 1e-8 || Math.Abs(cxy.Normalize().DotProduct(sxy.Normalize())) < 0.9994)
                    return false;
                return DistanceToAxisXY(a, s0, sxy) <= AxisTolerance && DistanceToAxisXY(b, s0, sxy) <= AxisTolerance;
            }

            if (candidateDirection.GetLength() < 1e-8 || sourceDirection.GetLength() < 1e-8 ||
                Math.Abs(candidateDirection.Normalize().DotProduct(sourceDirection.Normalize())) < 0.9994)
                return false;
            return (a - s0).CrossProduct(sourceDirection.Normalize()).GetLength() <= AxisTolerance &&
                (b - s0).CrossProduct(sourceDirection.Normalize()).GetLength() <= AxisTolerance;
        }

        private static double DistanceToAxisXY(XYZ point, XYZ origin, XYZ direction)
        {
            double length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length < 1e-8) return double.MaxValue;
            return Math.Abs((point.X - origin.X) * direction.Y - (point.Y - origin.Y) * direction.X) / length;
        }

        private static readonly double AxisTolerance = UnitUtils.ConvertToInternalUnits(250, UnitTypeId.Millimeters);
    }
}
