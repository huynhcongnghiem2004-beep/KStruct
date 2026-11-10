using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    // Stage 1: read-only analysis. Does NOT create views, tags, rebar or Sheets.
    internal sealed class V4ColumnDrawingFloor
    {
        public string Floor { get; set; }
        public string Elevation { get; set; }
        public string Size { get; set; }
        public string MainBars { get; set; }
        public string Ties { get; set; }
        public int ExistingRebarElements { get; set; }
        public int ExistingBarPositions { get; set; }
        public string ProposedSections { get; set; }
        public string State { get; set; }
    }

    internal sealed class V4ColumnDrawingAnalysis
    {
        public readonly List<V4ColumnDrawingFloor> Floors = new List<V4ColumnDrawingFloor>();
        public readonly List<string> SectionPositions = new List<string>();
        public int ExistingRebarElements;
        public int ExistingBarPositions;
        public int NeedsReview;
    }

    internal static class V4ColumnDrawingAnalyzer
    {
        internal static V4ColumnDrawingAnalysis Analyze(Document doc,
            IList<ColumnSegment> segments, IDictionary<long,V3RebarConfig> configs)
        {
            if (segments == null || configs == null) throw new ArgumentNullException("segments/configs");
            var result = new V4ColumnDrawingAnalysis();
            var barsByHost = new Dictionary<long,int>();
            var positionsByHost = new Dictionary<long,int>();
            var requestedIds = new HashSet<long>(segments.Select(segment => segment.Id.Value));
            // Only inspect the selected stack's column hosts. Rebar 3D can also
            // be hosted by a foundation, so this is a host-based count only.
            // Rebar Set count is distinct from the number of physical positions.
            if (doc != null)
            {
                foreach (Rebar bar in new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>())
                {
                    var hostId=bar.GetHostId();
                    if (hostId == null || hostId == ElementId.InvalidElementId) continue;
                    long key=hostId.Value;
                    if (!requestedIds.Contains(key)) continue;
                    barsByHost[key] = (barsByHost.TryGetValue(key,out int count) ? count : 0) + 1;
                    int positions = Math.Max(1,bar.NumberOfBarPositions);
                    positionsByHost[key] = (positionsByHost.TryGetValue(key,out int total) ? total : 0) + positions;
                }
            }

            var ordered=segments.OrderBy(x=>x.BottomMm).ThenBy(x=>x.Id.Value).ToList();
            string priorSignature=null;
            for (int i=0;i<ordered.Count;i++)
            {
                var segment=ordered[i];
                configs.TryGetValue(segment.Id.Value,out V3RebarConfig config);
                int existing=barsByHost.TryGetValue(segment.Id.Value,out int n) ? n : 0;
                int positions=positionsByHost.TryGetValue(segment.Id.Value,out int m) ? m : 0;
                bool configured=config!=null && config.Cx>=2 && config.Cy>=2 &&
                    config.MainDiameter>0 && config.TieDiameter>0 &&
                    config.EndSpacing>0 && config.MidSpacing>0;
                string signature = configured
                    ? Math.Round(segment.WidthMm) + "x" + Math.Round(segment.DepthMm) + ":" +
                      config.Cx + "x" + config.Cy + ":" +
                      config.MainDiameter.ToString("0.##",CultureInfo.InvariantCulture) + ":" +
                      config.TieDiameter.ToString("0.##",CultureInfo.InvariantCulture) + ":" +
                      config.CrossTies + ":" + config.TieStyle + ":" +
                      config.EndSpacing.ToString("0.##",CultureInfo.InvariantCulture) + ":" +
                      config.MidSpacing.ToString("0.##",CultureInfo.InvariantCulture)
                    : "unconfigured:" + i;
                bool changed=i>0 && signature!=priorSignature;
                var levels=new List<string>();
                if (i==0 || changed) levels.Add("Chân tầng");
                levels.Add("Giữa tầng");
                if (i==ordered.Count-1 || changed) levels.Add("Đỉnh tầng");
                double h=segment.TopMm-segment.BottomMm;
                if (double.IsNaN(h) || double.IsInfinity(h) || h <= 0)
                    throw new InvalidOperationException("Cột " + segment.Id.Value +
                        ": cao độ không hợp lệ để lập mặt cắt.");
                // A short column may have identical proposed cut elevations.
                var proposedZ = new HashSet<long>();
                foreach(string where in levels)
                {
                    double z=where=="Chân tầng"?segment.BottomMm+Math.Min(150,h/4):
                        where=="Đỉnh tầng"?segment.TopMm-Math.Min(150,h/4):
                        (segment.BottomMm+segment.TopMm)/2;
                    if (!proposedZ.Add((long)Math.Round(z * 10))) continue;
                    result.SectionPositions.Add("MC " + (result.SectionPositions.Count+1) +
                        " · " + segment.LevelName + " / " + where + " / Z=" +
                        Math.Round(z,0).ToString("0",CultureInfo.InvariantCulture) + " mm");
                }
                bool ready=configured && existing>0;
                if (!ready) result.NeedsReview++;
                result.ExistingRebarElements += existing;
                result.ExistingBarPositions += positions;
                result.Floors.Add(new V4ColumnDrawingFloor {
                    Floor=segment.LevelName,
                    Elevation=Math.Round(segment.BottomMm).ToString("0",CultureInfo.InvariantCulture) +
                        " → " + Math.Round(segment.TopMm).ToString("0",CultureInfo.InvariantCulture) + " mm",
                    Size=Math.Round(segment.WidthMm)+"×"+Math.Round(segment.DepthMm)+" mm",
                    MainBars=configured ? config.BarCount + "Ø" +
                        config.MainDiameter.ToString("0.##",CultureInfo.InvariantCulture) : "Chưa cấu hình",
                    Ties=configured ? "Ø" + config.TieDiameter.ToString("0.##",CultureInfo.InvariantCulture) +
                        " a" + config.EndSpacing.ToString("0.##",CultureInfo.InvariantCulture) +
                        "/" + config.MidSpacing.ToString("0.##",CultureInfo.InvariantCulture) : "Chưa cấu hình",
                    ExistingRebarElements=existing,
                    ExistingBarPositions=positions,
                    ProposedSections=string.Join(", ",levels),
                    State=ready ? "Có Rebar host cột · cần kiểm tra chi tiết" :
                        configured ? "Chưa có Rebar host cột" : "Thiếu cấu hình thép"
                });
                priorSignature=signature;
            }
            return result;
        }
    }
}
