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

        // Mặt cắt dọc: P/T chọn phía theo View.RightDirection; leader ngang, TOP/MID/BOT chọn cao độ.
        private void TagStirrupByName(Document doc, V3SegmentWork work, ViewSection view, Rebar bar,
                                     FamilySymbol tagSymbol, HashSet<string> spans)
        {
            IList<Curve> first, last;
            try
            {
                var positions = Enumerable.Range(0, bar.NumberOfBarPositions).Where(bar.DoesBarExistAtPosition).ToList();
                if (positions.Count == 0) return;
                first = bar.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, positions.First());
                last = bar.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, positions.Last());
            }
            catch { return; }
            if (first.Count == 0 || last.Count == 0) return;

            double z0 = first[0].GetEndPoint(0).Z, z1 = last[0].GetEndPoint(0).Z;
            double midZ = KStruct.Core.RebarTagPlacement.SelectPosition(tagSymbol.FamilyName, tagSymbol.Name, z0, z1);
            // Các set đai cùng đoạn (ví dụ đai phụ trùng đoạn đai chính) chỉ tag một lần.
            string key = Math.Round(V3Units.Mm(Math.Min(z0, z1)) / 10).ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
                         Math.Round(V3Units.Mm(Math.Max(z0, z1)) / 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!spans.Add(key)) return;

            var g = work.Geometry;
            XYZ right = view.RightDirection;
            V3TagHorizontalSide side = V3TagPlacement.Resolve(tagSymbol.FamilyName, tagSymbol.Name,
                V3TagHorizontalSide.Left);
            double sign = side == V3TagHorizontalSide.Right ? 1 : -1;
            XYZ face = Project(view, g.At(0, 0, 0)) + right * (sign * g.Width / 2);
            XYZ anchor = Project(view, new XYZ(face.X, face.Y, midZ));

            var sub = bar.GetSubelements().FirstOrDefault();
            var reference = sub == null ? new Reference(bar) : sub.GetReference();
            KStruct.Core.RebarTagPlacement.CreateHorizontal(doc, view, tagSymbol, reference, anchor, StirrupTagGapMm, -1);
        }
    }
}
