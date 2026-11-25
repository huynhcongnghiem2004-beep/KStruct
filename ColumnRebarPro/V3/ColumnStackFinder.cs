using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    public sealed class ColumnSegment
    {
        public ElementId Id { get; set; }
        public string LevelName { get; set; }
        public double BottomMm { get; set; }
        public double TopMm { get; set; }
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }

        public string Description
        {
            get
            {
                return LevelName + " | H=" + Math.Round(TopMm - BottomMm) + " mm | " +
                    Math.Round(WidthMm) + "×" + Math.Round(DepthMm);
            }
        }
    }

    public static class ColumnStackFinder
    {
        public static string GetMark(FamilyInstance col)
        {
            if (col == null) return string.Empty;

            var p = col.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (p != null)
            {
                var value = p.AsString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "C" + col.Id.Value;
        }

        public static List<ColumnSegment> Find(Document doc, FamilyInstance selected, double xyToleranceMm = 50)
        {
            string selectedMark = GetMark(selected);

            V3Geometry refGeom = V3GeometryReader.Read(selected);
            double tolerance = V3Units.Ft(xyToleranceMm);

            var result = new List<ColumnSegment>();

            foreach (FamilyInstance col in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>())
            {
                if (col.CreatedPhaseId != selected.CreatedPhaseId ||
                    col.DemolishedPhaseId != selected.DemolishedPhaseId ||
                    (col.DesignOption == null ? ElementId.InvalidElementId : col.DesignOption.Id) !=
                    (selected.DesignOption == null ? ElementId.InvalidElementId : selected.DesignOption.Id))
                    continue;

                if (!(col.Location is LocationPoint)) continue;

                bool useMarkMatch = !string.IsNullOrWhiteSpace(selectedMark) &&
                    !selectedMark.StartsWith("C", StringComparison.OrdinalIgnoreCase);

                if (useMarkMatch)
                {
                    string otherMark = GetMark(col);
                    if (!string.Equals(otherMark, selectedMark, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                V3Geometry geom;
                try { geom = V3GeometryReader.Read(col); }
                catch { continue; }

                XYZ a = new XYZ(geom.Center.X, geom.Center.Y, 0);
                XYZ b = new XYZ(refGeom.Center.X, refGeom.Center.Y, 0);

                if (a.DistanceTo(b) > tolerance) continue;

                result.Add(new ColumnSegment
                {
                    Id = col.Id,
                    LevelName = LevelSpanName(doc, col, geom),
                    BottomMm = V3Units.Mm(geom.Bottom),
                    TopMm = V3Units.Mm(geom.Top),
                    WidthMm = V3Units.Mm(geom.Width),
                    DepthMm = V3Units.Mm(geom.Depth)
                });
            }

            return result.OrderBy(s => s.BottomMm).ThenBy(s => s.Id.Value).ToList();
        }

        private static string LevelSpanName(Document doc, FamilyInstance column, V3Geometry geometry)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => level.Elevation)
                .ToList();
            if (levels.Count == 0) return "Không rõ tầng";

            double tolerance = V3Units.Ft(5);
            Level baseLevel = levels.LastOrDefault(level => level.Elevation <= geometry.Bottom + tolerance);
            Level topLevel = levels.LastOrDefault(level => level.Elevation <= geometry.Top + tolerance);
            if (baseLevel == null) baseLevel = doc.GetElement(column.LevelId) as Level;
            if (baseLevel == null) return topLevel == null ? "Không rõ tầng" : topLevel.Name;
            if (topLevel == null || baseLevel.Id == topLevel.Id) return baseLevel.Name;

            var crossed = levels
                .Where(level => level.Elevation >= baseLevel.Elevation - tolerance &&
                    level.Elevation <= topLevel.Elevation + tolerance)
                .Select(level => level.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return crossed.Count > 1
                ? string.Join(" → ", crossed)
                : baseLevel.Name + " → " + topLevel.Name;
        }
    }
}
