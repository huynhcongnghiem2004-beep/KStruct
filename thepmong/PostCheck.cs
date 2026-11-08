using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.KStruct.FootingRebar
{
    /// <summary>Kiểm tra độc lập SAU khi tạo: số thanh thực tế và vị trí so với móng.</summary>
    public static class PostCheck
    {
        private static readonly double Tol = UnitConv.ToFeet(1.0); // 1 mm

        public static List<string> Verify(Document doc, IList<CreatedSet> created, FootingFrame frame, CoverSettings cover)
        {
            var issues = new List<string>();
            Transform worldToLocal = frame.LocalToWorld.Inverse;
            const double toleranceMm = 1.0;

            foreach (CreatedSet c in created)
            {
                var rebar = doc.GetElement(c.RebarId) as Rebar;
                if (rebar == null)
                {
                    issues.Add(c.Set.Name + ": không tìm thấy thanh thép vừa tạo.");
                    continue;
                }

                if (rebar.NumberOfBarPositions != c.Set.Count)
                    issues.Add(string.Format("{0}: kế hoạch {1} thanh nhưng Revit có {2}.",
                        c.Set.Name, c.Set.Count, rebar.NumberOfBarPositions));

                RebarBarType actualType = doc.GetElement(rebar.GetTypeId()) as RebarBarType;
                double actualDiameterMm = actualType == null
                    ? c.Set.Diameter
                    : UnitConv.ToMm(actualType.BarModelDiameter);
                if (Math.Abs(actualDiameterMm - c.Set.Diameter) > 0.5)
                {
                    issues.Add(string.Format("{0}: kế hoạch Ø{1:0.#} mm nhưng Revit đang dùng Ø{2:0.#} mm.",
                        c.Set.Name, c.Set.Diameter, actualDiameterMm));
                }
                double radiusMm = actualDiameterMm / 2.0;
                bool outsideCover = false;
                for (int barIndex = 0; barIndex < rebar.NumberOfBarPositions && !outsideCover; barIndex++)
                {
                    IList<Curve> curves;
                    try
                    {
                        curves = rebar.GetTransformedCenterlineCurves(
                            false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, barIndex);
                    }
                    catch (Exception ex)
                    {
                        issues.Add(c.Set.Name + ": không đọc được hình học thanh thực tế để kiểm tra cover — " + ex.Message);
                        outsideCover = true;
                        break;
                    }

                    if (curves == null || curves.Count == 0)
                    {
                        issues.Add(c.Set.Name + ": Revit không trả hình học thanh để kiểm tra cover.");
                        outsideCover = true;
                        break;
                    }

                    foreach (Curve curve in curves)
                    foreach (XYZ worldPoint in curve.Tessellate())
                    {
                        XYZ local = worldToLocal.OfPoint(worldPoint);
                        double x = UnitConv.ToMm(local.X - frame.LocalMin.X);
                        double y = UnitConv.ToMm(local.Y - frame.LocalMin.Y);
                        double z = UnitConv.ToMm(local.Z - frame.LocalMin.Z);
                        bool inside =
                            x - radiusMm >= cover.Side - toleranceMm && x + radiusMm <= frame.Model.LengthX - cover.Side + toleranceMm &&
                            y - radiusMm >= cover.Side - toleranceMm && y + radiusMm <= frame.Model.WidthY - cover.Side + toleranceMm &&
                            z - radiusMm >= cover.Bottom - toleranceMm && z + radiusMm <= frame.Model.Height - cover.Top + toleranceMm;
                        if (!inside)
                        {
                            issues.Add(string.Format("{0}: thanh số {1} vượt mặt đài/không đủ cover {2:0.#} mm.",
                                c.Set.Name, barIndex + 1, cover.Side));
                            outsideCover = true;
                            break;
                        }
                    }
                }
            }
            return issues;
        }
    }
}
