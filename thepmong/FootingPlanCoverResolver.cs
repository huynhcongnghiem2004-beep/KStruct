using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // Resolve cover from the real host and the line-center diameter from hosted Rebar.
    internal static class FootingPlanCoverResolver
    {
        internal static Tuple<double, double, double> Read(Document doc, FamilyInstance footing,
            FootingDrawingProfile profile, bool top, IList<string> warnings)
        {
            double coverMm = profile.PlanDetailCoverMm;
            if (profile.PlanDetailReadHostCover)
            {
                Parameter coverParam = footing.get_Parameter(BuiltInParameter.CLEAR_COVER_OTHER);
                if (coverParam != null && coverParam.StorageType == StorageType.ElementId)
                {
                    RebarCoverType coverType = doc.GetElement(coverParam.AsElementId()) as RebarCoverType;
                    if (coverType != null && coverType.CoverDistance > 0)
                        coverMm = UnitUtils.ConvertFromInternalUnits(coverType.CoverDistance, UnitTypeId.Millimeters);
                    else
                        warnings?.Add("Móng " + footing.Id.Value +
                            " không có Rebar Cover - Other Faces hợp lệ; sử dụng C đã nhập trong Profile.");
                }
                else warnings?.Add("Móng " + footing.Id.Value +
                    " thiếu Rebar Cover - Other Faces; sử dụng C trong Profile.");
            }
            if (double.IsNaN(coverMm) || double.IsInfinity(coverMm) || coverMm <= 0)
                throw new InvalidOperationException("Chưa có lớp bảo vệ mặt bên của móng. " +
                    "Hãy thiết lập Rebar Cover - Other Faces hoặc nhập C > 0 mm trong KStruct.");
            BoundingBoxXYZ hostBox = footing.get_BoundingBox(null);
            Transform axes = FootingDrawingGeometry.Axes(footing);
            var byAxis = new Dictionary<string, List<double>>
            {
                { "X", new List<double>() }, { "Y", new List<double>() }
            };
            var bars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(bar => bar.GetHostId().Value == footing.Id.Value &&
                    FootingDrawingVisibility.InLayer(bar, hostBox, top))
                .OrderBy(bar => bar.Id.Value).ToList();
            foreach (Rebar bar in bars)
            {
                Parameter size = doc.GetElement(bar.GetTypeId())?.get_Parameter(
                    BuiltInParameter.REBAR_BAR_DIAMETER);
                if (size == null || size.StorageType != StorageType.Double || !size.HasValue ||
                    size.AsDouble() <= 0)
                    throw new InvalidOperationException("Rebar " + bar.Id.Value +
                        " không có đường kính thực hợp lệ.");
                string direction = DirectionOfBar(bar, axes);
                byAxis[direction].Add(UnitUtils.ConvertFromInternalUnits(
                    size.AsDouble(), UnitTypeId.Millimeters));
            }
            double dx = UniqueDiameter(byAxis["X"], "X", top, footing.Id.Value);
            double dy = UniqueDiameter(byAxis["Y"], "Y", top, footing.Id.Value);
            return Tuple.Create(coverMm, dx, dy);
        }

        private static double UniqueDiameter(List<double> sizes, string direction, bool top, long footingId)
        {
            if (sizes.Count == 0)
                throw new InvalidOperationException("Móng " + footingId + " không có Rebar " +
                    direction + " lớp " + (top ? "TOP" : "BOT") +
                    " để căn lớp bảo vệ cho Family thép 2D.");
            double first = sizes[0];
            if (sizes.Any(value => Math.Abs(value - first) > 0.1))
                throw new InvalidOperationException("Móng " + footingId + " có nhiều đường kính " +
                    "trong cùng phương " + direction + " lớp " + (top ? "TOP" : "BOT") +
                    ". Family một đường bao không thể căn chính xác đồng thời; cần Family nhiều nhóm thép.");
            return first;
        }

        private static string DirectionOfBar(Rebar bar, Transform axes)
        {
            int index = Enumerable.Range(0, bar.NumberOfBarPositions)
                .Where(i => bar.DoesBarExistAtPosition(i)).DefaultIfEmpty(-1).First();
            if (index < 0)
                throw new InvalidOperationException("Rebar " + bar.Id.Value +
                    " không có thanh hợp lệ để xác định phương.");
            XYZ longest = XYZ.Zero;
            foreach (Curve curve in bar.GetTransformedCenterlineCurves(false, false, true,
                MultiplanarOption.IncludeAllMultiplanarCurves, index))
            {
                var points = curve.Tessellate();
                for (int i = 1; i < points.Count; i++)
                {
                    XYZ segment = points[i] - points[i - 1];
                    segment = new XYZ(segment.X, segment.Y, 0);
                    if (segment.GetLength() > longest.GetLength()) longest = segment;
                }
            }
            if (longest.GetLength() < 1e-8)
                throw new InvalidOperationException("Rebar " + bar.Id.Value +
                    " không có đoạn thẳng nằm ngang để xác định X/Y.");
            double x = Math.Abs(longest.Normalize().DotProduct(axes.BasisX.Normalize()));
            double y = Math.Abs(longest.Normalize().DotProduct(axes.BasisY.Normalize()));
            if (Math.Abs(x - y) < 0.05)
                throw new InvalidOperationException("Rebar " + bar.Id.Value +
                    " nằm chéo; không thể ánh xạ vào Family 2D X/Y.");
            return x > y ? "X" : "Y";
        }
    }
}
