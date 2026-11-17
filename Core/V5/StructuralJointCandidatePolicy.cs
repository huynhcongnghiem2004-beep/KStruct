using System;
using System.Collections.Generic;
namespace SDK_HD.KStruct.Core.V5
{
    /// <summary>Conservative broad-phase structural proximity checks; AABB overlap
    /// is never proof of physical intersection, support, cover, anchorage or ownership.</summary>
    internal static class StructuralJointCandidatePolicy
    {
        internal static bool ValidBox(HostVector3 min,HostVector3 max) =>
            min.IsFinite && max.IsFinite && min.X<=max.X && min.Y<=max.Y && min.Z<=max.Z;
        internal static bool TrySegmentBox(HostVector3 start,HostVector3 end,
            HostVector3 min,HostVector3 max,double paddingMm,out double entry,out double exit)
        {
            entry=0;exit=0;
            if(!start.IsFinite||!end.IsFinite||!ValidBox(min,max)||
                !AuditV2Policy.IsFinite(paddingMm)||paddingMm<0||
                !(end-start).TryUnit(out _))return false;
            double[] a=start.Components,d=(end-start).Components,
                lows=min.Components,highs=max.Components;
            double t0=0,t1=1;
            for(int axis=0;axis<3;axis++)
            {
                double lower=lows[axis]-paddingMm,upper=highs[axis]+paddingMm;
                if(!AuditV2Policy.IsFinite(lower)||!AuditV2Policy.IsFinite(upper))return false;
                if(Math.Abs(d[axis])<1e-10)
                {
                    if(a[axis]<lower||a[axis]>upper)return false;
                    continue;
                }
                double u=(lower-a[axis])/d[axis],v=(upper-a[axis])/d[axis];
                if(u>v){double t=u;u=v;v=t;}
                t0=Math.Max(t0,u);
                t1=Math.Min(t1,v);
                if(t0>t1)return false;
            }
            entry=t0;exit=t1;
            return true;
        }
        internal static bool TryColumnStackCandidate(
            HostVector3 lowerMin,HostVector3 lowerMax,
            HostVector3 upperMin,HostVector3 upperMax,
            double xyToleranceMm,double verticalToleranceMm,out double xyIntersectionArea)
        {
            xyIntersectionArea=0;
            if(!ValidBox(lowerMin,lowerMax)||!ValidBox(upperMin,upperMax)||
                !AuditV2Policy.IsFinite(xyToleranceMm)||xyToleranceMm<0||
                !AuditV2Policy.IsFinite(verticalToleranceMm)||verticalToleranceMm<0)return false;
            if(Math.Abs(upperMin.Z-lowerMax.Z)>verticalToleranceMm)return false;
            double dx=Math.Min(lowerMax.X,upperMax.X)-Math.Max(lowerMin.X,upperMin.X);
            double dy=Math.Min(lowerMax.Y,upperMax.Y)-Math.Max(lowerMin.Y,upperMin.Y);
            if(dx < -xyToleranceMm || dy < -xyToleranceMm)return false;
            // Actual XY intersection (not tolerance inflation) is reported. Zero
            // overlap may represent merely adjacent geometry and must be reviewed.
            xyIntersectionArea=Math.Max(0,dx)*Math.Max(0,dy);
            return AuditV2Policy.IsFinite(xyIntersectionArea);
        }
    }
}
