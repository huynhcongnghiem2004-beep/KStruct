using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    internal sealed class V3BeamJointInfo
    {
        internal ElementId BeamId;
        internal string BeamName;
        internal double HeightMm;
        internal double BottomZ;
        internal double TopZ;
        internal double TopCoverMm;
        internal int MatchingBeamCount;
    }

    /// <summary>
    /// Reads beam geometry and its explicit Rebar Cover - Top Face value.
    /// It only resolves a beam when its faces align with the two column ends.
    /// </summary>
    internal static class V3BeamJointDetector
    {
        private const double FaceToleranceMm = 25;
        private const double XYMarginMm = 50;

        internal static bool TryDetect(Document doc, V3SegmentWork lower,
            V3SegmentWork upper, out V3BeamJointInfo result, out string reason)
        {
            result = null;
            reason = null;
            if (doc == null || lower?.Column == null || upper?.Column == null)
            {
                reason = "Không có đủ dữ liệu cột để tự nhận diện dầm.";
                return false;
            }

            var lowerBox = lower.Column.get_BoundingBox(null);
            var upperBox = upper.Column.get_BoundingBox(null);
            if (lowerBox == null || upperBox == null)
            {
                reason = "Không đọc được BoundingBox của một trong hai cột.";
                return false;
            }

            var projected = new List<Tuple<Element, BoundingBoxXYZ>>();
            var misaligned = new List<string>();
            var missingCover = new List<string>();
            foreach (Element beam in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType())
            {
                var familyBeam = beam as FamilyInstance;
                var location = beam.Location as LocationCurve;
                var line = location?.Curve as Line;
                var box = beam.get_BoundingBox(null);
                if (familyBeam == null || familyBeam.StructuralType != StructuralType.Beam ||
                    line == null || box == null || !line.IsBound)
                    continue;

                XYZ p0 = line.GetEndPoint(0), p1 = line.GetEndPoint(1);
                if (Math.Abs(V3Units.Mm(p1.Z - p0.Z)) > FaceToleranceMm)
                    continue; // Sloped framing needs a separate face/section resolver.
                if (!CrossesColumnPlan(p0, p1, lowerBox) || !CrossesColumnPlan(p0, p1, upperBox))
                    continue;

                projected.Add(Tuple.Create(beam, box));
                double bottomMm = V3Units.Mm(box.Min.Z);
                double topMm = V3Units.Mm(box.Max.Z);
                double lowerTopMm = V3Units.Mm(lower.Geometry.Top);
                double upperBottomMm = V3Units.Mm(upper.Geometry.Bottom);
                double floorZMm = Math.Max(lowerTopMm, upperBottomMm);

                // Case 1: Gap modeled between lower column and upper column to accommodate beam
                bool alignedWithGap = Math.Abs(bottomMm - lowerTopMm) <= FaceToleranceMm &&
                                      Math.Abs(topMm - upperBottomMm) <= FaceToleranceMm;

                // Case 2: Continuous columns touching at floor level (standard BIM practice)
                bool alignedContinuous = Math.Abs(topMm - floorZMm) <= FaceToleranceMm &&
                                         bottomMm < floorZMm - 50;

                // Case 3: Beam top aligns with upper column bottom or lower column top within tolerance
                bool alignedTop = Math.Abs(topMm - upperBottomMm) <= FaceToleranceMm ||
                                  Math.Abs(topMm - lowerTopMm) <= FaceToleranceMm;

                if (!alignedWithGap && !(alignedContinuous && alignedTop))
                {
                    misaligned.Add(beam.Name + " (đáy=" + Math.Round(bottomMm, 1) +
                        ", đỉnh=" + Math.Round(topMm, 1) + " mm)");
                    continue;
                }

                double coverMm;
                string coverReason;
                if (!TryTopCover(doc, beam, out coverMm, out coverReason))
                {
                    // Fail closed: configuration/default cover is not evidence of the beam's actual top cover.
                    missingCover.Add(beam.Name + " (" + coverReason + ")");
                    continue;
                }

                var valid = new V3BeamJointInfo
                {
                    BeamId = beam.Id,
                    BeamName = beam.Name,
                    HeightMm = topMm - bottomMm,
                    BottomZ = box.Min.Z,
                    TopZ = box.Max.Z,
                    TopCoverMm = coverMm,
                    MatchingBeamCount = 1
                };
                if (valid.HeightMm <= 0)
                    continue;

                if (result == null)
                    result = valid;
                else
                {
                    // When multiple beams intersect at the column joint (e.g., X and Y beams of different depths),
                    // the deeper beam governs the anchorage and termination height.
                    if (valid.HeightMm > result.HeightMm)
                    {
                        result = valid;
                    }
                    result.MatchingBeamCount++;
                }
            }

            if (result != null)
            {
                return true;
            }

            if (missingCover.Count > 0)
                reason = "Tìm thấy dầm giao nút nhưng không đọc được Rebar Cover - Top Face: " +
                    string.Join("; ", missingCover.Distinct().Take(3)) + ".";
            else if (misaligned.Count > 0)
                reason = "Có dầm chiếu qua tim cột nhưng mặt dầm không trùng đầu hai cột trong sai số " + FaceToleranceMm.ToString("0") + " mm: " +
                    string.Join("; ", misaligned.Distinct().Take(3)) + ".";
            else if (projected.Count == 0)
                reason = "Không tìm thấy dầm Structural Framing nằm ngang giao qua mặt bằng cả hai cột tại nút này.";
            else
                reason = "Không tìm được dầm có hình học và lớp bảo vệ mặt trên hợp lệ cho nút này.";
            return false;
        }

        private static bool CrossesColumnPlan(XYZ p0, XYZ p1, BoundingBoxXYZ column)
        {
            double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
            double length2 = dx * dx + dy * dy;
            if (length2 < 1e-10) return false;
            double cx = (column.Min.X + column.Max.X) / 2;
            double cy = (column.Min.Y + column.Max.Y) / 2;
            double t = Math.Max(0, Math.Min(1,
                ((cx - p0.X) * dx + (cy - p0.Y) * dy) / length2));
            double distance = Math.Sqrt(Math.Pow(p0.X + dx * t - cx, 2) +
                                        Math.Pow(p0.Y + dy * t - cy, 2));
            double halfDiagonal = Math.Sqrt(Math.Pow((column.Max.X - column.Min.X) / 2, 2) +
                                            Math.Pow((column.Max.Y - column.Min.Y) / 2, 2));
            return distance <= halfDiagonal + V3Units.Ft(XYMarginMm);
        }

        private static bool TryTopCover(Document doc, Element beam, out double coverMm, out string reason)
        {
            coverMm = 0;
            reason = null;
            try
            {
                Parameter parameter = beam.get_Parameter(BuiltInParameter.CLEAR_COVER_TOP);
                if (parameter == null || !parameter.HasValue || parameter.StorageType != StorageType.ElementId)
                {
                    reason = "thiếu tham số Rebar Cover - Top Face.";
                    return false;
                }
                ElementId coverId = parameter.AsElementId();
                var coverType = coverId == null || coverId == ElementId.InvalidElementId
                    ? null : doc.GetElement(coverId) as RebarCoverType;
                if (coverType == null)
                {
                    reason = "chưa gán Rebar Cover Type cho mặt trên.";
                    return false;
                }
                coverMm = V3Units.Mm(coverType.CoverDistance);
                if (double.IsNaN(coverMm) || double.IsInfinity(coverMm) || coverMm <= 0 || coverMm > 500)
                {
                    reason = "giá trị Rebar Cover Type mặt trên ngoài miền hợp lệ.";
                    coverMm = 0;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                reason = "lỗi đọc cover (" + ex.GetType().Name + ").";
                return false;
            }
        }
    }
}
