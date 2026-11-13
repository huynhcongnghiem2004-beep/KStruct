using System;

namespace ColumnRebarPro.V3
{
    internal static class V3ConnectionAdapter
    {
        internal static V3ConnectionProfile Profile(V3SegmentWork work)
        {
            var c=work.Config;var g=work.Geometry;
            return new V3ConnectionProfile {
                Label=work.Segment.LevelName,Width=V3Units.Mm(g.Width),Depth=V3Units.Mm(g.Depth),
                BottomZ=V3Units.Mm(g.Bottom),TopZ=V3Units.Mm(g.Top),
                JointBeamHeightMm=c.JointBeamHeight,BeamTopCoverMm=c.BeamTopCoverMm,
                Center=new V3Point2(V3Units.Mm(g.Center.X),V3Units.Mm(g.Center.Y)),
                AxisX=new V3Point2(g.X.X,g.X.Y),AxisY=new V3Point2(g.Y.X,g.Y.Y),
                Cx=c.Cx,Cy=c.Cy,Diameter=c.MainDiameter,TieDiameter=c.TieDiameter,Cover=c.Cover,
                HasFoot=work.FoundationHost!=null,FootDirection=c.FoundationAnchorDirection };
        }
        internal static V3ColumnConnectionPlan Plan(V3SegmentWork lower,V3SegmentWork upper)
        {
            var a=Profile(lower);var b=Profile(upper);
            try { return V3ColumnPairing.Create(a,b,lower.Config.BottleneckClearanceMm,lower.Config.BottleneckSlope); }
            catch(InvalidOperationException ex)
            {
                throw new InvalidOperationException(ex.Message+"\nDưới: "+a.Describe()+"\nTrên: "+b.Describe(),ex);
            }
        }
    }
}
