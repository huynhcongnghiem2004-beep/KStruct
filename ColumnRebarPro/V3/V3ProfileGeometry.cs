using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    internal struct V3Point3
    {
        internal double X,Y,Z;
        internal V3Point3(double x,double y,double z){X=x;Y=y;Z=z;}
        internal double Length=>Math.Sqrt(X*X+Y*Y+Z*Z);
        internal V3Point3 Unit=>Length>1e-9?this/Length:new V3Point3();
        internal double Dot(V3Point3 b)=>X*b.X+Y*b.Y+Z*b.Z;
        internal V3Point3 Cross(V3Point3 b)=>new V3Point3(Y*b.Z-Z*b.Y,Z*b.X-X*b.Z,X*b.Y-Y*b.X);
        public static V3Point3 operator +(V3Point3 a,V3Point3 b)=>new V3Point3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static V3Point3 operator -(V3Point3 a,V3Point3 b)=>new V3Point3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static V3Point3 operator *(V3Point3 a,double b)=>new V3Point3(a.X*b,a.Y*b,a.Z*b);
        public static V3Point3 operator /(V3Point3 a,double b)=>new V3Point3(a.X/b,a.Y/b,a.Z/b);
    }
    internal sealed class V3BarPath
    {
        internal List<V3Point3> Points;
        internal int BarIndex;
        internal double Shift,Height;
        internal V3Point3 Normal;
        internal bool Planar;
        internal void Inspect()
        {
            Normal=new V3Point3();
            var first=Points[1]-Points[0];
            for(int i=2;i<Points.Count;i++)
            {
                var cross=first.Cross(Points[i]-Points[0]);
                if(cross.Length>1e-5){Normal=cross.Unit;break;}
            }
            if(Normal.Length<.5)Normal=new V3Point3(1,0,0);
            Planar=Points.All(p=>Math.Abs((p-Points[0]).Dot(Normal))<=.05);
        }
    }
    internal sealed class V3BarPathGroup
    {
        internal List<V3BarPath> Bars=new List<V3BarPath>();
        internal V3Point3 Step;
    }
    internal sealed class V3RoundedCurve
    {
        internal V3Point3 Start,End,Mid;
        internal bool Arc;
    }
    internal static class V3ProfileGeometry
    {
        internal static double SegmentDistance(V3Point3 a,V3Point3 b,V3Point3 c,V3Point3 d)
        {
            var u=b-a;var v=d-c;var w=a-c;
            double aa=u.Dot(u),bb=u.Dot(v),cc=v.Dot(v),dd=u.Dot(w),ee=v.Dot(w);
            Func<double,double> clamp=x=>Math.Max(0,Math.Min(1,x));
            if(aa<1e-12&&cc<1e-12)return w.Length;
            if(aa<1e-12)return (a-(c+v*clamp(ee/cc))).Length;
            if(cc<1e-12)return (a+u*clamp(-dd/aa)-c).Length;
            double denom=aa*cc-bb*bb;
            double t=denom>1e-12?clamp((bb*ee-cc*dd)/denom):0;
            double q=(bb*t+ee)/cc;
            if(q<0){q=0;t=clamp(-dd/aa);}
            else if(q>1){q=1;t=clamp((bb-dd)/aa);}
            return (w+u*t-v*q).Length;
        }

        internal static List<V3BarPathGroup> Group(List<V3BarPath> paths,bool enabled)
        {
            foreach(var p in paths)p.Inspect();
            var remaining=new List<V3BarPath>(paths);var result=new List<V3BarPathGroup>();
            while(remaining.Count>0)
            {
                var prototype=remaining[0];
                var compatible=enabled&&prototype.Planar?remaining.Where(p=>Compatible(prototype,p))
                    .OrderBy(p=>p.Points[0].Dot(prototype.Normal)).ToList():new List<V3BarPath>{prototype};
                var group=new V3BarPathGroup();group.Bars.Add(compatible[0]);
                if(compatible.Count>1)
                {
                    group.Step=compatible[1].Points[0]-compatible[0].Points[0];
                    group.Bars.Add(compatible[1]);
                    for(int i=2;i<compatible.Count;i++)
                    {
                        if((compatible[i].Points[0]-compatible[i-1].Points[0]-group.Step).Length>.05)break;
                        group.Bars.Add(compatible[i]);
                    }
                }
                foreach(var bar in group.Bars)remaining.Remove(bar);result.Add(group);
            }
            return result;
        }
        private static bool Compatible(V3BarPath a,V3BarPath b)
        {
            if(!b.Planar||a.Points.Count!=b.Points.Count)return false;
            var offset=b.Points[0]-a.Points[0];
            if(offset.Cross(a.Normal).Length>.05)return false;
            for(int i=1;i<a.Points.Count;i++)if((b.Points[i]-a.Points[i]-offset).Length>.05)return false;
            return true;
        }

        // Explicit tangent arcs for non-planar FreeForm bars. All dimensions are millimetres.
        internal static List<V3RoundedCurve> Round(List<V3Point3> points,double radius,double tolerance)
        {
            if(points.Count<2||!V3BottleneckRules.Finite(radius)||radius<=0)throw new InvalidOperationException("Bán kính uốn không hợp lệ.");
            var trims=new double[points.Count];var starts=new V3Point3[points.Count];var ends=new V3Point3[points.Count];
            var mids=new V3Point3[points.Count];
            for(int i=1;i<points.Count-1;i++)
            {
                var incoming=(points[i]-points[i-1]).Unit;var outgoing=(points[i+1]-points[i]).Unit;
                double dot=Math.Max(-1,Math.Min(1,incoming.Dot(outgoing)));double angle=Math.Acos(dot);
                if(angle<1e-7){starts[i]=ends[i]=points[i];continue;}
                if(Math.PI-angle<1e-6)throw new InvalidOperationException("Đường thanh quay ngược 180 độ, không tạo được gối uốn.");
                trims[i]=radius*Math.Tan(angle/2);
                starts[i]=points[i]-incoming*trims[i];ends[i]=points[i]+outgoing*trims[i];
                var center=points[i]+(outgoing-incoming).Unit*(radius/Math.Cos(angle/2));
                mids[i]=center+((starts[i]-center)+(ends[i]-center)).Unit*radius;
            }
            for(int i=0;i<points.Count-1;i++)
                if((points[i+1]-points[i]).Length<=trims[i]+trims[i+1]+tolerance)
                    throw new InvalidOperationException("Đoạn thanh quá ngắn cho các bán kính uốn; tăng chiều cao nhấn hoặc chiều dài chân neo.");
            var result=new List<V3RoundedCurve>();var previous=points[0];
            for(int i=1;i<points.Count-1;i++)
            {
                if((starts[i]-previous).Length>tolerance)result.Add(new V3RoundedCurve{Start=previous,End=starts[i]});
                if(trims[i]>0)result.Add(new V3RoundedCurve{Arc=true,Start=starts[i],End=ends[i],Mid=mids[i]});
                previous=ends[i];
            }
            result.Add(new V3RoundedCurve{Start=previous,End=points[points.Count-1]});return result;
        }
    }
}
