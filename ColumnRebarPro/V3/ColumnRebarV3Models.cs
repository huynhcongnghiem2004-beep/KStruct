using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    public sealed class V3Geometry
    {
        public XYZ Center, X, Y;
        public double Width, Depth, Bottom, Top;
        public XYZ At(double x, double y, double z)
        {
            return new XYZ(Center.X, Center.Y, 0) + X * x + Y * y + XYZ.BasisZ * z;
        }
        public double Height { get { return Top - Bottom; } }
    }

    public static class V3Units
    {
        public static double Ft(double mm) { return UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters); }
        public static double Mm(double feet) { return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters); }
    }

    public static class V3GeometryReader
    {
        // Geometry is projected on the actual local axes, not on the global bounding box.
        public static V3Geometry Read(FamilyInstance col)
        {
            var loc = col.Location as LocationPoint;
            if (loc == null) throw new InvalidOperationException("Chỉ hỗ trợ cột đứng có LocationPoint.");
            Transform t = col.GetTransform();
            if (Math.Abs(t.BasisZ.Z) < 0.999) throw new InvalidOperationException("Không hỗ trợ cột nghiêng.");
            XYZ x = new XYZ(t.BasisX.X, t.BasisX.Y, 0).Normalize();
            XYZ y = new XYZ(t.BasisY.X, t.BasisY.Y, 0).Normalize();
            if (Math.Abs(x.DotProduct(y)) > 0.0001)
                throw new InvalidOperationException("Hệ trục cột không vuông góc.");
            var geo = col.get_Geometry(new Autodesk.Revit.DB.Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false });
            if (geo == null) throw new InvalidOperationException("Cột không có Geometry.");
            double xmin = double.MaxValue, ymin = double.MaxValue, zmin = double.MaxValue;
            double xmax = double.MinValue, ymax = double.MinValue, zmax = double.MinValue;
            int vertexCount = 0;
            Action<Solid> visit = solid =>
            {
                if (solid == null || solid.Volume <= 1e-8) return;
                foreach (Face face in solid.Faces)
                {
                    var plane = face as PlanarFace;
                    if (plane == null || Math.Max(Math.Abs(plane.FaceNormal.DotProduct(x)),
                        Math.Max(Math.Abs(plane.FaceNormal.DotProduct(y)), Math.Abs(plane.FaceNormal.Z))) < .9999)
                        throw new InvalidOperationException("V3 chỉ hỗ trợ cột lăng trụ chữ nhật đứng; không hỗ trợ mặt cong hoặc mặt vát.");
                    Mesh mesh = face.Triangulate();
                    foreach (XYZ p in mesh.Vertices)
                    {
                        XYZ d = p - loc.Point;
                        double px = d.DotProduct(x), py = d.DotProduct(y);
                        xmin = Math.Min(xmin, px); xmax = Math.Max(xmax, px);
                        ymin = Math.Min(ymin, py); ymax = Math.Max(ymax, py);
                        zmin = Math.Min(zmin, p.Z); zmax = Math.Max(zmax, p.Z);
                        vertexCount++;
                    }
                }
            };
            foreach (GeometryObject item in geo)
            {
                var solid = item as Solid;
                if (solid != null) visit(solid);
                var instance = item as GeometryInstance;
                if (instance != null)
                    foreach (GeometryObject child in instance.GetInstanceGeometry())
                        if (child is Solid) visit((Solid)child);
            }
            if (vertexCount == 0 || xmax <= xmin || ymax <= ymin || zmax <= zmin)
                throw new InvalidOperationException("Không đọc được kích thước hình học cột.");
            return new V3Geometry {
                Center = loc.Point + x * (xmin + xmax) / 2 + y * (ymin + ymax) / 2,
                X = x, Y = y, Width = xmax - xmin, Depth = ymax - ymin,
                Bottom = zmin, Top = zmax
            };
        }
    }

    public sealed class V3SegmentWork
    {
        public FamilyInstance Column;
        public FamilyInstance FoundationHost;
        public double FoundationBottom, FoundationBendZ, FoundationCoverMm, FoundationAnchorLengthMm;
        public ColumnSegment Segment;
        public V3Geometry Geometry;
        public V3RebarConfig Config;
        public bool HasPrevious, HasNext;
        public bool BottleneckToUpper;
        public double BottleneckUpperBarStart;
        internal V3ColumnConnectionPlan ConnectionPlan;
        public double IncomingStarterLength, IncomingJoint;
        public bool IncomingStagger;
        public string StarterWarning;
        // Chuyển tiếp khi đổi tiết diện/đường kính: null nếu hai tầng cùng cấu tạo.
        public V3Transition TransitionUp, TransitionDown;
    }

    /// <summary>
    /// Phương án ghép thanh giữa hai tầng kề nhau khi đổi tiết diện, đường kính, lớp bảo vệ hoặc đường kính đai.
    /// Thanh dọc ghép 1-1 theo thứ tự (cùng Cx/Cy). Chiều dài tính bằng mm, trừ khi ghi chú là feet.
    /// </summary>
    public sealed class V3Transition
    {
        public double LapLength;                 // mm, chiều dài nối áp dụng cho cặp tầng này
        public double JointZ;                    // feet, cao độ mạch ngừng (đỉnh cột dưới)
        public double LowerDiameter, UpperDiameter; // mm
        public double MaxLowerBend, MaxUpperBend;   // mm, đoạn uốn xiên lớn nhất
        public string Description;
        // Chỉ số i ứng với thanh thứ i trong V3RebarBuilder.MainBarCenters (XY, feet).
        public readonly List<XYZ> LowerPoints = new List<XYZ>();  // vị trí danh định thanh cột dưới
        public readonly List<XYZ> UpperPoints = new List<XYZ>();  // vị trí danh định thanh cột trên
        public readonly List<XYZ> LapPoints = new List<XYZ>();    // vị trí thanh dưới trên mạch ngừng (vùng nối)
        public readonly List<XYZ> UpperStarts = new List<XYZ>();  // vị trí thanh trên trong vùng nối
        public readonly List<double> LowerBend = new List<double>(); // mm, chiều dài uốn xiên thanh dưới (0 = thẳng)
        public readonly List<double> UpperBend = new List<double>(); // mm, chiều dài uốn xiên thanh trên (0 = thẳng)
        // Các danh sách trên đánh chỉ số theo thanh tầng DƯỚI. Terminated[i] = true: thanh không có đối ứng ở tầng trên, dừng tại mạch ngừng.
        public readonly List<bool> Terminated = new List<bool>();
        public int TerminatedLower, UnmatchedUpper;                  // số thanh tầng dưới dừng tại mạch ngừng / số thanh tầng trên không có đối ứng
    }

    public sealed class V3BuildReport
    {
        public int ColumnCount;
        public int MainBars;
        public int Ties;
        public int CrossTies;
        public int MainSets, TieSets, CrossTieSets;
        public readonly List<string> Warnings = new List<string>();
    }
}
