using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    // Hình khai triển thép dọc (nét đứt) bên phải mặt cắt dọc, như bản vẽ mẫu C1_MCD1:
    // mỗi dạng thanh khác nhau một hình, có kích thước từng đoạn và Rebar Tag của thanh đại diện
    // (mác, số lượng, đường kính, chiều dài do family tag quyết định). Dữ liệu lấy từ thép thật trong mô hình
    // (Partition KSTR_V3_<id cột>), là dữ liệu tại thời điểm tạo bản vẽ.
    internal sealed partial class V3DrawingBuilder
    {
        private sealed class BarShape
        {
            public List<XYZ> Pts = new List<XYZ>();   // X = độ lệch ngang (feet), Z = cao độ tuyệt đối (feet)
            public double DiameterMm, LengthMm;
            public int Count, Mark;
            public Rebar Bar;                           // thanh đại diện để gắn Rebar Tag
            public double LateralMin, LateralMax;
            public double MinZ { get { return Pts.Min(p => p.Z); } }
            public double MaxZ { get { return Pts.Max(p => p.Z); } }
        }

        private List<BarShape> CollectBarShapes(Document doc, IList<V3SegmentWork> works)
        {
            var partitions = new HashSet<string>(works.Select(w => V3RebarBuilder.Partition(w.Column.Id)));
            var map = new Dictionary<string, BarShape>();
            double minExtent = V3Units.Ft(300);   // loại đai / đai phụ (không có phương đứng)
            foreach (var r in new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>())
            {
                var part = r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                if (part == null || !partitions.Contains(part.AsString())) continue;

                IList<Curve> curves;
                try { curves = r.GetCenterlineCurves(false, true, true, r.IsRebarFreeForm() ? MultiplanarOption.IncludeAllMultiplanarCurves : MultiplanarOption.IncludeOnlyPlanarCurves, 0); }
                catch { continue; }
                if (curves == null || curves.Count == 0) continue;

                // Đường gấp khúc theo đường tâm (bỏ móc, bỏ bán kính uốn).
                var pts = new List<XYZ> { curves[0].GetEndPoint(0) };
                foreach (var c in curves)
                {
                    XYZ a = c.GetEndPoint(0), b = c.GetEndPoint(1), last = pts[pts.Count - 1];
                    pts.Add(a.DistanceTo(last) <= b.DistanceTo(last) ? b : a);
                }
                if (pts.Max(p => p.Z) - pts.Min(p => p.Z) < minExtent) continue;

                // Hướng uốn ngang: điểm xa nhất theo phương ngang so với điểm đầu.
                XYZ p0 = pts[0], dir = null;
                double far = 1e-4;
                foreach (var p in pts)
                {
                    var h = new XYZ(p.X - p0.X, p.Y - p0.Y, 0);
                    if (h.GetLength() > far) { far = h.GetLength(); dir = h.Normalize(); }
                }
                var local = pts.Select(p => new XYZ(dir == null ? 0 : (p - p0).DotProduct(dir), 0, p.Z)).ToList();

                var type = doc.GetElement(r.GetTypeId()) as RebarBarType;
                double dia = type == null ? 0 : Math.Round(V3Units.Mm(type.BarNominalDiameter));
                var lengthParameter = r.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH);
                double length = Math.Round(V3Units.Mm(lengthParameter != null && lengthParameter.HasValue
                    ? lengthParameter.AsDouble() : curves.Sum(c => c.Length)));
                string key = dia.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(";",
                    local.Select(p => Math.Round(V3Units.Mm(p.X)).ToString(CultureInfo.InvariantCulture) + "," +
                                      Math.Round(V3Units.Mm(p.Z)).ToString(CultureInfo.InvariantCulture)));
                BarShape shape;
                if (!map.TryGetValue(key, out shape))
                {
                    shape = new BarShape { Bar = r, Pts = local, DiameterMm = dia, LengthMm = length,
                        LateralMin = local.Min(p => p.X), LateralMax = local.Max(p => p.X) };
                    map[key] = shape;
                }
                shape.Count += Math.Max(1, r.Quantity);
            }
            var list = map.Values.OrderBy(s => s.MinZ).ThenBy(s => s.LengthMm).ToList();
            for (int i = 0; i < list.Count; i++) list[i].Mark = i + 1;
            return list;
        }

        private static GraphicsStyle DashedLineStyle(Document doc)
        {
            Category lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (lines == null) return null;
            foreach (Category sub in lines.SubCategories)
            {
                string n = sub.Name.ToLowerInvariant();
                if (n.Contains("dash") || n.Contains("hidden") || n.Contains("đứt"))
                    return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
            return null;
        }

        private ElementId BarShapeTextType(Document doc)
        {
            ElementId id = Id("LongTextType");
            if (id != ElementId.InvalidElementId && doc.GetElement(id) is TextNoteType) return id;
            return new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).FirstElementId();
        }

        internal void DrawBarShapes(Document doc, IList<V3SegmentWork> works, ViewSection view, V3Geometry overview)
        {
            // Dùng chung công tắc "Hiện giá trị" của mặt cắt dọc (đã kiểm tra Text Type ở Validate).
            if (!Enabled || !B("LongShowValue")) return;
            var shapes = CollectBarShapes(doc, works);
            if (shapes.Count == 0) return;

            double s = view.Scale;                    // kích thước giấy (mm) * tỷ lệ = kích thước mô hình
            double tol = doc.Application.ShortCurveTolerance;
            XYZ basePt = Project(view, overview.At(0, 0, 0));
            XYZ right = view.RightDirection;
            ElementId textType = BarShapeTextType(doc);
            ElementId tagType = Id("LongMainTag");
            GraphicsStyle dashed = DashedLineStyle(doc);
            var dimType = doc.GetElement(Id("LongDimension")) as DimensionType;
            var touched = new List<XYZ>();

            Func<double, double, XYZ> P = (x, z) => basePt + right * x + XYZ.BasisZ * z;
            double cursor = overview.Width / 2 + V3Units.Ft(60 * s);

            foreach (var sh in shapes)
            {
                double xs = cursor - sh.LateralMin;   // đưa mép trái hình về đúng vị trí con trỏ
                // 1) Đường gấp khúc nét đứt.
                for (int i = 0; i + 1 < sh.Pts.Count; i++)
                {
                    XYZ a = P(xs + sh.Pts[i].X, sh.Pts[i].Z), b = P(xs + sh.Pts[i + 1].X, sh.Pts[i + 1].Z);
                    if (a.DistanceTo(b) < tol) continue;
                    var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(a, b));
                    if (dashed != null) dc.LineStyle = dashed;
                    touched.Add(a); touched.Add(b);
                }

                // 2) Kích thước từng đoạn theo cao độ (các vạch ngang bên trái hình).
                var zs = sh.Pts.Select(p => p.Z).OrderBy(z => z).ToList();
                var breaks = new List<double>();
                foreach (double z in zs) if (breaks.Count == 0 || z - breaks[breaks.Count - 1] > tol) breaks.Add(z);
                double xRef = xs + sh.LateralMin;                 // mép trái hình khai triển
                double xTick = xRef - V3Units.Ft(6 * s), tick = V3Units.Ft(1.5 * s);   // đường Dim nằm bên trái hình
                var refs = new List<Reference>();
                foreach (double z in breaks)
                {
                    var tc = doc.Create.NewDetailCurve(view, Line.CreateBound(P(xRef - tick, z), P(xRef, z)));
                    refs.Add(tc.GeometryCurve.Reference);
                    touched.Add(P(xTick - tick, z));   // chân Dim chạy từ đường Dim vào sát hình
                }
                doc.Regenerate();
                for (int i = 0; i + 1 < breaks.Count; i++)
                {
                    if (dimType != null)
                    {
                        var ra = new ReferenceArray(); ra.Append(refs[i]); ra.Append(refs[i + 1]);
                        doc.Create.NewDimension(view, Line.CreateBound(P(xTick, breaks[i]), P(xTick, breaks[i + 1])), ra, dimType);
                    }
                    else
                    {
                        string t = Math.Round(V3Units.Mm(breaks[i + 1] - breaks[i])).ToString(CultureInfo.InvariantCulture);
                        TextNote.Create(doc, view.Id, P(xTick - V3Units.Ft(9 * s), (breaks[i] + breaks[i + 1]) / 2), t, textType);
                    }
                }

                // 3) Rebar Tag (loại đã chọn cho mặt cắt dọc) đặt cạnh hình, không leader.
                //    Mác, số lượng, đường kính, chiều dài do family tag quyết định.
                double bestLen = 0, midZ = (sh.MinZ + sh.MaxZ) / 2;
                for (int i = 0; i + 1 < sh.Pts.Count; i++)
                {
                    double len = Math.Abs(sh.Pts[i + 1].Z - sh.Pts[i].Z);
                    if (len > bestLen && Math.Abs(sh.Pts[i + 1].X - sh.Pts[i].X) < 1e-6) { bestLen = len; midZ = (sh.Pts[i].Z + sh.Pts[i + 1].Z) / 2; }
                }
                XYZ head = P(xs + sh.LateralMax + V3Units.Ft(8 * s), midZ);
                var tagSymbol = doc.GetElement(tagType) as FamilySymbol;
                if (tagSymbol != null)
                {
                    if (!tagSymbol.IsActive) { tagSymbol.Activate(); doc.Regenerate(); }
                    var sub = sh.Bar.GetSubelements().FirstOrDefault();
                    var reference = sub == null ? new Reference(sh.Bar) : sub.GetReference();
                    IndependentTag.Create(doc, tagType, view.Id, reference, false, TagOrientation.Horizontal, head);
                }
                touched.Add(head); touched.Add(head + right * V3Units.Ft(14 * s));

                cursor = xs + sh.LateralMax + V3Units.Ft(22 * s);
            }

            // Mở rộng crop để mọi chi tiết nằm trong view (cùng cách ZoneDetail đang làm).
            var box = view.CropBox; Transform inv = box.Transform.Inverse;
            var local2 = touched.Select(p => inv.OfPoint(p)).ToList();
            double pad = V3Units.Ft(10 * s);
            box.Min = new XYZ(Math.Min(box.Min.X, local2.Min(p => p.X) - pad), Math.Min(box.Min.Y, local2.Min(p => p.Y) - pad), box.Min.Z);
            box.Max = new XYZ(Math.Max(box.Max.X, local2.Max(p => p.X) + pad), Math.Max(box.Max.Y, local2.Max(p => p.Y) + pad), box.Max.Z);
            view.CropBox = box;
        }
    }
}
