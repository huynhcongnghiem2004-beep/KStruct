using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.FootingRebar
{
    /// <summary>
    /// Khung tọa độ của thân móng/đài: hộp bao trong hệ tọa độ family + phép biến đổi ra thế giới.
    /// Hệ của lõi: gốc = góc dưới (min) của hộp bao thân bê tông kết cấu; lớp bê tông lót không thuộc khung.
    /// </summary>
    public sealed class FootingFrame
    {
        public FamilyInstance Instance { get; }
        public Transform LocalToWorld { get; }
        /// <summary>Góc min của hộp bao thân móng trong hệ family (feet).</summary>
        public XYZ LocalMin { get; }
        /// <summary>Kích thước thân móng/đài (mm), không gồm bê tông lót được nhận diện.</summary>
        public FootingModel Model { get; }
        /// <summary>Nguồn kích thước và phương pháp chọn hình học.</summary>
        public string Source { get; }
        /// <summary>True khi hình học family có một solid bê tông lót riêng nằm dưới thân móng.</summary>
        public bool HasEmbeddedLeanConcrete { get; }

        public FootingFrame(FamilyInstance instance, Transform localToWorld, XYZ localMin,
                            FootingModel model, string source, bool hasEmbeddedLeanConcrete = false)
        {
            Instance = instance; LocalToWorld = localToWorld; LocalMin = localMin; Model = model; Source = source;
            HasEmbeddedLeanConcrete = hasEmbeddedLeanConcrete;
        }

        public XYZ ToWorld(Point3 pMm)
        {
            var local = new XYZ(
                LocalMin.X + UnitConv.ToFeet(pMm.X),
                LocalMin.Y + UnitConv.ToFeet(pMm.Y),
                LocalMin.Z + UnitConv.ToFeet(pMm.Z));
            return LocalToWorld.OfPoint(local);
        }

        public XYZ ToWorldDirection(XYZ localDir) => LocalToWorld.OfVector(localDir).Normalize();

        /// <summary>Hộp song song trục thế giới bao quanh thân móng (dùng cho post-check).</summary>
        public void WorldEnvelope(out XYZ min, out XYZ max)
        {
            double lx = UnitConv.ToFeet(Model.LengthX), ly = UnitConv.ToFeet(Model.WidthY), lz = UnitConv.ToFeet(Model.Height);
            double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue;
            double x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = LocalToWorld.OfPoint(new XYZ(
                    LocalMin.X + ((i & 1) != 0 ? lx : 0),
                    LocalMin.Y + ((i & 2) != 0 ? ly : 0),
                    LocalMin.Z + ((i & 4) != 0 ? lz : 0)));
                x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z);
                x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z);
            }
            min = new XYZ(x0, y0, z0); max = new XYZ(x1, y1, z1);
        }
    }

    public static class GeometryReader
    {
        private sealed class Acc
        {
            public double X0 = double.MaxValue, Y0 = double.MaxValue, Z0 = double.MaxValue;
            public double X1 = double.MinValue, Y1 = double.MinValue, Z1 = double.MinValue;
            public bool Any;
            public int Instances, Solids, Meshes;
            public readonly List<SolidProfile> SolidProfiles = new List<SolidProfile>();

            public void Add(XYZ p)
            {
                Any = true;
                X0 = Math.Min(X0, p.X); Y0 = Math.Min(Y0, p.Y); Z0 = Math.Min(Z0, p.Z);
                X1 = Math.Max(X1, p.X); Y1 = Math.Max(Y1, p.Y); Z1 = Math.Max(Z1, p.Z);
            }

            public void Include(Acc other)
            {
                if (other == null || !other.Any) return;
                Add(new XYZ(other.X0, other.Y0, other.Z0));
                Add(new XYZ(other.X1, other.Y1, other.Z1));
            }
        }

        private sealed class SolidProfile
        {
            public Acc Bounds;
            public double Volume;
            public double BottomArea;
        }

        public static FootingFrame Read(FamilyInstance fi)
        {
            Transform instT = fi.GetTransform();
            string log = "";

            foreach (bool nonVisible in new[] { false, true })
            foreach (ViewDetailLevel level in new[] { ViewDetailLevel.Fine, ViewDetailLevel.Medium, ViewDetailLevel.Coarse })
            {
                var opt = new Options { DetailLevel = level, IncludeNonVisibleObjects = nonVisible };
                GeometryElement ge = fi.get_Geometry(opt);
                if (ge == null) continue;

                // Cách A: hình học symbol trong hệ tọa độ family (không bị cắt bởi join).
                var a = new Acc();
                Transform top = null;
                foreach (GeometryObject go in ge)
                {
                    var gi = go as GeometryInstance;
                    if (gi == null) continue;
                    top = gi.Transform;
                    a.Instances++;
                    Collect(gi.GetSymbolGeometry(), Transform.Identity, a, true);
                }
                FootingFrame f = TryBuild(fi, top ?? instT, a, "Solid trong family");
                if (f != null) return f;

                // Cách B: hình học instance, đưa ngược về hệ family bằng transform của instance.
                var b = new Acc();
                Collect(ge, instT.Inverse, b, false);
                f = TryBuild(fi, instT, b, "Solid trong view (có thể đã bị cắt bởi join)");
                if (f != null) return f;

                log += string.Format("[{0}/{1}: instance={2}, solid={3}, mesh={4}] ",
                    level, nonVisible ? "gồm ẩn" : "chỉ hiện", a.Instances, a.Solids, a.Meshes);
            }

            // Cách C: BoundingBox của phần tử. Đây chỉ là dự phòng khi không đọc được solid.
            BoundingBoxXYZ bb = fi.get_BoundingBox(null);
            if (bb != null)
            {
                var c = new Acc();
                Transform inv = instT.Inverse;
                for (int i = 0; i < 8; i++)
                    c.Add(inv.OfPoint(new XYZ(
                        (i & 1) != 0 ? bb.Max.X : bb.Min.X,
                        (i & 2) != 0 ? bb.Max.Y : bb.Min.Y,
                        (i & 4) != 0 ? bb.Max.Z : bb.Min.Z)));
                FootingFrame f = TryBuild(fi, instT, c, "BoundingBox (không đọc được solid, kích thước có thể lớn hơn thực tế nếu móng xoay)");
                if (f != null) return f;
            }

            throw new InvalidOperationException(
                "Không đọc được hình học móng. Family: " + fi.Symbol.FamilyName +
                ". Chi tiết: " + log);
        }

        private static FootingFrame TryBuild(FamilyInstance fi, Transform t, Acc all, string source)
        {
            if (all == null || !all.Any || t == null) return null;
            if (t.BasisZ.Z < 0.999)
                throw new InvalidOperationException("Chỉ hỗ trợ móng đặt thẳng đứng (trục Z hướng lên).");

            bool hasLeanConcrete = false;
            Acc bounds = SelectStructuralBounds(fi, all, out hasLeanConcrete, out string selectionNote);
            if (bounds == null || !bounds.Any) return null;
            try
            {
                var model = new FootingModel(
                    UnitConv.ToMm(bounds.X1 - bounds.X0), UnitConv.ToMm(bounds.Y1 - bounds.Y0), UnitConv.ToMm(bounds.Z1 - bounds.Z0));
                string resolvedSource = string.IsNullOrEmpty(selectionNote) ? source : source + " (" + selectionNote + ")";
                return new FootingFrame(fi, t, new XYZ(bounds.X0, bounds.Y0, bounds.Z0), model, resolvedSource, hasLeanConcrete);
            }
            catch (ArgumentException) { return null; }
        }

        private static Acc SelectStructuralBounds(FamilyInstance fi, Acc all, out bool hasLeanConcrete, out string note)
        {
            hasLeanConcrete = false;
            note = null;
            if (all.SolidProfiles.Count < 2) return all;

            double length = GetFamilyDimension(fi, "Length", "Lendth");
            double width = GetFamilyDimension(fi, "Width");
            double thickness = GetFamilyDimension(fi, "Foundation Thickness");
            double tolerance = UnitUtils.ConvertToInternalUnits(25, UnitTypeId.Millimeters);
            Func<double, double, bool> matches = (actual, expected) => expected <= 0 || Math.Abs(actual - expected) <= tolerance;

            SolidProfile cap = all.SolidProfiles
                .Where(p => p.Bounds.Any && p.Volume > 1e-9)
                .Where(p =>
                {
                    double sx = p.Bounds.X1 - p.Bounds.X0;
                    double sy = p.Bounds.Y1 - p.Bounds.Y0;
                    double sz = p.Bounds.Z1 - p.Bounds.Z0;
                    bool planMatches = (matches(sx, length) && matches(sy, width)) ||
                                       (matches(sx, width) && matches(sy, length));
                    return length > 0 && width > 0 && planMatches && matches(sz, thickness);
                })
                .OrderByDescending(p => p.BottomArea)
                .ThenByDescending(p => p.Volume)
                .FirstOrDefault();

            if (cap != null)
            {
                hasLeanConcrete = all.SolidProfiles.Any(p => p != cap && IsThinLayerBelow(p, cap, false));
                note = hasLeanConcrete
                    ? "đã chọn solid thân đài theo Length/Width/ Foundation Thickness; đã loại lớp lót dưới đài"
                    : "đã chọn solid thân đài theo Length/Width/Foundation Thickness";
                return cap.Bounds;
            }

            // Family không có/không khớp đủ tham số: chỉ loại lớp lót khi có dấu hiệu hình học rõ
            // là một solid mỏng, nằm dưới thân chính, phủ gần hết footprint và nhô ra ngoài thân đài.
            SolidProfile main = all.SolidProfiles
                .Where(p => p.Bounds.Any && p.Volume > 1e-9)
                .OrderByDescending(p => p.Volume)
                .FirstOrDefault();
            if (main != null)
            {
                List<SolidProfile> liners = all.SolidProfiles
                    .Where(p => p != main && IsThinLayerBelow(p, main, true)).ToList();
                if (liners.Count == 1)
                {
                    hasLeanConcrete = true;
                    note = "đã loại solid bê tông lót mỏng nằm dưới thân chính";
                    return main.Bounds;
                }
            }
            return all;
        }

        private static bool IsThinLayerBelow(SolidProfile candidate, SolidProfile body, bool requireOverhang)
        {
            if (candidate?.Bounds == null || body?.Bounds == null || !candidate.Bounds.Any || !body.Bounds.Any)
                return false;

            double maxThickness = UnitUtils.ConvertToInternalUnits(requireOverhang ? 150 : 200, UnitTypeId.Millimeters);
            double contactTolerance = UnitUtils.ConvertToInternalUnits(25, UnitTypeId.Millimeters);
            double layerThickness = candidate.Bounds.Z1 - candidate.Bounds.Z0;
            double bodyThickness = body.Bounds.Z1 - body.Bounds.Z0;
            if (layerThickness <= UnitUtils.ConvertToInternalUnits(20, UnitTypeId.Millimeters) ||
                layerThickness > maxThickness || bodyThickness < layerThickness * 1.5)
                return false;

            double gap = body.Bounds.Z0 - candidate.Bounds.Z1;
            if (gap < -UnitUtils.ConvertToInternalUnits(5, UnitTypeId.Millimeters) || gap > contactTolerance ||
                candidate.Bounds.Z0 >= body.Bounds.Z0)
                return false;

            double bodyX = body.Bounds.X1 - body.Bounds.X0;
            double bodyY = body.Bounds.Y1 - body.Bounds.Y0;
            double bodyArea = Math.Max(1e-9, bodyX * bodyY);
            double overlapX = Math.Max(0, Math.Min(body.Bounds.X1, candidate.Bounds.X1) - Math.Max(body.Bounds.X0, candidate.Bounds.X0));
            double overlapY = Math.Max(0, Math.Min(body.Bounds.Y1, candidate.Bounds.Y1) - Math.Max(body.Bounds.Y0, candidate.Bounds.Y0));
            if ((overlapX * overlapY) / bodyArea < (requireOverhang ? 0.90 : 0.70)) return false;

            if (requireOverhang)
            {
                double layerArea = (candidate.Bounds.X1 - candidate.Bounds.X0) * (candidate.Bounds.Y1 - candidate.Bounds.Y0);
                if (layerArea < bodyArea * 1.02) return false;
            }
            return true;
        }

        private static double GetFamilyDimension(FamilyInstance fi, params string[] names)
        {
            foreach (string name in names)
            {
                Parameter parameter = fi.LookupParameter(name);
                if ((parameter == null || !parameter.HasValue) && fi.Symbol != null)
                    parameter = fi.Symbol.LookupParameter(name);
                if (parameter != null && parameter.HasValue && parameter.StorageType == StorageType.Double)
                {
                    double value = parameter.AsDouble();
                    if (value > 0 && !double.IsNaN(value) && !double.IsInfinity(value)) return value;
                }
            }
            return 0;
        }

        /// <param name="map">Biến đổi từ hệ của ge sang hệ đích (family).</param>
        /// <param name="symbolMode">true: gặp GeometryInstance lồng thì đi vào symbol geometry; false: dùng instance geometry.</param>
        private static void Collect(GeometryElement ge, Transform map, Acc acc, bool symbolMode)
        {
            if (ge == null) return;
            foreach (GeometryObject go in ge)
            {
                var gi = go as GeometryInstance;
                if (gi != null)
                {
                    acc.Instances++;
                    if (symbolMode) Collect(gi.GetSymbolGeometry(), map.Multiply(gi.Transform), acc, true);
                    else Collect(gi.GetInstanceGeometry(), map, acc, false);
                    continue;
                }

                var solid = go as Solid;
                if (solid != null && solid.Faces.Size > 0 && solid.Volume > 1e-9)
                {
                    acc.Solids++;
                    var solidBounds = new Acc();
                    foreach (Edge edge in solid.Edges)
                    {
                        Curve curve = edge.AsCurve();
                        solidBounds.Add(map.OfPoint(curve.GetEndPoint(0)));
                        solidBounds.Add(map.OfPoint(curve.GetEndPoint(1)));
                    }
                    if (solid.Edges.Size == 0)
                        foreach (Face face in solid.Faces)
                            foreach (XYZ vertex in face.Triangulate().Vertices)
                                solidBounds.Add(map.OfPoint(vertex));

                    acc.Include(solidBounds);
                    double bottomArea = solid.Faces.Cast<Face>().OfType<PlanarFace>()
                        .Where(face => map.OfVector(face.FaceNormal).Normalize().Z <= -0.995)
                        .Select(face => face.Area).DefaultIfEmpty(0).Max();
                    if (solidBounds.Any)
                        acc.SolidProfiles.Add(new SolidProfile { Bounds = solidBounds, Volume = Math.Abs(solid.Volume), BottomArea = bottomArea });
                    continue;
                }

                var mesh = go as Mesh;
                if (mesh != null)
                {
                    acc.Meshes++;
                    foreach (XYZ vertex in mesh.Vertices) acc.Add(map.OfPoint(vertex));
                }
            }
        }
    }
}
