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
                try
                {
                    LocationCurve location=instance.Location as LocationCurve;
                    if(location==null || !(location.Curve is Line line))
                    {
                        reason="Curved or unavailable framing axis. Local frame not inferred.";
                        return false;
                    }
                    // A LocationCurve alone does not encode cross-section roll. Revit
                    // Family transform is only a section reference, not certified
                    // concrete faces or reinforcement placement permission.
                    Transform t=instance.GetTransform();
                    if(t==null)
                    {
                        reason="Framing Family transform unavailable.";
                        return false;
                    }
                    bool reversed;
                    if(HostCoordinateFrame.TryCanonicalLine(ToMm(line.GetEndPoint(0)),
                        ToMm(line.GetEndPoint(1)),ToUnitless(t.BasisY),out frame,out reversed))
                    {
                        reason=null;
                        return true;
                    }
                    reason="Beam transform transverse axis is invalid or parallel to line; "+
                        "section geometry must be inspected before creating Rebar.";
                    return false;
                }
                catch(Exception ex)
                {
                    reason="Framing location/transform unavailable: "+ex.GetType().Name;
                    return false;
                }
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
                reason="Scaled, skewed or degenerate Family basis; explicit mapping required.";
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
