using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    // Distributed sets use Multi-Rebar Annotation; single/projected coincident
    // positions use the selected main-bar tag without changing model geometry.
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

            var geometry = work.Geometry;
            double scale = view.Scale;
            double tolerance = doc.Application.ShortCurveTolerance;
            XYZ columnCenter = Project(view, geometry.At(0, 0, 0));
            XYZ right = view.RightDirection, up = view.UpDirection;

            foreach (var bar in bars.OrderBy(item => item.Id.Value))
            {
                IList<Curve> first, last;
                var positions = Enumerable.Range(0, bar.NumberOfBarPositions)
                    .Where(bar.DoesBarExistAtPosition).ToList();
                if (positions.Count == 0)
                    throw new InvalidOperationException("Rebar " + bar.Id.Value + " không có vị trí thanh hiện hữu.");
                int firstIndex = positions.First(), lastIndex = positions.Last();
                try
                {
                    first = bar.GetCenterlineCurves(false, true, true,
                        MultiplanarOption.IncludeOnlyPlanarCurves, firstIndex);
                    last = bar.GetCenterlineCurves(false, true, true,
                        MultiplanarOption.IncludeOnlyPlanarCurves, lastIndex);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Không đọc được đường tâm Rebar " + bar.Id.Value + " để tạo MultiTag.", ex);
                }
                if (first.Count == 0 || last.Count == 0)
                    throw new InvalidOperationException("Rebar " + bar.Id.Value + " không có đường tâm để tạo MultiTag.");

                XYZ firstPoint = first[0].GetEndPoint(0), lastPoint = last[0].GetEndPoint(0);
                if (bar.IsRebarShapeDriven())
                {
                    using (var accessor = bar.GetShapeDrivenAccessor())
                    {
                        firstPoint = accessor.GetBarPositionTransform(firstIndex).OfPoint(firstPoint);
                        lastPoint = accessor.GetBarPositionTransform(lastIndex).OfPoint(lastPoint);
                    }
                }
                XYZ a = Project(view, firstPoint);
                XYZ b = Project(view, lastPoint);
                XYZ direction = b - a;
                if (positions.Count == 1 || direction.GetLength() < tolerance || type == null)
                {
                    ElementId tagId = Id("CrossMainTag");
                    if (tagId == ElementId.InvalidElementId && type != null) tagId = type.TagTypeId;
                    var tag = ActiveTagSymbol(doc, tagId);
                    string error = "Chưa chọn Tag thép dọc cho mặt cắt ngang.";
                    XYZ outward = a - columnCenter;
                    if (outward.GetLength() < tolerance) outward = right;
                    XYZ head = a + outward.Normalize() * V3Units.Ft(10 * scale);
                    if (tag == null || !TryTag(doc, view, tag, bar, head, out error))
                        throw new InvalidOperationException("Không gắn được tag cho Rebar " + bar.Id.Value + ": " + error);
                    covered.Add(bar.Id);
                    continue;
                }
                direction = direction.Normalize();
                XYZ midpoint = (a + b) / 2;
                bool alongUp = Math.Abs(direction.DotProduct(up)) > Math.Abs(direction.DotProduct(right));

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
