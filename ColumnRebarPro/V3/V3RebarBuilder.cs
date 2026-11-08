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
            return "KSTR_V3_" + columnId.Value;
        }

        // 1. Hỗ trợ truyền Partition dạng string (Fix lỗi CS1503)
        public static void SetPartition(Rebar rebar, string partitionName)
        {
            if (rebar == null || string.IsNullOrEmpty(partitionName)) return;
            var p = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (p != null && !p.IsReadOnly)
            {
                p.Set(partitionName);
            }
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
            if (works.Any(work => work.Config.Splice != V3SpliceStyle.None || work.Config.StaggerSplices ||
                work.HasNext || work.HasPrevious ||
                work.TransitionUp != null || work.TransitionDown != null))
                throw new InvalidOperationException("Không thể tạo nối chồng hoặc chuyển tiếp tự động. Cần chi tiết cấu tạo được thiết kế và duyệt.");

            var types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().ToList();
            var hooks = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().ToList();

            List<RebarHookType> hookTypes135 = hooks.Where(h =>
                h.Name.IndexOf("135", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            RebarHookType hook135 = hookTypes135.Count == 1 ? hookTypes135[0] : null;

            foreach (var work in works)
            {
                if (replaceOld) ClearExistingRebar(doc, work.Column.Id);

                if ((work.Config.TieStyle == V3TieStyle.Hook135 || work.Config.CrossTies) && hookTypes135.Count != 1)
                    throw new InvalidOperationException("Project chưa có Rebar Hook Type 135° cần theo cấu hình đã duyệt.");

                RebarBarType mainType = FindBarType(types, work.Config.MainDiameter);
                RebarBarType tieType = FindBarType(types, work.Config.TieDiameter);

                if (mainType == null || tieType == null)
                {
                    report.Warnings.Add("Không tìm thấy RebarBarType đường kính " + work.Config.MainDiameter + "mm hoặc " + work.Config.TieDiameter + "mm cho cột " + work.Column.Id.Value);
                    continue;
                }

                // 1. Tạo Thép Dọc
                BuildMainBars(doc, work, mainType, report);

                // 2. Tạo Thép Đai Chính (Fix lỗi CS0103)
                BuildOuterTies(doc, work, tieType, hook135, report);

                // 3. Tạo Thép Đai Phụ (Fix lỗi CS0103)
                if (work.Config.CrossTies)
                {
                    BuildCrossTies(doc, work, tieType, hook135, report);
                }

                report.ColumnCount++;
            }

            return report;
        }

        private static void ClearExistingRebar(Document doc, ElementId columnId)
        {
            string part = Partition(columnId);
            var rebars = new FilteredElementCollector(doc)
                .OfClass(typeof(Rebar))
                .Cast<Rebar>()
                .Where(r => r.GetHostId() == columnId || (r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM) != null && r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM).AsString() == part))
                .Select(r => r.Id)
                .ToList();

            if (rebars.Count > 0)
            {
                doc.Delete(rebars);
            }
        }

        private static RebarBarType FindBarType(List<RebarBarType> types, double diameterMm)
        {
            List<RebarBarType> matches = types
                .Where(t => Math.Abs(V3Units.Mm(t.BarNominalDiameter) - diameterMm) <= .01)
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
            double zMin = geo.Bottom + V3Units.Ft(cfg.Cover);
            double zMax = geo.Top;

            for (int i = 0; i < barPoints.Count; i++)
            {
                XYZ basePt = barPoints[i];
                var curves = new List<Curve>();
                XYZ p0 = new XYZ(basePt.X, basePt.Y, zMin);

                if (work.FoundationHost != null)
                {
                    double diameter = cfg.MainDiameter;
                    double verticalEmbedMm = V3Units.Mm(geo.Bottom - work.FoundationBendZ);
                    double horizontalMm = work.FoundationAnchorLengthMm - verticalEmbedMm;
                    if (horizontalMm <= 0)
                        throw new InvalidOperationException("Chiều dài neo theo thiết kế không đủ cho đoạn neo đứng trong móng.");
                    double hx = geo.Width / 2 - V3Units.Ft(cfg.Cover + cfg.TieDiameter + diameter / 2);
                    double hy = geo.Depth / 2 - V3Units.Ft(cfg.Cover + cfg.TieDiameter + diameter / 2);
                    XYZ inward = V3TransitionPlanner.InwardDirection(basePt, geo, hx, hy);
                    XYZ bend = new XYZ(basePt.X, basePt.Y, work.FoundationBendZ);
                    XYZ anchor = bend + inward * V3Units.Ft(horizontalMm);
                    curves.Add(Line.CreateBound(anchor, bend));
                    curves.Add(Line.CreateBound(bend, p0));
                }

                V3Transition tr = work.TransitionUp;
                if (tr != null && i < tr.Terminated.Count && tr.Terminated[i])
                {
                    curves.Add(Line.CreateBound(p0, new XYZ(basePt.X, basePt.Y, zMax)));
                }
                else if (tr != null && i < tr.UpperStarts.Count)
                {
                    double zJoint = tr.JointZ;
                    double ext = V3Units.Ft(cfg.StarterBars ? cfg.StarterLength : tr.LapLength);
                    double bendFt = V3Units.Ft(tr.LowerBend[i]);
                    double tol = doc.Application.ShortCurveTolerance;
                    XYZ p1 = new XYZ(tr.LapPoints[i].X, tr.LapPoints[i].Y, zJoint);
                    XYZ p2 = new XYZ(tr.UpperStarts[i].X, tr.UpperStarts[i].Y, zJoint);
                    XYZ p3 = new XYZ(p2.X, p2.Y, zJoint + ext);

                    if (bendFt > tol)
                    {
                        XYZ pA = new XYZ(basePt.X, basePt.Y, zJoint - bendFt);
                        curves.Add(Line.CreateBound(p0, pA));
                        curves.Add(Line.CreateBound(pA, p1));
                    }
                    else curves.Add(Line.CreateBound(p0, p1));
                    if (p1.DistanceTo(p2) > tol) curves.Add(Line.CreateBound(p1, p2));
                    curves.Add(Line.CreateBound(p2, p3));
                }
                else
                {
                    double topZ = cfg.StarterBars ? zMax + V3Units.Ft(cfg.StarterLength) : zMax;
                    curves.Add(Line.CreateBound(p0, new XYZ(basePt.X, basePt.Y, topZ)));
                }

                XYZ planeNormal = GetCurvePlaneNormal(curves, geo.X);
                Rebar rebar = Rebar.CreateFromCurves(
                    doc, RebarStyle.Standard, barType, null, null,
                    work.FoundationHost ?? (Element)work.Column,
                    planeNormal, curves, RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);

                if (rebar != null)
                {
                    SetPartition(rebar, work.Column.Id);
                    report.MainBars++;
                    report.MainSets++;
                }
            }
        }
        private static XYZ GetCurvePlaneNormal(IList<Curve> curves, XYZ fallback)
        {
            if (curves == null || curves.Count < 2) return fallback;
            XYZ origin = curves[0].GetEndPoint(0);
            XYZ first = null;
            double tolerance = 1e-8;
            foreach (Curve curve in curves)
            {
                XYZ direction = curve.GetEndPoint(1) - curve.GetEndPoint(0);
                if (direction.GetLength() <= tolerance) continue;
                if (first == null)
                {
                    first = direction.Normalize();
                    continue;
                }

                XYZ offset = curve.GetEndPoint(0) - origin;
                XYZ normal = first.CrossProduct(direction);
                if (normal.GetLength() <= tolerance) normal = first.CrossProduct(offset);
                if (normal.GetLength() > tolerance) return normal.Normalize();
            }
            return fallback;
        }

        // Định nghĩa phương thức BuildOuterTies
        private static void BuildOuterTies(Document doc, V3SegmentWork work, RebarBarType tieType, RebarHookType hook, V3BuildReport report)
        {
            var geo = work.Geometry;
            var cfg = work.Config;

            double coverFt = V3Units.Ft(cfg.Cover);
            double tieRadiusFt = V3Units.Ft(cfg.TieDiameter) / 2.0;
            double inset = coverFt + tieRadiusFt;

            double halfW = geo.Width / 2.0 - inset;
            double halfD = geo.Depth / 2.0 - inset;

            XYZ p1 = geo.At(-halfW, -halfD, geo.Bottom + inset);
            XYZ p2 = geo.At(halfW, -halfD, geo.Bottom + inset);
            XYZ p3 = geo.At(halfW, halfD, geo.Bottom + inset);
            XYZ p4 = geo.At(-halfW, halfD, geo.Bottom + inset);

            List<Curve> curves = new List<Curve>
            {
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4),
                Line.CreateBound(p4, p1)
            };

            Rebar tie = Rebar.CreateFromCurves(
                doc,
                RebarStyle.StirrupTie,
                tieType,
                hook,
                hook,
                work.Column,
                XYZ.BasisZ,
                curves,
                RebarHookOrientation.Left,
                RebarHookOrientation.Left,
                true,
                true
            );

            if (tie != null)
            {
                SetPartition(tie, work.Column.Id);

                double totalHeight = geo.Height - 2.0 * inset;
                double stepMidFt = V3Units.Ft(cfg.MidSpacing);

                RebarShapeDrivenAccessor accessor = tie.GetShapeDrivenAccessor();

                if (cfg.TieLayout == V3TieLayout.MaximumSpacing)
                {
                    accessor.SetLayoutAsMaximumSpacing(stepMidFt, totalHeight, true, true, true);
                }
                else
                {
                    accessor.SetLayoutAsNumberWithSpacing(cfg.MidTieCount, stepMidFt, true, true, true);
                }

                report.Ties += tie.Quantity;
                report.TieSets++;
            }
        }

        // Định nghĩa phương thức BuildCrossTies
        private static void BuildCrossTies(Document doc, V3SegmentWork work, RebarBarType tieType, RebarHookType hook, V3BuildReport report)
        {
            var geo = work.Geometry;
            var cfg = work.Config;

            double coverFt = V3Units.Ft(cfg.Cover);
            double tieDiaFt = V3Units.Ft(cfg.TieDiameter);
            double mainDiaFt = V3Units.Ft(cfg.MainDiameter);
            double inset = coverFt + tieDiaFt + mainDiaFt / 2.0;

            double halfW = geo.Width / 2.0 - inset;
            double halfD = geo.Depth / 2.0 - inset;

            double zLevel = geo.Bottom + coverFt + tieDiaFt;

            for (int i = 1; i < cfg.Cx - 1; i++)
            {
                double x = -halfW + (2.0 * halfW * i / (cfg.Cx - 1));
                XYZ start = geo.At(x, -halfD, zLevel);
                XYZ end = geo.At(x, halfD, zLevel);

                CreateSingleCrossTie(doc, work, tieType, hook, start, end, report);
            }

            for (int j = 1; j < cfg.Cy - 1; j++)
            {
                double y = -halfD + (2.0 * halfD * j / (cfg.Cy - 1));
                XYZ start = geo.At(-halfW, y, zLevel);
                XYZ end = geo.At(halfW, y, zLevel);

                CreateSingleCrossTie(doc, work, tieType, hook, start, end, report);
            }
        }

        private static void CreateSingleCrossTie(Document doc, V3SegmentWork work, RebarBarType tieType, RebarHookType hook, XYZ start, XYZ end, V3BuildReport report)
        {
            List<Curve> curves = new List<Curve> { Line.CreateBound(start, end) };

            Rebar crossTie = Rebar.CreateFromCurves(
                doc,
                RebarStyle.StirrupTie,
                tieType,
                hook,
                hook,
                work.Column,
                (end - start).CrossProduct(XYZ.BasisZ).Normalize(),
                curves,
                RebarHookOrientation.Left,
                RebarHookOrientation.Right,
                true,
                true
            );

            if (crossTie != null)
            {
                SetPartition(crossTie, work.Column.Id);

                double totalHeight = work.Geometry.Height - (2.0 * V3Units.Ft(work.Config.Cover));
                double stepMidFt = V3Units.Ft(work.Config.MidSpacing);

                RebarShapeDrivenAccessor accessor = crossTie.GetShapeDrivenAccessor();
                accessor.SetLayoutAsMaximumSpacing(stepMidFt, totalHeight, true, true, true);

                report.CrossTies += crossTie.Quantity;
                report.CrossTieSets++;
            }
        }
    }
}
