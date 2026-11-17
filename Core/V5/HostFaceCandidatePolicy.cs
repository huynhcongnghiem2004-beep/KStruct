using System;
using System.Collections.Generic;
namespace SDK_HD.KStruct.Core.V5
{
    /// <summary>Labels orientations of planar surface candidates. Does not certify
    /// that any face belongs to concrete or may be used as a cover offset.</summary>
    internal static class HostFaceCandidatePolicy
    {
        internal static string Orientation(HostCoordinateFrame frame,
            HostVector3 worldNormal,double cosineThreshold=0.985)
        {
            if(frame==null||!worldNormal.TryUnit(out var n)||
               !AuditV2Policy.IsFinite(cosineThreshold)||
               cosineThreshold<=0||cosineThreshold>1)return "unverified";
            double[] local=frame.LocalDirection(n);
            int axis=0;
            for(int i=1;i<3;i++)
                if(Math.Abs(local[i])>Math.Abs(local[axis]))axis=i;
            if(Math.Abs(local[axis])<cosineThreshold)return "oblique-candidate";
            string name=axis==0?"X":axis==1?"Y":"Z";
            return (local[axis]>=0?"+":"-")+name+"-face-candidate";
        }
        internal static bool ValidFaceArea(double areaSquareFeet) =>
            AuditV2Policy.IsFinite(areaSquareFeet)&&areaSquareFeet>1e-12;
    }
}
