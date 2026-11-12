using System;
using System.Collections.Generic;

namespace SDK_HD.KStruct.Core.V5
{
    // Evidence state for the staged V5 drawing engine. A detected bounding-box
    // intersection or geometric face candidate is NOT a verified engineering input.
    internal sealed class V5ExecutionEvidence
    {
        internal bool HostSolidIsConcrete;
        internal bool PhysicalCoverFacesVerified;
        internal bool JointsAndSupportsVerified;
        internal bool AnchorsAndLapsApproved;
        internal bool OwnershipAndRollbackVerified;
        internal bool ViewsTagsDimensionsVerified;
    }

    internal sealed class V5ExecutionReview
    {
        internal readonly List<string> Blockers = new List<string>();
        internal bool IsReady => Blockers.Count == 0;
    }

    internal static class V5ExecutionGatePolicy
    {
        internal static V5ExecutionReview Review(V5ExecutionEvidence evidence)
        {
            var review = new V5ExecutionReview();
            if (evidence == null)
            {
                review.Blockers.Add("Thiếu bằng chứng kiểm tra kết cấu V5.");
                return review;
            }
            if (!evidence.HostSolidIsConcrete)
                review.Blockers.Add("Chưa chứng minh được Solid là bê tông thuộc đúng Host.");
            if (!evidence.PhysicalCoverFacesVerified)
                review.Blockers.Add("Chưa chứng minh mặt ngoài thực và lớp bảo vệ cho mọi thanh.");
            if (!evidence.JointsAndSupportsVerified)
                review.Blockers.Add("Chưa xác minh nút khung, gối đỡ và vùng không bố trí đai.");
            if (!evidence.AnchorsAndLapsApproved)
                review.Blockers.Add("Chưa duyệt neo, móc, nối chồng và chuyển tiết diện.");
            if (!evidence.OwnershipAndRollbackVerified)
                review.Blockers.Add("Chưa xác minh quyền sở hữu phần tử, Transaction và rollback.");
            if (!evidence.ViewsTagsDimensionsVerified)
                review.Blockers.Add("Chưa kiểm tra mặt cắt, tag, kích thước và Sheet 2D.");
            return review;
        }
    }
}
