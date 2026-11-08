using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    public static class V3RebarBuilder
    {
        public static string Partition(ElementId columnId)
        {
            return V3RebarOwnership.Partition(columnId.Value);
        }

        // 1. Hỗ trợ truyền Partition dạng string (Fix lỗi CS1503)
        public static void SetPartition(Rebar rebar, string partitionName)
        {
            if (rebar == null || string.IsNullOrEmpty(partitionName)) return;
            var p = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (p == null || p.IsReadOnly || !p.Set(partitionName))
                throw new InvalidOperationException("Không ghi được Partition V3 lên thép; hủy tạo để tránh mất dấu sở hữu.");
        }

        // 2. Hỗ trợ truyền Partition dạng ElementId (Fix lỗi CS1503)
        public static void SetPartition(Rebar rebar, ElementId columnId)
        {
            if (columnId != null) SetPartition(rebar, Partition(columnId));
        }

        // 3. Overload 1: Nhận V3SegmentWork (Fix lỗi CS7036 khi V3BarShapeDrawer gọi 1 tham số)
        public static List<XYZ> MainBarCenters(V3SegmentWork work)
        {
            if (work == null || work.Geometry == null || work.Config == null) return new List<XYZ>();
            var cfg = work.Config;
            return MainBarCenters(work.Geometry, cfg.Cx, cfg.Cy, cfg.Cover, cfg.TieDiameter, cfg.MainDiameter);
        }

        // 4. Overload 2: Nhận V3Geometry cùng đầy đủ thông số từ thiết kế.
        public static List<XYZ> MainBarCenters(V3Geometry geo, int cx, int cy, double coverMm, double tieDiaMm, double mainDiaMm)
        {
            List<XYZ> list = new List<XYZ>();
            if (geo == null || cx < 2 || cy < 2) return list;

            double coverFt = V3Units.Ft(coverMm);
            double tieDiaFt = V3Units.Ft(tieDiaMm);
            double mainDiaFt = V3Units.Ft(mainDiaMm);
            double inset = coverFt + tieDiaFt + mainDiaFt / 2.0;

            double halfW = geo.Width / 2.0 - inset;
            double halfD = geo.Depth / 2.0 - inset;

            if (halfW <= 0 || halfD <= 0) return list;

            // 4 góc
            list.Add(geo.At(-halfW, -halfD, 0));
            list.Add(geo.At(halfW, -halfD, 0));
            list.Add(geo.At(halfW, halfD, 0));
            list.Add(geo.At(-halfW, halfD, 0));

            // Các thanh cạnh Cx (phương X)
            for (int i = 1; i < cx - 1; i++)
            {
                double x = -halfW + (2.0 * halfW * i / (cx - 1));
                list.Add(geo.At(x, -halfD, 0));
                list.Add(geo.At(x, halfD, 0));
            }

            // Các thanh cạnh Cy (phương Y)
            for (int j = 1; j < cy - 1; j++)
            {
                double y = -halfD + (2.0 * halfD * j / (cy - 1));
                list.Add(geo.At(-halfW, y, 0));
                list.Add(geo.At(halfW, y, 0));
            }

            return list;
        }

        public static V3BuildReport Build(Document doc, IList<V3SegmentWork> works, bool replaceOld)
        {
            var report = new V3BuildReport();
            if (works == null || works.Count == 0) return report;
            foreach (var work in works) V3LegacySplice.ValidateForCreation(work.Config);
            if (works.Any(work => work.Config.Splice != V3SpliceStyle.None || work.Config.StaggerSplices ||
                work.HasNext || work.HasPrevious ||
                work.TransitionUp != null || work.TransitionDown != null))
                throw new InvalidOperationException("Không thể tạo nối chồng hoặc chuyển tiếp tự động. Cần chi tiết cấu tạo được thiết kế và duyệt.");

            ValidateProjectTypes(doc,works);
            var types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().ToList();
            V3RebarValidator.Preflight(works);
            using (var attempt = new SubTransaction(doc))
            {
                attempt.Start();
                try
                {
                    foreach (var work in works)
                    {
                        RebarBarType mainType = FindBarType(types, work.Config.MainDiameter);
                        RebarBarType tieType = FindBarType(types, work.Config.TieDiameter);
                        if (mainType == null || tieType == null)
                            throw new InvalidOperationException("Không tìm thấy loại thép đúng đường kính cho cột " + work.Column.Id.Value);

                        RebarHookType hook135=V3ProjectRebar.Hook(doc,tieType,work.Config.TieHook135UniqueId,135,
                            work.Config.TieStyle==V3TieStyle.Hook135 || work.Config.CrossTies);
                        RebarHookType hook90=V3ProjectRebar.Hook(doc,tieType,work.Config.TieHook90UniqueId,90,
                            work.Config.CrossTies && work.Config.CrossTiesAlternating);
                        var existing = ExistingRebar(doc, work.Column.Id);
                        if (existing.Count > 0 && !replaceOld)
                            throw new InvalidOperationException("Cột đã có thép V3. Bật ghi đè để thay thế, tránh tạo trùng.");
                        if (replaceOld && existing.Count > 0) doc.Delete(existing);

                        if (work.FoundationHost != null || work.BottleneckToUpper)
                            V3RebarSetCreator.CreateShapedLongitudinalSets(doc, work, mainType, report);
                        else if (work.Config.GroupLongBars)
                            V3RebarSetCreator.CreateLongitudinalSets(doc, work, mainType, report);
                        else
                            BuildMainBars(doc, work, mainType, report);
                        V3RebarSetCreator.CreateTies(doc, work, tieType, hook135, hook90, report);
                        report.ColumnCount++;
                    }
                    if (attempt.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận tạo thép từ Rebar Shape; thép V3 cũ được giữ nguyên.");
                }
                catch
                {
                    if (attempt.GetStatus() == TransactionStatus.Started) attempt.RollBack();
                    throw;
                }
            }

            return report;
        }

        internal static void ValidateProjectTypes(Document doc,IList<V3SegmentWork> works)
        {
            var types=new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().ToList();
            foreach(var work in works)
            {
                var type=FindBarType(types,work.Config.TieDiameter);
                if(type==null)throw new InvalidOperationException("Không tìm thấy loại thép đai D"+work.Config.TieDiameter+" trong project.");
                var mainType=FindBarType(types,work.Config.MainDiameter);
                V3RebarShapeFactory.Resolve(doc,work.Config.LongitudinalShapeUniqueId,
                    V3RebarShapeGroup.Longitudinal,work.Config.TieStyle,false,mainType,work.Column.Id);
                if(work.FoundationHost!=null || work.BottleneckToUpper)
                    V3RebarShapeFactory.Resolve(doc,work.Config.AnchorShapeUniqueId,
                        V3RebarShapeGroup.Anchor,work.Config.TieStyle,false,mainType,work.Column.Id);
                V3RebarShapeFactory.Resolve(doc,work.Config.OuterTieShapeUniqueId,
                    V3RebarShapeGroup.OuterTie,work.Config.TieStyle,false,type,work.Column.Id);
                if(work.Config.CrossTies)
                    V3RebarShapeFactory.Resolve(doc,work.Config.CrossTieShapeUniqueId,
                        V3RebarShapeGroup.CrossTie,work.Config.TieStyle,work.Config.CrossTiesAlternating,type,work.Column.Id);
                RebarHookType hook135=V3ProjectRebar.Hook(doc,type,work.Config.TieHook135UniqueId,135,
                    work.Config.TieStyle==V3TieStyle.Hook135 || work.Config.CrossTies);
                RebarHookType hook90=V3ProjectRebar.Hook(doc,type,work.Config.TieHook90UniqueId,90,
                    work.Config.CrossTies && work.Config.CrossTiesAlternating);
                if(work.Config.TieStyle==V3TieStyle.Hook135 && hook135==null)
                    throw new InvalidOperationException("Chưa chọn Hook 135° có sẵn cho cột "+work.Column.Id.Value+".");
                if(work.Config.TieStyle==V3TieStyle.Hook135 && !type.GetHookPermission(hook135.Id))
                    throw new InvalidOperationException("RebarBarType đai không cho phép Hook 135° đã chọn ở cột "+work.Column.Id.Value+".");
                if(work.Config.CrossTies && hook135==null)
                    throw new InvalidOperationException("Chưa chọn Hook 135° cho đai phụ ở cột "+work.Column.Id.Value+".");
                if(work.Config.CrossTiesAlternating && work.Config.CrossTies && hook90==null)
                    throw new InvalidOperationException("Chưa chọn Hook 90° cho đai phụ luân phiên ở cột "+work.Column.Id.Value+".");
                if(work.Config.CrossTiesAlternating && work.Config.CrossTies &&
                    !type.GetHookPermission(hook90.Id))
                    throw new InvalidOperationException("RebarBarType đai không cho phép Hook 90° đã chọn ở cột "+work.Column.Id.Value+".");
                if(mainType==null)throw new InvalidOperationException("Không tìm thấy loại thép dọc đúng đường kính cho cột "+work.Column.Id.Value+".");
                if(work.FoundationHost!=null || work.BottleneckToUpper)
                    foreach(var path in V3RebarSetCreator.ConnectionPaths(work))
                    {
                        path.Inspect();
                        if(!path.Planar)throw new InvalidOperationException("Cột "+work.Column.Id.Value+
                            ": thanh neo L kết hợp cổ chai nằm nhiều mặt phẳng. Chưa hỗ trợ dựng thanh này từ Rebar Shape có sẵn; không tự chuyển sang FreeForm. Kiểm tra lại hướng neo/nhấn.");
                    }
            }
        }

        private static List<ElementId> ExistingRebar(Document doc, ElementId columnId)
        {
            // Legacy V3 bars also use this exact partition, including bars hosted by a foundation.
            // Host alone is never evidence of V3 ownership.
            return new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(r => V3RebarOwnership.Matches(columnId.Value,
                    r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString()))
                .Select(r => r.Id).ToList();
        }

        private static RebarBarType FindBarType(List<RebarBarType> types, double diameterMm)
        {
            List<RebarBarType> matches = types
                .Where(t => !t.Name.StartsWith(V3BottleneckBarType.Prefix, StringComparison.Ordinal) &&
                    Math.Abs(V3Units.Mm(t.BarNominalDiameter) - diameterMm) <= .01)
                .ToList();
            if (matches.Count > 1)
                throw new InvalidOperationException("Có nhiều RebarBarType cùng đường kính " +
                    diameterMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    " mm. Hãy chuẩn hóa loại thép trong Project; KStruct không tự chọn.");
            return matches.SingleOrDefault();
        }

        private static void BuildMainBars(Document doc, V3SegmentWork work, RebarBarType barType, V3BuildReport report)
        {
            var geo = work.Geometry;
            var cfg = work.Config;
            List<XYZ> barPoints = MainBarCenters(work);
            double zMin = geo.Bottom + V3Units.Ft(cfg.Cover + cfg.MainDiameter / 2);
            double zMax = geo.Top - V3Units.Ft(cfg.Cover + cfg.MainDiameter / 2);
            RebarShape shape = V3RebarShapeFactory.Resolve(doc, cfg.LongitudinalShapeUniqueId,
                V3RebarShapeGroup.Longitudinal, cfg.TieStyle, false, barType, work.Column.Id);
            for (int i = 0; i < barPoints.Count; i++)
            {
                XYZ basePt = barPoints[i];
                XYZ p0 = new XYZ(basePt.X, basePt.Y, zMin);
                double topZ = cfg.StarterBars ? geo.Top + V3Units.Ft(cfg.StarterLength) : zMax;
                XYZ p1 = new XYZ(basePt.X, basePt.Y, topZ);
                XYZ widthAxis=geo.X;
                XYZ xVector=XYZ.BasisZ*(topZ-p0.Z);
                XYZ yVector=widthAxis*barType.BarNominalDiameter;
                Rebar rebar = V3RebarShapeFactory.Create(doc, shape, barType, work.Column, p0,
                    xVector, yVector, work.Column.Id, "thanh dọc");
                V3RebarShapeFactory.ValidateEnvelope(rebar, new[] { p0, p1 }, V3Units.Ft(2),
                    work.Column.Id, "thanh dọc");
                SetPartition(rebar, work.Column.Id);
                report.MainBars++;
            }
        }

    }
}
