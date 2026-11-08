using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    // Break Line của mặt cắt dọc dùng để che các cấu kiện khác giao với cột:
    //  - Dầm: cắt ở mỗi phía dầm kéo ra khỏi cột (dầm hai bên cắt hai bên, dầm một bên cắt một bên).
    //  - Móng: cắt phía dưới móng đỡ cột (che phần cọc/móng bên dưới).
    internal sealed partial class V3DrawingBuilder
    {
        private const double BeamBreakOffsetMm = 600;   // vị trí cắt dầm tính từ mép cột
        // true: phần được che của family Break Line nằm bên PHẢI hướng vẽ đường. Nếu thử thấy che ngược phía, đổi thành false.
        private const bool BreakMaskOnRight = true;

        private void BreakLines(Document doc, IList<V3SegmentWork> works, ViewSection view)
        {
            ElementId id = Id("LongBreakLine");
            if (id == ElementId.InvalidElementId) return;
            var symbol = doc.GetElement(id) as FamilySymbol;
            if (symbol == null) throw new InvalidOperationException("Break Line không phải Detail Item.");
            var placement = symbol.Family.FamilyPlacementType;
            if (placement != FamilyPlacementType.CurveBasedDetail && placement != FamilyPlacementType.ViewBased)
                throw new InvalidOperationException("Break Line phải là Detail Item đặt theo đường hoặc theo điểm.");
            if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

            XYZ right = view.RightDirection;
            double tol = V3Units.Ft(50), pad = V3Units.Ft(100), off = V3Units.Ft(BeamBreakOffsetMm);
            XYZ baseP = Project(view, works[0].Geometry.At(0, 0, 0));
            double baseR = baseP.DotProduct(right);
            Func<double, double, XYZ> P = (r, z) => baseP + right * (r - baseR) + XYZ.BasisZ * z;
            var touched = new List<XYZ>();
            var done = new HashSet<string>();

            // Khoảng chiếu của phần tử lên phương ngang và đứng của view: {minR, maxR, minZ, maxZ}.
            Func<Element, double[]> extent = e =>
            {
                BoundingBoxXYZ bb = e.get_BoundingBox(view);
                if (bb == null) return null;
                double r0 = double.MaxValue, r1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
                foreach (double x in new[] { bb.Min.X, bb.Max.X })
                    foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                        foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                        {
                            XYZ w = bb.Transform.OfPoint(new XYZ(x, y, z));
                            double r = w.DotProduct(right);
                            r0 = Math.Min(r0, r); r1 = Math.Max(r1, r);
                            z0 = Math.Min(z0, w.Z); z1 = Math.Max(z1, w.Z);
                        }
                return new[] { r0, r1, z0, z1 };
            };
            Func<V3SegmentWork, double[]> columnSpan = w =>
            {
                double c = Project(view, w.Geometry.At(0, 0, 0)).DotProduct(right);
                return new[] { c - w.Geometry.Width / 2, c + w.Geometry.Width / 2 };
            };

            // 1) Dầm giao với cột.
            foreach (Element beam in new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType())
            {
                double[] ext = extent(beam);
                if (ext == null) continue;
                foreach (var w in works)
                {
                    double[] col = columnSpan(w);
                    bool touchesZ = ext[3] >= w.Geometry.Bottom - tol && ext[2] <= w.Geometry.Top + tol;
                    bool touchesR = ext[1] >= col[0] - tol && ext[0] <= col[1] + tol;
                    if (!touchesZ || !touchesR) continue;

                    double z0 = ext[2] - pad, z1 = ext[3] + pad;
                    if (col[0] - ext[0] > off + tol && done.Add("L|" + Math.Round(V3Units.Mm(z0)) + "|" + Math.Round(V3Units.Mm(z1))))
                    {   // dầm kéo ra bên trái: che phía trái đường cắt
                        XYZ a = P(col[0] - off, z1), b = P(col[0] - off, z0);   // hướng xuống: phía phải của hướng vẽ = bên trái màn hình
                        PlaceBreak(doc, view, symbol, BreakMaskOnRight ? a : b, BreakMaskOnRight ? b : a, true, touched);
                    }
                    if (ext[1] - col[1] > off + tol && done.Add("R|" + Math.Round(V3Units.Mm(z0)) + "|" + Math.Round(V3Units.Mm(z1))))
                    {   // dầm kéo ra bên phải: che phía phải đường cắt
                        XYZ a = P(col[1] + off, z0), b = P(col[1] + off, z1);   // hướng lên
                        PlaceBreak(doc, view, symbol, BreakMaskOnRight ? a : b, BreakMaskOnRight ? b : a, true, touched);
                    }
                    break;
                }
            }

            // 2) Móng đỡ cột: cắt phía dưới đáy móng (móng chứa cao độ chân cột; cọc nằm dưới nên không bị lấy).
            V3SegmentWork lowest = works.OrderBy(w => w.Geometry.Bottom).First();
            double[] lowCol = columnSpan(lowest);
            double? cutZ = null;
            foreach (Element foundation in new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation).WhereElementIsNotElementType())
            {
                double[] ext = extent(foundation);
                if (ext == null) continue;
                bool holdsColumnBase = lowest.Geometry.Bottom >= ext[2] - tol && lowest.Geometry.Bottom <= ext[3] + tol;
                bool touchesR = ext[1] >= lowCol[0] - tol && ext[0] <= lowCol[1] + tol;
                if (holdsColumnBase && touchesR) cutZ = cutZ.HasValue ? Math.Min(cutZ.Value, ext[2]) : ext[2];
            }
            if (cutZ.HasValue)
            {
                XYZ a = P(lowCol[0] - off, cutZ.Value), b = P(lowCol[1] + off, cutZ.Value);   // hướng sang phải: phía phải của hướng vẽ = phía dưới
                PlaceBreak(doc, view, symbol, BreakMaskOnRight ? a : b, BreakMaskOnRight ? b : a, false, touched);
            }

            if (touched.Count == 0) return;
            // Mở rộng crop để chứa phần dầm còn lại, móng và đường cắt.
            var box = view.CropBox; Transform inv = box.Transform.Inverse;
            var local = touched.Select(p => inv.OfPoint(p)).ToList();
            box.Min = new XYZ(Math.Min(box.Min.X, local.Min(p => p.X) - pad), Math.Min(box.Min.Y, local.Min(p => p.Y) - pad), box.Min.Z);
            box.Max = new XYZ(Math.Max(box.Max.X, local.Max(p => p.X) + pad), Math.Max(box.Max.Y, local.Max(p => p.Y) + pad), box.Max.Z);
            view.CropBox = box;
        }

        private void PlaceBreak(Document doc, ViewSection view, FamilySymbol symbol, XYZ a, XYZ b, bool vertical, List<XYZ> touched)
        {
            if (symbol.Family.FamilyPlacementType == FamilyPlacementType.CurveBasedDetail)
                doc.Create.NewFamilyInstance(Line.CreateBound(a, b), symbol, view);
            else
            {   // Break Line đặt theo điểm: đặt tại giữa đường cắt, xoay 90° khi cắt đứng (giả định family mặc định nằm ngang)
                XYZ mid = (a + b) / 2;
                var instance = doc.Create.NewFamilyInstance(mid, symbol, view);
                if (vertical)
                    ElementTransformUtils.RotateElement(doc, instance.Id, Line.CreateBound(mid, mid + view.ViewDirection), Math.PI / 2);
            }
            touched.Add(a); touched.Add(b);
        }
    }
}
