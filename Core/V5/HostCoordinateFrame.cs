using System;
using System.Collections.Generic;

namespace SDK_HD.KStruct.Core.V5
{
    /// <summary>Revit-independent 3D local coordinate frames for structural host diagnostics.
    /// Values are in millimeters; all directions are unit vectors.</summary>
    internal struct HostVector3
    {
        internal readonly double X,Y,Z;
        internal HostVector3(double x,double y,double z) {X=x;Y=y;Z=z;}
        internal double Magnitude => Math.Sqrt(X*X+Y*Y+Z*Z);
        internal double[] Components => new[]{X,Y,Z};
        internal static HostVector3 operator+(HostVector3 a,HostVector3 b)=>new HostVector3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        internal static HostVector3 operator-(HostVector3 a,HostVector3 b)=>new HostVector3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        internal static HostVector3 operator*(HostVector3 a,double scalar)=>new HostVector3(a.X*scalar,a.Y*scalar,a.Z*scalar);
        internal static double Dot(HostVector3 a,HostVector3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        internal static HostVector3 Cross(HostVector3 a,HostVector3 b)=>
            new HostVector3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        internal bool IsFinite=>AuditV2Policy.IsFinite(X)&&AuditV2Policy.IsFinite(Y)&&AuditV2Policy.IsFinite(Z);
        internal bool TryUnit(out HostVector3 unit)
        {
            double length=Magnitude;
            unit=default(HostVector3);
            if(!IsFinite||!AuditV2Policy.IsFinite(length)||length<1e-9)return false;
            unit=new HostVector3(X/length,Y/length,Z/length);
            return true;
        }
    }

    internal sealed class HostCoordinateFrame
    {
        internal readonly HostVector3 Origin,X,Y,Z;
        internal readonly string Source;
        internal double Handedness => HostVector3.Dot(HostVector3.Cross(X,Y),Z);
        internal bool IsMirrored => Handedness < 0;
        private HostCoordinateFrame(HostVector3 origin,HostVector3 x,HostVector3 y,HostVector3 z,string source)
        {Origin=origin;X=x;Y=y;Z=z;Source=source;}
        internal double[] ToLocal(HostVector3 worldPoint)
        {
            var d=worldPoint-Origin;
            return new[]{HostVector3.Dot(d,X),HostVector3.Dot(d,Y),HostVector3.Dot(d,Z)};
        }
        internal double[] ToWorld(double lx,double ly,double lz) =>
            (Origin+X*lx+Y*ly+Z*lz).Components;
        internal double[] LocalDirection(HostVector3 worldVector) =>
            new[]{HostVector3.Dot(worldVector,X),HostVector3.Dot(worldVector,Y),HostVector3.Dot(worldVector,Z)};

        /// <summary>Line tangent is X, with vertical Z as up wherever possible.
        /// Vertical columns use the reference world Y; their handedness remains right-handed.</summary>
        internal static bool TryLine(HostVector3 start,HostVector3 end,out HostCoordinateFrame frame)
        {
            frame=null;
            if(!start.IsFinite||!end.IsFinite)return false;
            HostVector3 tangent;
            if(!(end-start).TryUnit(out tangent))return false;
            // Pick the least aligned global basis. Unlike a hard-coded 0.98 cutoff
            // this guarantees the cross product is numerically well conditioned.
            HostVector3[] references={new HostVector3(0,0,1),new HostVector3(0,1,0),
                new HostVector3(1,0,0)};
            HostVector3 reference=references[0];
            foreach(HostVector3 candidate in references)
                if(Math.Abs(HostVector3.Dot(candidate,tangent))<
                   Math.Abs(HostVector3.Dot(reference,tangent)))reference=candidate;
            return TryLineWithReference(start,end,reference,"location-curve-straight",out frame);
        }

        // Project a known section transverse axis onto the plane normal to the
        // beam location line. This reflects roll of a Family transform but does
        // not by itself certify the concrete cross section.
        internal static bool TryLineWithReference(HostVector3 start,HostVector3 end,
            HostVector3 sectionAxis,string source,out HostCoordinateFrame frame)
        {
            frame=null;
            HostVector3 tangent, transverse, normal;
            if(!start.IsFinite||!end.IsFinite||!sectionAxis.IsFinite||
               !(end-start).TryUnit(out tangent))return false;
            var projected=sectionAxis-tangent*HostVector3.Dot(sectionAxis,tangent);
            if(!projected.TryUnit(out transverse))return false;
            if(!HostVector3.Cross(tangent,transverse).TryUnit(out normal))return false;
            frame=new HostCoordinateFrame(start,tangent,transverse,normal,
                string.IsNullOrWhiteSpace(source)?"line-with-reference":source);
            return true;
        }

        // Revit can reverse the LocationCurve endpoints without changing the
        // physical host. The canonical direction is only for diagnostics:
        // structural support/start/end conventions require a separate resolver.
        internal static bool TryCanonicalLine(HostVector3 start,HostVector3 end,
            HostVector3 sectionAxis,out HostCoordinateFrame frame,out bool reversed)
        {
            reversed=false;
            frame=null;
            if(!start.IsFinite||!end.IsFinite)return false;
            var a=start;var b=end;
            if(a.X>b.X||(a.X==b.X&&a.Y>b.Y)||
                (a.X==b.X&&a.Y==b.Y&&a.Z>b.Z))
            {a=end;b=start;reversed=true;}
            return TryLineWithReference(a,b,sectionAxis,"canonical-line-section-reference",out frame);
        }

        /// <summary>Instance transform frame; preserve orthonormal mirrored bases for diagnostics.
        /// Reflection is not placement approval. Reject skewed, degenerate and scaled bases.</summary>
        internal static bool TryInstance(HostVector3 origin,HostVector3 bx,HostVector3 by,HostVector3 bz,
            out HostCoordinateFrame frame)
        {
            frame=null;
            if(!origin.IsFinite||!bx.IsFinite||!by.IsFinite||!bz.IsFinite)return false;
            HostVector3 x,y,z;
            if(!bx.TryUnit(out x)||!by.TryUnit(out y)||!bz.TryUnit(out z))return false;
            const double tolerance=0.0001;
            if(Math.Abs(bx.Magnitude-1)>tolerance||Math.Abs(by.Magnitude-1)>tolerance||
                Math.Abs(bz.Magnitude-1)>tolerance)return false;
            if(Math.Abs(HostVector3.Dot(x,y))>tolerance||Math.Abs(HostVector3.Dot(y,z))>tolerance||
                Math.Abs(HostVector3.Dot(z,x))>tolerance)return false;
            if(Math.Abs(Math.Abs(HostVector3.Dot(HostVector3.Cross(x,y),z))-1)>tolerance)return false;
            frame=new HostCoordinateFrame(origin,x,y,z,"family-instance-transform");
            return true;
        }

        internal Dictionary<string,object> AsRecord()
        {
            return new Dictionary<string,object> {
                {"originMm",Origin.Components},{"axisX",X.Components},
                {"axisY",Y.Components},{"axisZ",Z.Components},
                {"source",Source},{"validatedOrthonormal",true},
                {"handedness",IsMirrored?"mirrored":"right-handed"},
                {"placementApproved",false},
                {"placementBlocker","Local axes alone do not establish concrete faces, cover, joint or anchorage."}
            };
        }
    }
}
