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
            var reference=Math.Abs(HostVector3.Dot(tangent,new HostVector3(0,0,1)))>0.98
                ?new HostVector3(0,1,0):new HostVector3(0,0,1);
            HostVector3 y,z;
            if(!HostVector3.Cross(reference,tangent).TryUnit(out y)||
                !HostVector3.Cross(tangent,y).TryUnit(out z))return false;
            frame=new HostCoordinateFrame(start,tangent,y,z,"location-curve-straight");
            return true;
        }

        /// <summary>Instance transform frame; reject skewed, mirrored, degenerate and scaled bases.
        /// A mirrored Family may be valid in Revit, but this method cannot certify its axes.</summary>
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
            if(HostVector3.Dot(HostVector3.Cross(x,y),z)<1-tolerance)return false;
            frame=new HostCoordinateFrame(origin,x,y,z,"family-instance-transform");
            return true;
        }

        internal Dictionary<string,object> AsRecord()
        {
            return new Dictionary<string,object> {
                {"originMm",Origin.Components},{"axisX",X.Components},
                {"axisY",Y.Components},{"axisZ",Z.Components},
                {"source",Source},{"validatedOrthonormal",true}
            };
        }
    }
}
