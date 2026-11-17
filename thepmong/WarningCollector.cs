using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.FootingRebar
{
    /// <summary>
    /// Ghi lại các cảnh báo Revit phát sinh lúc commit (ví dụ "Rebar is placed completely
    /// outside of its host") để đưa vào báo cáo thay vì bật hộp thoại riêng lẻ.
    /// </summary>
    public sealed class WarningCollector : IFailuresPreprocessor
    {
        public List<string> Warnings { get; } = new List<string>();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (FailureMessageAccessor m in accessor.GetFailureMessages())
            {
                if (m.GetSeverity() != FailureSeverity.Warning) continue;
                Warnings.Add(m.GetDescriptionText());
                accessor.DeleteWarning(m); // chỉ ẩn hộp thoại; cảnh báo vẫn nằm trong báo cáo của add-in
            }
            return FailureProcessingResult.Continue;
        }
    }
}
