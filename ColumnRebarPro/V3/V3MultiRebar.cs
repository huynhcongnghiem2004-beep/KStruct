using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    // Mặt cắt ngang: ghi chú thép bằng Multi-Rebar Annotation (loại Aligned) thay cho tag từng thanh.
    // Mỗi nhóm thép chủ (Rebar Set có từ 2 thanh trở lên nằm trong mặt phẳng view) được một annotation.
    internal sealed partial class V3DrawingBuilder
    {
        private MultiReferenceAnnotationType FindMultiRebarType(Document doc)
        {
            // Nếu sau này thêm ô chọn "CrossMultiRebar" trong tab Triển khai thì ưu tiên loại đó.
            var chosen = doc.GetElement(Id("CrossMultiRebar")) as MultiReferenceAnnotationType;
            if (chosen != null) return chosen;
            var all = new FilteredElementCollector(doc).OfClass(typeof(MultiReferenceAnnotationType))
                .Cast<MultiReferenceAnnotationType>().ToList();
            return all.FirstOrDefault(t => t.Name.IndexOf("align", StringComparison.OrdinalIgnoreCase) >= 0
                                        || t.Name.IndexOf("aline", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? all.FirstOrDefault();
        }

        // Thanh đai: vòng phẳng nằm ngang (độ cao đường tâm gần như không đổi). Thép dọc thì ngược lại.
        private static bool IsStirrupBar(Rebar bar)
        {
            try
            {
                var curves = bar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                if (curves.Count == 0) return false;
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (var c in curves)
                    foreach (var p in new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
                    { if (p.Z < lo) lo = p.Z; if (p.Z > hi) hi = p.Z; }
                return hi - lo < V3Units.Ft(50);
            }
            catch { return false; }
        }

        // Trả về các Rebar đã được phủ bởi Multi-Rebar Annotation (kể cả set giống nhau ở mặt đối diện).
        // Thanh nào chưa được phủ sẽ được tag riêng bằng "Tag thép dọc" của mặt cắt ngang (nếu đã chọn).
        private HashSet<ElementId> AnnotateMultiRebar(Document doc, V3SegmentWork work, ViewSection view, List<Rebar> bars)
        {
            var covered = new HashSet<ElementId>();
            var type = FindMultiRebarType(doc);
            if (type == null) return covered;   // project chưa có loại Multi-Rebar Annotation: dùng Tag thép dọc thay thế
            var g = work.Geometry;
            double s = view.Scale, tol = doc.Application.ShortCurveTolerance;
            XYZ colCenter = Project(view, g.At(0, 0, 0));
            XYZ right = view.RightDirection, up = view.UpDirection;
            var done = new HashSet<string>();

            foreach (var bar in bars.OrderBy(b => b.Id.Value))
            {
                if (bar.Quantity < 2) continue;
                IList<Curve> first, last;
                try
                {
                    first = bar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                    last = bar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, bar.NumberOfBarPositions - 1);
                }
                catch { continue; }
                if (first.Count == 0 || last.Count == 0) continue;

                // Vị trí thanh đầu/cuối của set chiếu lên mặt phẳng view. Đai (rải theo phương dọc cột) chiếu ra ~0 nên bị loại.
                XYZ a = Project(view, first[0].GetEndPoint(0)), b = Project(view, last[0].GetEndPoint(0));
                XYZ d = b - a;
                if (d.GetLength() < tol) continue;
                d = d.Normalize();
                XYZ mid = (a + b) / 2;
                bool alongUp = Math.Abs(d.DotProduct(up)) > Math.Abs(d.DotProduct(right));

                // Các set giống nhau ở hai mặt đối diện chỉ ghi một lần.
                var type0 = doc.GetElement(bar.GetTypeId()) as RebarBarType;
                string key = (alongUp ? "U" : "R") + "|" + bar.Quantity + "|" +
                    (type0 == null ? 0 : Math.Round(V3Units.Mm(type0.BarNominalDiameter))) + "|" +
                    Math.Round(V3Units.Mm(b.DistanceTo(a)));
                if (!done.Add(key)) { covered.Add(bar.Id); continue; }

                // Dim luôn nằm bên trái cột. Set nằm ngang (không thể Dim ở bên trái) thì đặt phía dưới cột.
                XYZ n, origin;
                double along = (mid - colCenter).DotProduct(alongUp ? up : right);
                if (alongUp)
                {
                    n = -right;
                    origin = colCenter + up * along + n * (g.Width / 2 + V3Units.Ft(8 * s));
                }
                else
                {
                    n = -up;
                    origin = colCenter + right * along + n * (g.Depth / 2 + V3Units.Ft(8 * s));
                }

                try
                {
                    var options = new MultiReferenceAnnotationOptions(type);
                    options.SetElementsToDimension(new List<ElementId> { bar.Id });
                    options.DimensionLineOrigin = origin;
                    options.DimensionLineDirection = d;
                    options.DimensionPlaneNormal = view.ViewDirection;
                    options.TagHeadPosition = origin + n * V3Units.Ft(10 * s);
                    options.TagHasLeader = false;
                    MultiReferenceAnnotation.Create(doc, view.Id, options);
                    covered.Add(bar.Id);
                }
                catch { /* thanh này sẽ được tag riêng */ }
            }
            return covered;
        }
    }
}
