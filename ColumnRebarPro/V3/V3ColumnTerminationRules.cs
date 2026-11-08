using System;
using System.Collections.Generic;
using System.Linq;
namespace ColumnRebarPro.V3
{
    internal static class V3ColumnTerminationRules
    {
        internal static double? SoffitInRectangle(IEnumerable<V3Point3[]> triangles,double hx,double hy)
        {
            double? lowest=null;
            foreach(var triangle in triangles)
            {
                var polygon=triangle.ToList();
                foreach(int plane in new[]{0,1,2,3})
                {
                    var output=new List<V3Point3>();
                    Func<V3Point3,double> distance=p=>plane==0?p.X+hx:plane==1?hx-p.X:plane==2?p.Y+hy:hy-p.Y;
                    for(int i=0;i<polygon.Count;i++)
                    {
                        var a=polygon[i];var b=polygon[(i+1)%polygon.Count];double da=distance(a),db=distance(b);
                        if(da>=0)output.Add(a);
                        if((da>=0)!=(db>=0))output.Add(a+(b-a)*(da/(da-db)));
                    }
                    polygon=output;
                }
                if(polygon.Count>0){double z=polygon.Min(p=>p.Z);lowest=lowest.HasValue?Math.Min(lowest.Value,z):z;}
            }
            return lowest;
        }
        internal static double EndMm(double bottom,double top,double cover,double diameter,double? soffit)
        {
            if(!V3BottleneckRules.Finite(bottom)||!V3BottleneckRules.Finite(top)||top<=bottom||
                !V3BottleneckRules.Finite(cover)||cover<=0||!V3BottleneckRules.Finite(diameter)||diameter<=0)
                throw new InvalidOperationException("Cao độ hoặc lớp bảo vệ đầu thanh dư không hợp lệ.");
            double limit=top;
            if(soffit.HasValue)
            {
                if(!V3BottleneckRules.Finite(soffit.Value)||soffit.Value<=bottom)
                    throw new InvalidOperationException("Không xác định được đáy dầm ở đầu cột.");
                limit=Math.Min(top,soffit.Value);
            }
            double end=limit-cover-diameter/2;
            if(end<=bottom+cover+diameter/2)
                throw new InvalidOperationException("Chiều cao còn lại không đủ kết thúc thanh dư trong cột.");
            return end;
        }
    }
}
