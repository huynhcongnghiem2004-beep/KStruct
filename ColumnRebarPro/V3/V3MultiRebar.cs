using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    // Cross-section main bars use Multi-Rebar Annotation. Individual main-bar
    // tags are not used as a fallback because the office template controls the
    // annotation presentation.
    internal sealed partial class V3DrawingBuilder
    {
        private MultiReferenceAnnotationType FindMultiRebarType(Document doc)
        {
            var chosen = doc.GetElement(Id("CrossMultiRebar")) as MultiReferenceAnnotationType;
            if (chosen != null) return chosen;
            var all = new FilteredElementCollector(doc).OfClass(typeof(MultiReferenceAnnotationType))
                .Cast<MultiReferenceAnnotationType>().ToList();
            return all.FirstOrDefault(t => t.Name.IndexOf("align", StringComparison.OrdinalIgnoreCase) >= 0
                                        || t.Name.IndexOf("aline", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? all.FirstOrDefault();
        }

        private static bool IsStirrupBar(Rebar bar)
        {
            try
            {
                var curves = bar.GetCenterlineCurves(false, true, true,
                    MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                if (curves.Count == 0) return false;
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (var curve in curves)
                    foreach (var point in new[] { curve.GetEndPoint(0), curve.GetEndPoint(1) })
                    {
                        if (point.Z < lo) lo = point.Z;
                        if (point.Z > hi) hi = point.Z;
                    }
                return hi - lo < V3Units.Ft(50);
            }
            catch { return false; }
        }

        private HashSet<ElementId> AnnotateMultiRebar(Document doc, V3SegmentWork work,
            ViewSection view, List<Rebar> bars)
        {
            var covered = new HashSet<ElementId>();
            var type = FindMultiRebarType(doc);
            if (type == null)
                throw new InvalidOperationException(
                    "Mặt cắt ngang yêu cầu Multi-Rebar Annotation Type; project/template chưa có MultiTag phù hợp.");

            var geometry = work.Geometry;
            double scale = view.Scale;
            double tolerance = doc.Application.ShortCurveTolerance;
            XYZ columnCenter = Project(view, geometry.At(0, 0, 0));
            XYZ right = view.RightDirection, up = view.UpDirection;
            var done = new HashSet<string>();

            foreach (var bar in bars.OrderBy(item => item.Id.Value))
            {
                if (bar.Quantity < 2)
                    throw new InvalidOperationException(
                        "Rebar " + bar.Id.Value + " không phải Rebar Set nhiều vị trí; không thể tag thép chủ mặt cắt bằng MultiTag.");

                IList<Curve> first, last;
                try
                {
                    first = bar.GetCenterlineCurves(false, true, true,
                        MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                    last = bar.GetCenterlineCurves(false, true, true,
                        MultiplanarOption.IncludeOnlyPlanarCurves, bar.NumberOfBarPositions - 1);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Không đọc được đường tâm Rebar " + bar.Id.Value + " để tạo MultiTag.", ex);
                }
                if (first.Count == 0 || last.Count == 0)
                    throw new InvalidOperationException("Rebar " + bar.Id.Value + " không có đường tâm để tạo MultiTag.");

                XYZ a = Project(view, first[0].GetEndPoint(0));
                XYZ b = Project(view, last[0].GetEndPoint(0));
                XYZ direction = b - a;
                if (direction.GetLength() < tolerance)
                    throw new InvalidOperationException("Rebar " + bar.Id.Value + " có khoảng phân bố bằng 0; không thể tạo MultiTag.");
                direction = direction.Normalize();
                XYZ midpoint = (a + b) / 2;
                bool alongUp = Math.Abs(direction.DotProduct(up)) > Math.Abs(direction.DotProduct(right));

                var barType = doc.GetElement(bar.GetTypeId()) as RebarBarType;
                string key = (alongUp ? "U" : "R") + "|" + bar.Quantity + "|" +
                    (barType == null ? 0 : Math.Round(V3Units.Mm(barType.BarNominalDiameter))) + "|" +
                    Math.Round(V3Units.Mm(b.DistanceTo(a)));
                if (!done.Add(key))
                {
                    covered.Add(bar.Id);
                    continue;
                }

                XYZ normal, origin;
                double along = (midpoint - columnCenter).DotProduct(alongUp ? up : right);
                if (alongUp)
                {
                    normal = -right;
                    origin = columnCenter + up * along + normal *
                        (geometry.Width / 2 + V3Units.Ft(8 * scale));
                }
                else
                {
                    normal = -up;
                    origin = columnCenter + right * along + normal *
                        (geometry.Depth / 2 + V3Units.Ft(8 * scale));
                }

                try
                {
                    var options = new MultiReferenceAnnotationOptions(type);
                    options.SetElementsToDimension(new List<ElementId> { bar.Id });
                    options.DimensionLineOrigin = origin;
                    options.DimensionLineDirection = direction;
                    options.DimensionPlaneNormal = view.ViewDirection;
                    options.TagHeadPosition = origin + normal * V3Units.Ft(10 * scale);
                    options.TagHasLeader = false;
                    MultiReferenceAnnotation.Create(doc, view.Id, options);
                    covered.Add(bar.Id);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Không tạo được Multi-Rebar Annotation cho Rebar " + bar.Id.Value + ".", ex);
                }
            }
            return covered;
        }
    }
}
