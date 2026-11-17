using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.KStruct.Core.V5
{
    /// <summary>
    /// Reads Revit's explicit host cover TYPE parameters. These values are not
    /// proof that all visible faces are exposed or that Rebar meets real cover.
    /// </summary>
    internal sealed class V5HostCoverSnapshot
    {
        internal readonly Dictionary<string, double> CoverByFaceMm =
            new Dictionary<string,double>(StringComparer.Ordinal);
        internal readonly List<string> Warnings = new List<string>();
        internal bool IsComplete => CoverByFaceMm.Count==3 && Warnings.Count==0;
        internal bool PlacementApproved => false;
    }

    internal static class V5HostCoverInspector
    {
        internal static V5HostCoverSnapshot Inspect(Element host)
        {
            var result = new V5HostCoverSnapshot();
            if(host==null)
            {
                result.Warnings.Add("Chưa chọn Host để đọc lớp bảo vệ.");
                return result;
            }
            ReadCover(host, BuiltInParameter.CLEAR_COVER_OTHER, "other", result);
            ReadCover(host, BuiltInParameter.CLEAR_COVER_TOP, "top", result);
            ReadCover(host, BuiltInParameter.CLEAR_COVER_BOTTOM, "bottom", result);
            if (result.CoverByFaceMm.Count==0)
                result.Warnings.Add("Host chưa có Rebar Cover Type được đọc thành công.");
            // Revit's face-specific cover may differ, so do not map a single
            // user-entered cover value onto every physical face automatically.
            return result;
        }

        private static void ReadCover(Element host,BuiltInParameter id,
            string role,V5HostCoverSnapshot result)
        {
            try
            {
                Parameter parameter=host.get_Parameter(id);
                if(parameter==null) return; // Face role not supported by this Host.
                if(!parameter.HasValue || parameter.StorageType!=StorageType.ElementId)
                {
                    result.Warnings.Add(role+": giá trị Cover Type không hợp lệ.");
                    return;
                }
                ElementId typeId=parameter.AsElementId();
                if(typeId==null || typeId==ElementId.InvalidElementId)
                {
                    result.Warnings.Add(role+": chưa có Cover Type.");
                    return;
                }
                var coverType=host.Document.GetElement(typeId) as RebarCoverType;
                if(coverType==null)
                {
                    result.Warnings.Add(role+": không đọc được RebarCoverType.");
                    return;
                }
                double coverMm=AuditV2Policy.Mm(coverType.CoverDistance);
                if(!AuditV2Policy.IsFinite(coverMm) || coverMm<=0 || coverMm>500)
                {
                    result.Warnings.Add(role+": lớp bảo vệ ngoài miền kiểm tra.");
                    return;
                }
                result.CoverByFaceMm[role]=coverMm;
            }
            catch(Exception ex)
            {
                result.Warnings.Add(role+": lỗi đọc Cover Type "+ex.GetType().Name);
            }
        }
    }
}
