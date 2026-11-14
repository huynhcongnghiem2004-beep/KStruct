using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    // One immutable-in-practice preparation result for both preview and creation.
    // All paths come from the production shape-driven Rebar path generator.
    internal sealed class V3VerifiedPathSnapshot
    {
        internal readonly List<V3SegmentWork> Works;
        internal readonly Dictionary<long, List<V3BarPath>> Paths;
        private V3VerifiedPathSnapshot(List<V3SegmentWork> works,
            Dictionary<long, List<V3BarPath>> paths)
        {
            Works = works;
            Paths = paths;
        }

        internal static V3VerifiedPathSnapshot Prepare(Document doc,
            IList<V3SegmentWork> input, V3FoundationAnchorSettings foundation,
            bool attachFoundation)
        {
            if (doc == null || input == null || input.Count == 0)
                throw new InvalidOperationException("Thiếu tài liệu Revit hoặc tuyến cột.");
            var works = input.OrderBy(w => w.Geometry.Bottom).ToList();
            if (works.Any(w => w.Column == null || w.Config == null || w.Geometry == null))
                throw new InvalidOperationException("Thiếu host, cấu hình hoặc Solid cột.");
            if (attachFoundation)
                V3FoundationAnchor.Attach(doc, works[0], foundation);
            VerifyBeamJoints(doc, works);
            V3StarterConnection.Resolve(works);
            V3RebarValidator.Preflight(works);
            var paths = new Dictionary<long, List<V3BarPath>>();
            foreach (var work in works)
                paths.Add(work.Segment.Id.Value, V3RebarSetCreator.ConnectionPaths(work));
            return new V3VerifiedPathSnapshot(works, paths);
        }

        internal static void VerifyBeamJoints(Document doc, IList<V3SegmentWork> works)
        {
            for (int i = 0; i + 1 < works.Count; i++)
            {
                var lower = works[i];
                var upper = works[i + 1];
                lower.VerifiedBeamJoint = false;
                if (!lower.Config.StarterBars) continue;
                double gapMm = V3Units.Mm(upper.Geometry.Bottom - lower.Geometry.Top);
                if (gapMm < -2 || gapMm > 2) continue;
                V3BeamJointInfo info;
                string reason;
                if (V3BeamJointDetector.TryDetect(doc, lower, upper, out info, out reason))
                {
                    lower.VerifiedBeamJoint = true;
                    lower.Config.JointBeamHeight = info.HeightMm;
                    lower.Config.BeamTopCoverMm = info.TopCoverMm;
                }
                else
                    throw new InvalidOperationException(
                        "Nút " + lower.Segment.LevelName + " → " + upper.Segment.LevelName +
                        " (khe hở " + Math.Round(gapMm, 1) +
                        " mm): chưa xác minh được dầm giao. " + reason);
            }
        }
    }
}
