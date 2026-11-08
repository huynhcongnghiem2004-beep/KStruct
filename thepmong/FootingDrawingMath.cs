using System;
using System.Collections.Generic;
using System.Linq;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal struct DrawingPoint2
    {
        internal double X, Y;
        internal DrawingPoint2(double x, double y) { X=x; Y=y; }
    }
    internal struct DrawingInterval
    {
        internal double Start, End;
        internal DrawingInterval(double start,double end) { Start=start; End=end; }
    }
    internal sealed class DrawingViewRange
    {
        internal double Top, Cut, Bottom, Depth;
    }
    internal static class FootingDrawingMath
    {
        internal static DrawingViewRange Range(double hostBottom, double hostTop, double barBottom, double barTop, double levelElevation, double padding)
        {
            if (new[]{hostBottom,hostTop,barBottom,barTop,levelElevation,padding}.Any(x=>double.IsNaN(x)||double.IsInfinity(x)) ||
                hostTop<hostBottom || barTop<barBottom || padding<=0) throw new ArgumentException("Khoảng cao độ không hợp lệ.");
            double lower=Math.Min(hostBottom,barBottom), upper=Math.Max(hostTop,barTop);
            return new DrawingViewRange {Top=upper+padding-levelElevation,Cut=upper+padding/2-levelElevation,
                Bottom=lower-padding-levelElevation,Depth=lower-2*padding-levelElevation};
        }
        // Clip a crop-boundary segment against a projected solid triangle, retaining actual silhouette intersections.
        internal static DrawingInterval? ClipTriangle(DrawingPoint2 a, DrawingPoint2 b, DrawingPoint2 p, DrawingPoint2 q, DrawingPoint2 r)
        {
            double area=Cross(q.X-p.X,q.Y-p.Y,r.X-p.X,r.Y-p.Y);
            if (Math.Abs(area)<1e-10) return null;
            double sign=area>0?1:-1, enter=0, exit=1;
            var vertices=new[]{p,q,r};
            for(int i=0;i<3;i++)
            {
                DrawingPoint2 v=vertices[i], w=vertices[(i+1)%3];
                double f=sign*Cross(w.X-v.X,w.Y-v.Y,a.X-v.X,a.Y-v.Y);
                double df=sign*Cross(w.X-v.X,w.Y-v.Y,b.X-a.X,b.Y-a.Y);
                if(Math.Abs(df)<1e-12) {if(f < -1e-9) return null;continue;}
                double t=-f/df;
                if(df>0) enter=Math.Max(enter,t); else exit=Math.Min(exit,t);
                if(exit-enter<=1e-9) return null;
            }
            return new DrawingInterval(enter,exit);
        }
        internal static IList<DrawingInterval> Merge(IEnumerable<DrawingInterval> source)
        {
            var result=new List<DrawingInterval>();
            foreach(var span in source.OrderBy(x=>x.Start))
            {
                if(result.Count==0 || span.Start>result[result.Count-1].End+1e-8) result.Add(span);
                else {var last=result[result.Count-1];last.End=Math.Max(last.End,span.End);result[result.Count-1]=last;}
            }
            return result;
        }
        internal static bool InteriorOnLeft(DrawingPoint2 a,DrawingPoint2 b,DrawingPoint2 inside) =>
            Cross(b.X-a.X,b.Y-a.Y,inside.X-a.X,inside.Y-a.Y)>0;
        private static double Cross(double ax,double ay,double bx,double by)=>ax*by-ay*bx;
    }
}
