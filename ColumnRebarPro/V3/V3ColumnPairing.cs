using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ColumnRebarPro.V3
{
    internal struct V3Point2
    {
        internal double X, Y;
        internal V3Point2(double x, double y) { X=x; Y=y; }
        internal double Length => Math.Sqrt(X*X+Y*Y);
        internal V3Point2 Unit => Length > 1e-9 ? this / Length : new V3Point2();
        internal double Dot(V3Point2 b) => X*b.X+Y*b.Y;
        public static V3Point2 operator +(V3Point2 a,V3Point2 b)=>new V3Point2(a.X+b.X,a.Y+b.Y);
        public static V3Point2 operator -(V3Point2 a,V3Point2 b)=>new V3Point2(a.X-b.X,a.Y-b.Y);
        public static V3Point2 operator *(V3Point2 a,double b)=>new V3Point2(a.X*b,a.Y*b);
        public static V3Point2 operator /(V3Point2 a,double b)=>new V3Point2(a.X/b,a.Y/b);
    }

    internal sealed class V3ConnectionProfile
    {
        internal string Label;
        internal double Width,Depth,Diameter,TieDiameter,Cover;
        internal int Cx,Cy;
        internal V3Point2 Center,AxisX=new V3Point2(1,0),AxisY=new V3Point2(0,1);
        internal bool HasFoot;
        internal V3AnchorDirection FootDirection;
        internal double HalfX(double diameter)=>Width/2-Cover-TieDiameter-diameter/2;
        internal double HalfY(double diameter)=>Depth/2-Cover-TieDiameter-diameter/2;
        internal V3Point2 At(double x,double y)=>Center+AxisX*x+AxisY*y;
        internal V3Point2 Local(V3Point2 p) { p=p-Center; return new V3Point2(p.Dot(AxisX),p.Dot(AxisY)); }
        internal bool Contains(V3Point2 p,double diameter)
        {
            p=Local(p);return Math.Abs(p.X)<=HalfX(diameter)+1e-5 && Math.Abs(p.Y)<=HalfY(diameter)+1e-5;
        }
        internal void Validate()
        {
            double[] values={Width,Depth,Diameter,TieDiameter,Cover,Center.X,Center.Y,AxisX.X,AxisX.Y,AxisY.X,AxisY.Y};
            if(values.Any(v=>!V3BottleneckRules.Finite(v)) || Width<=0 || Depth<=0 || Diameter<=0 ||
                TieDiameter<=0 || Cover<=0 || Cx<2 || Cy<2 || Cx>20 || Cy>20 ||
                HalfX(Diameter)<=0 || HalfY(Diameter)<=0 || Math.Abs(AxisX.Length-1)>1e-5 ||
                Math.Abs(AxisY.Length-1)>1e-5 || Math.Abs(AxisX.Dot(AxisY))>1e-5)
                throw new InvalidOperationException("Tiết diện, số thanh hoặc hệ trục cột không hợp lệ.");
        }
        internal string Describe()=>string.Format(CultureInfo.InvariantCulture,
            "{0}: {1:0.###}×{2:0.###}; Cx/Cy={3}/{4}; D{5:0.###}; đai D{6:0.###}; cover={7:0.###}; tâm=({8:0.###},{9:0.###}) mm",
            Label,Width,Depth,Cx,Cy,Diameter,TieDiameter,Cover,Center.X,Center.Y);
    }

    internal sealed class V3PairPoint
    {
        internal V3Point2 Point;
        internal bool Corner;
        internal List<V3Point2> Inward=new List<V3Point2>();
    }
    internal sealed class V3BarConnection
    {
        internal int LowerIndex,UpperIndex;
        internal V3Point2 Lower,Upper,End;
        internal double Shift,Height;
    }
    internal sealed class V3ColumnConnectionPlan
    {
        internal V3ConnectionProfile Lower,Upper;
        internal List<V3BarConnection> Bars=new List<V3BarConnection>();
        internal List<int> TerminatedLower=new List<int>();
        internal double MaximumShift=>Bars.Count==0?0:Bars.Max(b=>b.Shift);
        internal double MaximumHeight=>Bars.Count==0?0:Bars.Max(b=>b.Height);
    }
    public sealed class V3ConnectionDifference
    {
        public string Parameter {get;set;}
        public string Lower {get;set;}
        public string Upper {get;set;}
        public string Status {get;set;}
    }

    internal static class V3ColumnPairing
    {
        internal static List<V3PairPoint> Points(V3ConnectionProfile p)
        {
            p.Validate();double hx=p.HalfX(p.Diameter),hy=p.HalfY(p.Diameter);
            var points=new List<V3PairPoint>();
            Action<double,double,bool,V3Point2,V3Point2?> add=(x,y,corner,n1,n2)=>{
                var point=new V3PairPoint {Point=p.At(x,y),Corner=corner};point.Inward.Add(n1);
                if(n2.HasValue)point.Inward.Add(n2.Value);points.Add(point);
            };
            for(int i=0;i<p.Cx;i++)
            {
                double x=-hx+2*hx*i/(p.Cx-1);bool corner=i==0||i==p.Cx-1;
                V3Point2? second=corner?(V3Point2?)(p.AxisX*(i==0?1:-1)):null;
                add(x,-hy,corner,p.AxisY,second);add(x,hy,corner,p.AxisY*-1,second);
            }
            for(int i=1;i<p.Cy-1;i++)
            {
                double y=-hy+2*hy*i/(p.Cy-1);
                add(-hx,y,false,p.AxisX,null);add(hx,y,false,p.AxisX*-1,null);
            }
            return points.OrderBy(b=>Math.Atan2(b.Point.Y-p.Center.Y,b.Point.X-p.Center.X)).ToList();
        }

        internal static List<V3ConnectionDifference> Compare(V3ConnectionProfile a,V3ConnectionProfile b)
        {
            Func<double,string> n=v=>v.ToString("0.###",CultureInfo.InvariantCulture);
            Func<V3Point2,string> xy=p=>"("+n(p.X)+", "+n(p.Y)+")";
            var rows=new List<V3ConnectionDifference>();
            Action<string,string,string> add=(name,l,u)=>rows.Add(new V3ConnectionDifference {
                Parameter=name,Lower=l,Upper=u,Status=l==u?"Giống":"Khác"});
            add("Tiết diện (mm)",n(a.Width)+" × "+n(a.Depth),n(b.Width)+" × "+n(b.Depth));
            add("Cx / Cy",a.Cx+" / "+a.Cy,b.Cx+" / "+b.Cy);
            add("Tổng thanh",(2*(a.Cx+a.Cy)-4).ToString(),(2*(b.Cx+b.Cy)-4).ToString());
            add("Thép chủ (mm)",n(a.Diameter),n(b.Diameter));add("Thép đai (mm)",n(a.TieDiameter),n(b.TieDiameter));
            add("Cover (mm)",n(a.Cover),n(b.Cover));add("Tâm XY (mm)",xy(a.Center),xy(b.Center));
            add("Trục X",xy(a.AxisX),xy(b.AxisX));add("Trục Y",xy(a.AxisY),xy(b.AxisY));
            return rows;
        }

        internal static V3ColumnConnectionPlan Create(V3ConnectionProfile lower,V3ConnectionProfile upper,double clearance,double slope)
        {
            lower.Validate();upper.Validate();
            if(!V3BottleneckRules.Finite(clearance)||clearance<0||!V3BottleneckRules.Finite(slope)||slope<=0)
                throw new InvalidOperationException("Khe hở hoặc tỷ lệ nhấn cổ chai không hợp lệ.");
            var all=Points(lower);var b=Points(upper);
            if(all.Count<b.Count)
                throw new InvalidOperationException("Khác số thanh: tầng trên có thêm thanh; cần phương án bổ sung/neo riêng.");
            var map=MatchSubset(all,b);
            var indices=Enumerable.Range(0,all.Count).Where(i=>map[i]>=0).ToArray();
            var a=indices.Select(i=>all[i]).ToList();int count=a.Count;
            var targets=indices.Select(i=>map[i]).ToArray();
            var plan=new V3ColumnConnectionPlan {Lower=lower,Upper=upper,
                TerminatedLower=Enumerable.Range(0,all.Count).Where(i=>map[i]<0).ToList()};
            var choices=new List<V3Point2>[count];double separation=(lower.Diameter+upper.Diameter)/2+clearance;
            for(int i=0;i<count;i++)
            {
                var target=b[targets[i]];
                choices[i]=Candidates(lower,upper,a[i],target,separation)
                    .Where(p=>lower.Contains(p,lower.Diameter)&&upper.Contains(p,lower.Diameter))
                    .Where(p=>b.All(other=>(p-other.Point).Length>=(lower.Diameter+upper.Diameter)/2-1e-5))
                    // Terminated bars remain straight below the joint; don't bend continuing bars across them.
                    .Where(p=>plan.TerminatedLower.All(t=>Separated(a[i].Point,p,all[t].Point,all[t].Point,slope,lower.Diameter)))
                    .OrderBy(p=>(p-a[i].Point).Length).ToList();
                if(choices[i].Count==0)throw new InvalidOperationException("Thanh "+(i+1)+": không có vị trí cổ chai đủ cover và không giao thanh tầng trên.");
            }
            var order=Enumerable.Range(0,count).OrderBy(i=>choices[i].Count).ThenBy(i=>i).ToArray();
            var selected=new V3Point2[count];var assigned=new bool[count];int attempts=0;
            Func<int,bool> solve=null;
            solve=depth=>{
                if(depth==count)return true;if(++attempts>50000)return false;int i=order[depth];
                foreach(var p in choices[i])
                {
                    bool valid=true;
                    for(int j=0;j<count;j++)if(assigned[j]&&!Separated(a[i].Point,p,a[j].Point,selected[j],slope,lower.Diameter)){valid=false;break;}
                    if(!valid)continue;selected[i]=p;assigned[i]=true;
                    if(solve(depth+1))return true;assigned[i]=false;
                }
                return false;
            };
            if(!solve(0))throw new InvalidOperationException("Không tìm được bố trí cổ chai không giao nhau; kiểm tra mật độ thanh và khe hở nhấn.");
            for(int i=0;i<count;i++)
            {
                double shift=(selected[i]-a[i].Point).Length;
                plan.Bars.Add(new V3BarConnection {LowerIndex=indices[i],UpperIndex=targets[i],
                    Lower=a[i].Point,Upper=b[targets[i]].Point,End=selected[i],Shift=shift,Height=shift*slope});
            }
            return plan;
        }

        // Match all upper bars once, preserving perimeter order and the four corners.
        // Non-corner lower bars may be skipped; never silently invent upper starters.
        private static int[] MatchSubset(List<V3PairPoint> lower,List<V3PairPoint> upper)
        {
            int n=lower.Count,m=upper.Count;double best=double.PositiveInfinity;int[] selected=null;bool ambiguous=false;
            for(int offset=0;offset<m;offset++)
            {
                var cost=new double[n+1,m+1];var ways=new int[n+1,m+1];var matched=new bool[n+1,m+1];
                for(int i=0;i<=n;i++)for(int j=0;j<=m;j++)cost[i,j]=double.PositiveInfinity;
                cost[0,0]=0;ways[0,0]=1;
                for(int i=1;i<=n;i++)for(int j=0;j<=m;j++)
                {
                    if(!lower[i-1].Corner){cost[i,j]=cost[i-1,j];ways[i,j]=ways[i-1,j];}
                    if(j==0||lower[i-1].Corner!=upper[(j-1+offset)%m].Corner||ways[i-1,j-1]==0)continue;
                    var delta=lower[i-1].Point-upper[(j-1+offset)%m].Point;
                    double candidate=cost[i-1,j-1]+delta.Dot(delta);
                    if(candidate<cost[i,j]-1e-7){cost[i,j]=candidate;ways[i,j]=ways[i-1,j-1];matched[i,j]=true;}
                    else if(Math.Abs(candidate-cost[i,j])<=1e-7)ways[i,j]=Math.Min(2,ways[i,j]+ways[i-1,j-1]);
                }
                if(ways[n,m]==0)continue;
                double tolerance=1e-7*Math.Max(1,double.IsInfinity(best)?cost[n,m]:best);
                if(cost[n,m]<best-tolerance||selected==null)
                {
                    best=cost[n,m];ambiguous=ways[n,m]>1;
                    selected=Enumerable.Repeat(-1,n).ToArray();int j=m;
                    for(int i=n;i>0;i--)if(matched[i,j]){selected[i-1]=(j-1+offset)%m;j--;}
                }
                else if(Math.Abs(cost[n,m]-best)<=tolerance)ambiguous=true;
            }
            if(selected==null)throw new InvalidOperationException("Không ghép được thanh trên các cạnh; giữ cặp góc và không ghép góc với thanh giữa cạnh.");
            if(ambiguous)throw new InvalidOperationException("Có nhiều phương án ghép thanh tương đương; kiểm tra hướng cột hoặc chỉ định lại vị trí thanh.");
            return selected;
        }

        private static bool Separated(V3Point2 a,V3Point2 endA,V3Point2 b,V3Point2 endB,double slope,double diameter)
        {
            double ha=(endA-a).Length*slope,hb=(endB-b).Length*slope;
            double bottom=-Math.Max(ha,hb)-diameter;
            var pa=new[]{new V3Point3(a.X,a.Y,bottom),new V3Point3(a.X,a.Y,-ha),
                new V3Point3(endA.X,endA.Y,0),new V3Point3(endA.X,endA.Y,diameter)};
            var pb=new[]{new V3Point3(b.X,b.Y,bottom),new V3Point3(b.X,b.Y,-hb),
                new V3Point3(endB.X,endB.Y,0),new V3Point3(endB.X,endB.Y,diameter)};
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)
                if(V3ProfileGeometry.SegmentDistance(pa[i],pa[i+1],pb[j],pb[j+1])<diameter-1e-5)return false;
            return true;
        }

        private static List<V3Point2> Candidates(V3ConnectionProfile lower,V3ConnectionProfile upper,
            V3PairPoint source,V3PairPoint target,double separation)
        {
            var result=new List<V3Point2>();
            Action<V3Point2> add=p=>{if(result.All(x=>(x-p).Length>1e-5))result.Add(p);};
            if(lower.HasFoot)
            {
                var local=lower.Local(source.Point);V3Point2 axis;
                if(lower.FootDirection==V3AnchorDirection.PositiveX||lower.FootDirection==V3AnchorDirection.NegativeX)axis=lower.AxisX;
                else if(lower.FootDirection==V3AnchorDirection.PositiveY||lower.FootDirection==V3AnchorDirection.NegativeY)axis=lower.AxisY;
                else axis=Math.Abs(Math.Abs(local.Y)-lower.HalfY(lower.Diameter))<1e-4?lower.AxisY:lower.AxisX;
                var offset=target.Point-source.Point;double along=offset.Dot(axis),perp2=offset.Dot(offset)-along*along;
                if(perp2<=separation*separation+1e-5)
                {
                    double step=Math.Sqrt(Math.Max(0,separation*separation-perp2));
                    add(source.Point+axis*(along+step));add(source.Point+axis*(along-step));
                }
            }
            foreach(var normal in target.Inward)add(target.Point+normal*separation);
            if(target.Inward.Count==2)add(target.Point+(target.Inward[0]+target.Inward[1]).Unit*separation);
            add(target.Point+(upper.Center-target.Point).Unit*separation);
            return result;
        }
    }
}
