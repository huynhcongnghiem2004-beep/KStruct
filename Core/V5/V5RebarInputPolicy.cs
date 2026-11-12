using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SDK_HD.KStruct.Core.V5
{
    internal enum V5StructuralModule { Beam, Column, Footing }

    // UI validation only, not engineering design / TCVN compliance approval.
    internal sealed class V5RebarInput
    {
        internal V5StructuralModule Module;
        internal double WidthMm, HeightMm, CoverMm, MainBarDiameterMm, TieDiameterMm, TieSpacingMm;
        internal int MainBarCount;
        internal string HostUniqueId;
    }

    internal sealed class V5InputReview
    {
        internal readonly List<string> Errors=new List<string>();
        internal readonly List<string> Warnings=new List<string>();
        internal double InnerWidthMm, InnerHeightMm, NetBarClearWidthMm;
        internal bool IsValid=>Errors.Count==0;
        internal bool CanCreateRebar=>false; // hard release gate until real Solid/cover/joint verification
    }

    internal static class V5RebarInputPolicy
    {
        internal static bool TryMillimeters(string text,out double value)
        {
            value=0;
            if(string.IsNullOrWhiteSpace(text))return false;
            // No grouping separators: "1,500" could ambiguously mean 1.5 or 1500.
            string normalized=text.Trim().Replace(',', '.');
            if(normalized.Count(c=>c=='.')>1)return false;
            if(!double.TryParse(normalized,NumberStyles.AllowLeadingSign|
                NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value))return false;
            return AuditV2Policy.IsFinite(value);
        }

        internal static V5InputReview Review(V5RebarInput input,IEnumerable<double> availableBarDiametersMm)
        {
            var result=new V5InputReview();
            if(input==null){result.Errors.Add("Thiếu dữ liệu cấu kiện.");return result;}
            if(!Enum.IsDefined(typeof(V5StructuralModule),input.Module))
                result.Errors.Add("Module không hợp lệ.");
            if(!Range(input.WidthMm,50,10000))result.Errors.Add("Chiều rộng phải từ 50 đến 10000 mm.");
            if(!Range(input.HeightMm,50,10000))result.Errors.Add("Chiều cao phải từ 50 đến 10000 mm.");
            if(!Range(input.CoverMm,10,200))result.Errors.Add("Lớp bảo vệ nhập từ 10 đến 200 mm để kiểm tra hình học; không tự xác nhận đạt tiêu chuẩn.");
            if(!Range(input.MainBarDiameterMm,4,80))result.Errors.Add("Đường kính thép chủ không hợp lệ.");
            if(!Range(input.TieDiameterMm,4,32))result.Errors.Add("Đường kính đai không hợp lệ.");
            if(!Range(input.TieSpacingMm,20,600))result.Errors.Add("Khoảng cách đai không hợp lệ.");
            if(input.MainBarCount<2||input.MainBarCount>64)result.Errors.Add("Số thép chủ nhập từ 2 đến 64.");
            if(string.IsNullOrWhiteSpace(input.HostUniqueId))
                result.Errors.Add("Phải chọn một cấu kiện Host trước khi có thể nghiệm thu bản nhập.");
            var types=(availableBarDiametersMm??Enumerable.Empty<double>())
                .Where(AuditV2Policy.IsFinite).ToArray();
            if(types.Length==0)
                result.Errors.Add("Dự án chưa có RebarBarType hợp lệ để đối chiếu.");
            else
            {
                if(AuditV2Policy.IsFinite(input.MainBarDiameterMm) &&
                   !types.Any(d=>Math.Abs(d-input.MainBarDiameterMm)<0.15))
                    result.Errors.Add("Đường kính thép chủ chưa có trong RebarBarType của dự án.");
                if(AuditV2Policy.IsFinite(input.TieDiameterMm) &&
                   !types.Any(d=>Math.Abs(d-input.TieDiameterMm)<0.15))
                    result.Errors.Add("Đường kính đai chưa có trong RebarBarType của dự án.");
            }
            if(Range(input.WidthMm,50,10000)&&Range(input.HeightMm,50,10000)&&
               Range(input.CoverMm,10,200)&&Range(input.TieDiameterMm,4,32)&&
               Range(input.MainBarDiameterMm,4,80))
            {
                double offset=input.CoverMm+input.TieDiameterMm+input.MainBarDiameterMm/2;
                result.InnerWidthMm=input.WidthMm-2*offset;
                result.InnerHeightMm=input.HeightMm-2*offset;
                result.NetBarClearWidthMm=input.WidthMm-2*input.CoverMm-
                    2*input.TieDiameterMm-input.MainBarCount*input.MainBarDiameterMm;
                if(result.InnerWidthMm<=0||result.InnerHeightMm<=0)
                    result.Errors.Add("Không đủ tiết diện cho tim thanh thép sau khi trừ lớp bảo vệ và đường kính.");
                if(result.NetBarClearWidthMm<0)
                    result.Warnings.Add("Không đủ chỗ xếp tất cả thép chủ trên một hàng theo chiều rộng.");
            }
            result.Warnings.Add("Đây là kiểm tra thông số nhập, không phải tính toán kết cấu, TCVN hay phê duyệt đặt thép.");
            return result;
        }
        private static bool Range(double value,double min,double max)=>
            AuditV2Policy.IsFinite(value)&&value>=min&&value<=max;
    }
}
