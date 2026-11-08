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
            if (works.Any(work => work.Config.Splice != V3SpliceStyle.None || work.Config.StaggerSplices ||
                work.HasNext || work.HasPrevious ||
                work.TransitionUp != null || work.TransitionDown != null))
                throw new InvalidOperationException("Không thể tạo nối chồng hoặc chuyển tiếp tự động. Cần chi tiết cấu tạo được thiết kế và duyệt.");

            var types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().ToList();
            var hooks = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().ToList();

            List<RebarHookType> hookTypes135 = hooks.Where(h =>
                Math.Abs(h.HookAngle - 3 * Math.PI / 4) < 1e-6).ToList();
            RebarHookType hook135 = hookTypes135.Count == 1 ? hookTypes135[0] : null;

            var hooks90 = hooks.Where(h => Math.Abs(h.HookAngle - Math.PI / 2) < 1e-6).ToList();
            RebarHookType hook90 = hooks90.Count == 1 ? hooks90[0] : null;
            V3RebarValidator.Preflight(works);
            foreach (var work in works)
            {
                if ((work.Config.TieStyle == V3TieStyle.Hook135 || work.Config.CrossTies) && hookTypes135.Count != 1)
                    throw new InvalidOperationException("Cần đúng một Rebar Hook Type 135°; hãy chuẩn hóa loại móc trong project.");

                if (work.Config.CrossTies && work.Config.CrossTiesAlternating && hook90 == null)
                    throw new InvalidOperationException("Đai phụ luân phiên cần đúng một Rebar Hook Type 90°; hãy chuẩn hóa loại móc trong project.");

                RebarBarType mainType = FindBarType(types, work.Config.MainDiameter);
                RebarBarType tieType = FindBarType(types, work.Config.TieDiameter);

                if (mainType == null || tieType == null)
                    throw new InvalidOperationException("Không tìm thấy loại thép đúng đường kính cho cột " + work.Column.Id.Value);

                var existing = ExistingRebar(doc, work.Column.Id);
                if (existing.Count > 0 && !replaceOld)
                    throw new InvalidOperationException("Cột đã có thép V3. Bật ghi đè để thay thế, tránh tạo trùng.");
                if (replaceOld && existing.Count > 0) doc.Delete(existing);

                if (work.FoundationHost != null)
                    V3RebarSetCreator.CreateFoundationAnchorSets(doc, work, mainType, report);
                else if (work.Config.GroupLongBars)
                    V3RebarSetCreator.CreateLongitudinalSets(doc, work, mainType, report);
                else
                    BuildMainBars(doc, work, mainType, report);
                V3RebarSetCreator.CreateTies(doc, work, tieType, hook135, hook90, report);

                report.ColumnCount++;
            }

            return report;
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
            double zMin = geo.Bottom + V3Units.Ft(cfg.Cover + cfg.MainDiameter / 2);
            double zMax = geo.Top - V3Units.Ft(cfg.Cover + cfg.MainDiameter / 2);

            for (int i = 0; i < barPoints.Count; i++)
            {
                XYZ basePt = barPoints[i];
                var curves = new List<Curve>();
                XYZ p0 = new XYZ(basePt.X, basePt.Y, zMin);

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
                    double topZ = cfg.StarterBars ? geo.Top + V3Units.Ft(cfg.StarterLength) : zMax;
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

    }
}
