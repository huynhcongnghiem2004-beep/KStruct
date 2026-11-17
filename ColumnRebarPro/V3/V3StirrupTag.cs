using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    internal sealed partial class V3DrawingBuilder
    {
        private const double StirrupTagGapMm = 200;   // khoảng cách từ mép cột đến mép gần nhất của tag

        // Mặt cắt dọc: tag thép đai luôn nằm bên trái cột, không leader, xoay 90° theo chiều dọc cột,
        // mép tag cách mép trái cột 200 mm, tâm tag nằm giữa đoạn đai (giữa vị trí đai đầu và đai cuối của set).
        private void TagStirrupLeft(Document doc, V3SegmentWork work, ViewSection view, Rebar bar,
                                     FamilySymbol tagSymbol, HashSet<string> spans)
        {
            IList<Curve> first, last;
            try
            {
                first = bar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                last = bar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves,
                    Math.Max(0, bar.NumberOfBarPositions - 1));
            }
            catch { return; }
            if (first.Count == 0 || last.Count == 0) return;

            double z0 = first[0].GetEndPoint(0).Z, z1 = last[0].GetEndPoint(0).Z;
            double midZ = (z0 + z1) / 2;
            // Các set đai cùng đoạn (ví dụ đai phụ trùng đoạn đai chính) chỉ tag một lần.
            string key = Math.Round(V3Units.Mm(Math.Min(z0, z1)) / 10).ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
                         Math.Round(V3Units.Mm(Math.Max(z0, z1)) / 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!spans.Add(key)) return;

            var g = work.Geometry;
            XYZ right = view.RightDirection;
            XYZ faceLeft = Project(view, g.At(0, 0, 0)) - right * (g.Width / 2);   // mép trái cột
            double targetRight = faceLeft.DotProduct(right) - V3Units.Ft(StirrupTagGapMm);
            XYZ head = faceLeft - right * V3Units.Ft(StirrupTagGapMm) + XYZ.BasisZ * midZ;

            var sub = bar.GetSubelements().FirstOrDefault();
            var reference = sub == null ? new Reference(bar) : sub.GetReference();
            var tag = IndependentTag.Create(doc, tagSymbol.Id, view.Id, reference, false, TagOrientation.Vertical, head);
            doc.Regenerate();

            // Căn lại theo khung bao thật của tag: mép phải của tag cách cột 200 mm, tâm đứng đúng giữa đoạn đai.
            BoundingBoxXYZ bb = tag.get_BoundingBox(view);
            if (bb == null) return;
            double maxR = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (double x in new[] { bb.Min.X, bb.Max.X })
                foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                    foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                    {
                        XYZ w = bb.Transform.OfPoint(new XYZ(x, y, z));
                        maxR = Math.Max(maxR, w.DotProduct(right));
                        minZ = Math.Min(minZ, w.Z); maxZ = Math.Max(maxZ, w.Z);
                    }
            tag.TagHeadPosition = tag.TagHeadPosition + right * (targetRight - maxR) + XYZ.BasisZ * (midZ - (minZ + maxZ) / 2);
        }
    }
}
