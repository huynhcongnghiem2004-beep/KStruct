using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.Core.V5
{
    /// <summary>Revit 2024 read-only adapter. This is diagnostic geometry, not Rebar placement permission.</summary>
    internal static class RevitHostGeometryResolver
    {
        internal static bool TryResolve(Element host,out HostCoordinateFrame frame,out string reason)
        {
            frame=null;
            reason="Host element unavailable.";
            if(!(host is FamilyInstance instance))
            {
                reason="Host is not a structural FamilyInstance.";
                return false;
            }
            if(instance.Category==null)
            {
                reason="Host category unavailable.";
                return false;
            }
            long category=instance.Category.Id.Value;
            if(category==(long)BuiltInCategory.OST_StructuralFraming)
            {
                LocationCurve location=instance.Location as LocationCurve;
                if(location==null || !(location.Curve is Line line))
                {
                    reason="Curved or unavailable framing axis. Local frame not inferred.";
                    return false;
                }
                if(HostCoordinateFrame.TryLine(ToMm(line.GetEndPoint(0)),ToMm(line.GetEndPoint(1)),out frame))
                {
                    reason=null;
                    return true;
                }
                reason="Degenerate or invalid beam location line.";
                return false;
            }
            if(category!=(long)BuiltInCategory.OST_StructuralColumns &&
                category!=(long)BuiltInCategory.OST_StructuralFoundation)
            {
                reason="This category is not yet supported by the structural host resolver.";
                return false;
            }
            try
            {
                Transform transform=instance.GetTransform();
                if(transform==null)
                {
                    reason="Family transform unavailable.";
                    return false;
                }
                if(HostCoordinateFrame.TryInstance(ToMm(transform.Origin),
                    ToUnitless(transform.BasisX),ToUnitless(transform.BasisY),ToUnitless(transform.BasisZ),out frame))
                {
                    reason=null;
                    return true;
                }
                reason="Mirrored, scaled, skewed or degenerate Family basis; explicit mapping required.";
                return false;
            }
            catch(Exception ex)
            {
                reason="Family transform cannot be read: "+ex.GetType().Name;
                return false;
            }
        }
        internal static HostVector3 ToMm(XYZ xyz) =>
            new HostVector3(AuditV2Policy.Mm(xyz.X),AuditV2Policy.Mm(xyz.Y),AuditV2Policy.Mm(xyz.Z));
        internal static HostVector3 ToUnitless(XYZ xyz) =>
            new HostVector3(xyz.X,xyz.Y,xyz.Z);
    }
}
