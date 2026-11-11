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
        internal static Tuple<double, double> Read(Document doc, FamilyInstance footing,
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
            List<double> diameters = new FilteredElementCollector(doc).OfClass(typeof(Rebar))
                .Cast<Rebar>().Where(bar => bar.GetHostId().Value == footing.Id.Value &&
                    FootingDrawingVisibility.InLayer(bar, hostBox, top))
                .Select(bar => doc.GetElement(bar.GetTypeId())?.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER))
                .Where(parameter => parameter != null && parameter.HasValue &&
                    parameter.StorageType == StorageType.Double && parameter.AsDouble() > 0)
                .Select(parameter => UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Millimeters))
                .Distinct().ToList();
            if (diameters.Count == 0)
                throw new InvalidOperationException("Không tìm thấy đường kính Rebar lớp " +
                    (top ? "TOP" : "BOT") + " trên móng " + footing.Id.Value +
                    "; không thể căn theo tim thép và lớp bảo vệ.");
            double first = diameters[0];
            if (diameters.Any(d => Math.Abs(d - first) > 0.1))
                throw new InvalidOperationException("Móng " + footing.Id.Value +
                    " có nhiều đường kính khác nhau trong lớp " + (top ? "TOP" : "BOT") +
                    ". Ký hiệu Family chung L/W chưa thể căn chính xác mọi nhóm thép; " +
                    "hãy dùng chế độ chú thích/thép thực hoặc một Family nhiều nhóm.");
            return Tuple.Create(coverMm, first);
        }
    }
}
