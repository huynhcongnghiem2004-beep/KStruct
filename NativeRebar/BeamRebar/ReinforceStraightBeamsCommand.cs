using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReinforceStraightBeamsCommand : IExternalCommand
    {
        internal sealed class BeamPreviewSupport
        {
            public long ElementId;
            public double StartMm;
            public double EndMm;
            public double WidthMm;
            public string Kind;
            public string Label;
            public string AxisLabel;
        }

        internal sealed class BeamPreviewSection
        {
            public double StartMm, EndMm, WidthMm, HeightMm, TopElevationMm, BottomElevationMm;
            public string Label;
        }

        internal sealed class DetailedBarSpec
        {
            public string Key;
            public int Count;
            // Index and total count identify this bar's lane within its layer.
            // Older settings omit these fields and keep the original distributed-row behavior.
            public int BarIndex = -1;
            public int BarGroupCount;
            public RebarBarType Type;
            public bool IsTop;
            public double StartMm;
            public double EndMm;
            public double SectionPosition;
            public double LeftHookMm;
            public double RightHookMm;
            public double HookDownMm;
            public RebarHookType StartHook;
            public RebarHookType EndHook;
            public int StartPointIndex;
            public int EndPointIndex = -1;
            public double LeftRatio = 0.25;
            public double RightRatio = 0.25;
        }

        private sealed class BeamFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance fi && fi.Category != null &&
                fi.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class BarChoice
        {
            public RebarBarType Type { get; private set; }
            public string Display { get; private set; }
            public BarChoice(RebarBarType type)
            {
                Type = type;
                Parameter p = type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                double mm = p != null && p.HasValue ? UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters) : 0;
                Display = type.Name + " | Ø" + mm.ToString("0.##") + " mm";
            }
        }

        private sealed class HookChoice
        {
            public RebarHookType Type { get; private set; }
            public string Display { get; private set; }
            public HookChoice(RebarHookType type)
            {
                Type = type;
                Display = type == null ? "Không dùng móc" : type.Name;
            }
        }

        internal sealed class Options
        {
            public List<string> DeletedReinforcementRanges = new List<string>();
            public RebarBarType MainType;
            public RebarBarType TopType;
            public RebarBarType BottomType;
            public RebarBarType TieType;
            public int TopCount;
            public int BottomCount;
            public int TopLayer2Count;
            public int BottomLayer2Count;
            public int TopLayer3Count;
            public int BottomLayer3Count;
            public int SideBarCount;
            public RebarBarType TopLayer2Type;
            public RebarBarType BottomLayer2Type;
            public RebarBarType TopLayer3Type;
            public RebarBarType BottomLayer3Type;
            public RebarBarType SideBarType;
            public RebarHookType TopStartHook, TopEndHook, BottomStartHook, BottomEndHook;
            public double LongitudinalLayerSpacingMm;
            public double CoverMm;
            public double TieSpacingMm;
            public bool UseZonedTies;
            public bool TcvnCutoffEnabled = true;
            public double TcvnConcreteRbtMpa;
            public double TcvnSteelRsMpa;
            public double TcvnBondEta1 = 2.5;
            public double TcvnAreaRatio = 1.0;
            public double TcvnAlpha = 1.0;
            public double TieEndZoneLengthMm;
            public double TieEndSpacingMm;
            public double TieMiddleSpacingMm;
            public double StirrupSupportOffsetMm = 50;
            public double SideBarMinimumHeightMm;
            public double SideBarSpacingMm;
            public double SideBarAnchorageMm;
            [XmlIgnore] public List<DetailedBarSpec> DetailedBars = new List<DetailedBarSpec>();
            // Preview-only geometry. The editor reads these from the selected beam;
            // they are intentionally not written to the settings file.
            [XmlIgnore] public double PreviewBeamLengthMm;
            [XmlIgnore] public double PreviewBeamWidthMm;
            [XmlIgnore] public double PreviewBeamHeightMm;
            [XmlIgnore] public string PreviewSectionDescription;
            [XmlIgnore] public List<BeamPreviewSupport> PreviewSupports = new List<BeamPreviewSupport>();
            [XmlIgnore] public List<BeamPreviewSection> PreviewBeamSections = new List<BeamPreviewSection>();
        }

        private sealed class Frame
        {
            public XYZ Origin, Axis, Width, Up;
            public double HalfLength, WidthCenter, HeightCenter, HalfWidth, HalfHeight;
        }

        private sealed class BeamRunSegment
        {
            public FamilyInstance Beam;
            public Frame Frame;
            public double StartMm, EndMm;
            public bool Forward;
        }

        private sealed class BeamRun
        {
            public Frame Frame;
            public double LengthMm;
            public List<BeamRunSegment> Segments = new List<BeamRunSegment>();
        }

        private static BeamRun TryBuildBeamRun(IList<FamilyInstance> beams, Document doc)
        {
            if (beams == null || beams.Count < 2) return null;
            Frame reference = GetFrame(beams[0], doc);
            var raw = new List<Tuple<FamilyInstance, Frame, double, double, bool>>();
            double lineTolerance = UnitUtils.ConvertToInternalUnits(30, UnitTypeId.Millimeters);
            foreach (FamilyInstance beam in beams)
            {
                Frame frame;
                try { frame = GetFrame(beam, doc); }
                catch { return null; }
                double alignment = frame.Axis.DotProduct(reference.Axis);
                if (Math.Abs(alignment) < 0.999) return null;
                XYZ delta = frame.Origin - reference.Origin;
                XYZ perpendicular = delta - reference.Axis * delta.DotProduct(reference.Axis);
                if (perpendicular.GetLength() > lineTolerance) return null;
                double center = delta.DotProduct(reference.Axis);
                double halfLength = frame.HalfLength;
                raw.Add(Tuple.Create(beam, frame, center - halfLength, center + halfLength, alignment >= 0));
            }
            raw = raw.OrderBy(item => item.Item3).ToList();
            double gapTolerance = UnitUtils.ConvertToInternalUnits(150, UnitTypeId.Millimeters);
            for (int i = 1; i < raw.Count; i++)
                if (raw[i].Item3 - raw[i - 1].Item4 > gapTolerance) return null;
            double runStart = raw[0].Item3, runEnd = raw.Max(item => item.Item4);
            if (runEnd - runStart < doc.Application.ShortCurveTolerance) return null;
            double centerStation = (runStart + runEnd) / 2.0;
            var runFrame = new Frame
            {
                Origin = reference.Origin + reference.Axis * centerStation,
                Axis = reference.Axis, Width = reference.Width, Up = reference.Up,
                HalfLength = (runEnd - runStart) / 2.0,
                WidthCenter = reference.WidthCenter, HeightCenter = reference.HeightCenter,
                HalfWidth = raw.Max(item => item.Item2.HalfWidth), HalfHeight = raw.Max(item => item.Item2.HalfHeight)
            };
            var run = new BeamRun
            {
                Frame = runFrame,
                LengthMm = UnitUtils.ConvertFromInternalUnits(runEnd - runStart, UnitTypeId.Millimeters)
            };
            foreach (var item in raw)
            {
                double startMm = UnitUtils.ConvertFromInternalUnits(item.Item3 - runStart, UnitTypeId.Millimeters);
                double endMm = UnitUtils.ConvertFromInternalUnits(item.Item4 - runStart, UnitTypeId.Millimeters);
                run.Segments.Add(new BeamRunSegment
                {
                    Beam = item.Item1, Frame = item.Item2, StartMm = startMm, EndMm = endMm, Forward = item.Item5
                });
            }
            return run;
        }

        private static string DescribeBeamRun(BeamRun run)
        {
            return "Dầm ghép " + run.Segments.Count + " đoạn cùng trục · " + string.Join(" | ", run.Segments.Select(segment =>
                segment.StartMm.ToString("0") + "–" + segment.EndMm.ToString("0") + " mm: " +
                UnitUtils.ConvertFromInternalUnits(segment.Frame.HalfWidth * 2, UnitTypeId.Millimeters).ToString("0") + "×" +
                UnitUtils.ConvertFromInternalUnits(segment.Frame.HalfHeight * 2, UnitTypeId.Millimeters).ToString("0") + " mm"));
        }

        private static List<BeamPreviewSection> BuildPreviewSections(BeamRun run)
        {
            var sections = new List<BeamPreviewSection>();
            var ordered = run.Segments.OrderBy(segment => segment.StartMm).ToList();
            var breaks = new List<double> { 0 };
            for (int i = 0; i + 1 < ordered.Count; i++)
                breaks.Add(Math.Max(0, Math.Min(run.LengthMm, (ordered[i].EndMm + ordered[i + 1].StartMm) / 2.0)));
            breaks.Add(run.LengthMm);
            for (int i = 0; i < ordered.Count; i++)
            {
                BeamRunSegment segment = ordered[i];
                Frame frame = segment.Frame;
                XYZ offset = frame.Origin - run.Frame.Origin;
                double widthSign = frame.Width.DotProduct(run.Frame.Width);
                double heightSign = frame.Up.DotProduct(run.Frame.Up);
                double centerH = offset.DotProduct(run.Frame.Up) + frame.HeightCenter * heightSign;
                double halfH = frame.HalfHeight * Math.Abs(heightSign);
                double widthMm = UnitUtils.ConvertFromInternalUnits(frame.HalfWidth * 2, UnitTypeId.Millimeters);
                double heightMm = UnitUtils.ConvertFromInternalUnits(frame.HalfHeight * 2, UnitTypeId.Millimeters);
                sections.Add(new BeamPreviewSection
                {
                    StartMm = breaks[i], EndMm = breaks[i + 1], WidthMm = widthMm, HeightMm = heightMm,
                    TopElevationMm = UnitUtils.ConvertFromInternalUnits(centerH + halfH, UnitTypeId.Millimeters),
                    BottomElevationMm = UnitUtils.ConvertFromInternalUnits(centerH - halfH, UnitTypeId.Millimeters),
                    Label = "Đoạn " + (i + 1) + " · " + widthMm.ToString("0") + "×" + heightMm.ToString("0") + " mm"
                });
            }
            return sections;
        }

        private static void ApplyRunSegmentOptions(Options options, BeamRun run, BeamRunSegment segment)
        {
            double totalLength = run.LengthMm;
            double segmentLength = segment.EndMm - segment.StartMm;
            var mapped = new List<DetailedBarSpec>();
            var source = (options.DetailedBars ?? new List<DetailedBarSpec>()).ToList();
            foreach (DetailedBarSpec bar in source)
            {
                if (bar == null || bar.Count <= 0) continue;
                double globalStart = Math.Max(0, bar.StartMm);
                double globalEnd = Math.Min(totalLength, bar.EndMm);
                double clippedStart = Math.Max(globalStart, segment.StartMm);
                double clippedEnd = Math.Min(globalEnd, segment.EndMm);
                if (clippedEnd - clippedStart < 50) continue;
                double localStart = segment.Forward ? clippedStart - segment.StartMm : segment.EndMm - clippedEnd;
                double localEnd = segment.Forward ? clippedEnd - segment.StartMm : segment.EndMm - clippedStart;
                mapped.Add(new DetailedBarSpec
                {
                    Key = bar.Key, Count = bar.Count, BarIndex = bar.BarIndex, BarGroupCount = bar.BarGroupCount,
                    Type = bar.Type, IsTop = bar.IsTop, StartMm = localStart, EndMm = localEnd,
                    SectionPosition = bar.SectionPosition, LeftHookMm = bar.LeftHookMm, RightHookMm = bar.RightHookMm,
                    HookDownMm = bar.HookDownMm, StartPointIndex = bar.StartPointIndex, EndPointIndex = bar.EndPointIndex,
                    LeftRatio = bar.LeftRatio, RightRatio = bar.RightRatio,
                    StartHook = Math.Abs(clippedStart - globalStart) < 1 && Math.Abs(localStart) < 1 ? bar.StartHook : null,
                    EndHook = Math.Abs(clippedEnd - globalEnd) < 1 && Math.Abs(localEnd - segmentLength) < 1 ? bar.EndHook : null
                });
            }
            options.DetailedBars = mapped;
            options.PreviewBeamLengthMm = segmentLength;
            options.PreviewSupports = (options.PreviewSupports ?? new List<BeamPreviewSupport>())
                .Select(support =>
                {
                    double a = Math.Max(segment.StartMm, support.StartMm);
                    double b = Math.Min(segment.EndMm, support.EndMm);
                    if (b <= a) return null;
                    return new BeamPreviewSupport
                    {
                        ElementId = support.ElementId, Kind = support.Kind, Label = support.Label, AxisLabel = support.AxisLabel,
                        StartMm = segment.Forward ? a - segment.StartMm : segment.EndMm - b,
                        EndMm = segment.Forward ? b - segment.StartMm : segment.EndMm - a,
                        WidthMm = support.WidthMm
                    };
                }).Where(support => support != null).OrderBy(support => support.StartMm).ToList();
            void DisableAbsentLayer(string key, bool hasSavedDetails, Action<int> setCount, int count)
            {
                if (hasSavedDetails && !mapped.Any(bar => bar.Key == key)) setCount(0);
                else setCount(count);
            }
            // Layer counts are kept only where the run's detailed bars intersect this segment.
            DisableAbsentLayer("top", source.Any(bar => bar.Key == "top"), value => options.TopCount = value, options.TopCount);
            DisableAbsentLayer("bottom", source.Any(bar => bar.Key == "bottom"), value => options.BottomCount = value, options.BottomCount);
            DisableAbsentLayer("top1", source.Any(bar => bar.Key == "top1"), value => options.TopLayer2Count = value, options.TopLayer2Count);
            DisableAbsentLayer("bottom1", source.Any(bar => bar.Key == "bottom1"), value => options.BottomLayer2Count = value, options.BottomLayer2Count);
            DisableAbsentLayer("top2", source.Any(bar => bar.Key == "top2"), value => options.TopLayer3Count = value, options.TopLayer3Count);
            DisableAbsentLayer("bottom2", source.Any(bar => bar.Key == "bottom2"), value => options.BottomLayer3Count = value, options.BottomLayer3Count);
        }

        private sealed class BeamLocalBounds
        {
            public double MinAxis = double.PositiveInfinity, MaxAxis = double.NegativeInfinity;
            public double MinWidth = double.PositiveInfinity, MaxWidth = double.NegativeInfinity;
            public double MinHeight = double.PositiveInfinity, MaxHeight = double.NegativeInfinity;
        }

        private static Frame BuildRunRebarEnvelope(BeamRun run)
        {
            double minW = double.NegativeInfinity, maxW = double.PositiveInfinity;
            double minH = double.NegativeInfinity, maxH = double.PositiveInfinity;
            foreach (BeamRunSegment segment in run.Segments)
            {
                Frame frame = segment.Frame;
                XYZ offset = frame.Origin - run.Frame.Origin;
                double widthSign = frame.Width.DotProduct(run.Frame.Width);
                double heightSign = frame.Up.DotProduct(run.Frame.Up);
                double centerW = offset.DotProduct(run.Frame.Width) + frame.WidthCenter * widthSign;
                double centerH = offset.DotProduct(run.Frame.Up) + frame.HeightCenter * heightSign;
                double halfW = frame.HalfWidth * Math.Abs(widthSign);
                double halfH = frame.HalfHeight * Math.Abs(heightSign);
                minW = Math.Max(minW, centerW - halfW); maxW = Math.Min(maxW, centerW + halfW);
                minH = Math.Max(minH, centerH - halfH); maxH = Math.Min(maxH, centerH + halfH);
            }
            if (maxW - minW < 1e-6 || maxH - minH < 1e-6)
                throw new InvalidOperationException("Các tiết diện đã chọn không có vùng giao nhau để đặt một bộ thép dọc liên tục. Kiểm tra cao độ/trục của các đoạn dầm.");
            return new Frame
            {
                Origin = run.Frame.Origin, Axis = run.Frame.Axis, Width = run.Frame.Width, Up = run.Frame.Up,
                HalfLength = run.Frame.HalfLength, WidthCenter = (minW + maxW) / 2.0,
                HeightCenter = (minH + maxH) / 2.0, HalfWidth = (maxW - minW) / 2.0,
                HalfHeight = (maxH - minH) / 2.0
            };
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> beams = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(IsStructuralBeam).Distinct().ToList();
                if (beams.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new BeamFilter(), "Chọn dầm thẳng (kể cả dầm chéo) tiết diện chữ nhật cần bố trí cốt thép");
                    beams = picked.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().Where(IsStructuralBeam).Distinct().ToList();
                }
                if (beams.Count == 0) return Result.Cancelled;
                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                List<RebarHookType> hookTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType))
                    .Cast<RebarHookType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                Options defaults = LoadSettings(doc, types, hookTypes) ?? new Options();
                BeamRun beamRun = TryBuildBeamRun(beams, doc);
                Frame previewFrame = beamRun?.Frame ?? GetFrame(beams[0], doc);
                defaults.PreviewBeamLengthMm = beamRun?.LengthMm ?? UnitUtils.ConvertFromInternalUnits(previewFrame.HalfLength * 2.0, UnitTypeId.Millimeters);
                defaults.PreviewBeamWidthMm = beamRun?.Segments.Max(segment => UnitUtils.ConvertFromInternalUnits(segment.Frame.HalfWidth * 2.0, UnitTypeId.Millimeters))
                    ?? UnitUtils.ConvertFromInternalUnits(previewFrame.HalfWidth * 2.0, UnitTypeId.Millimeters);
                defaults.PreviewBeamHeightMm = beamRun?.Segments.Max(segment => UnitUtils.ConvertFromInternalUnits(segment.Frame.HalfHeight * 2.0, UnitTypeId.Millimeters))
                    ?? UnitUtils.ConvertFromInternalUnits(previewFrame.HalfHeight * 2.0, UnitTypeId.Millimeters);
                defaults.PreviewSupports = beamRun == null
                    ? FindBeamPreviewSupports(doc, beams[0], previewFrame, new[] { beams[0].Id })
                    : FindBeamPreviewSupports(doc, beams[0], previewFrame, beams.Select(beam => beam.Id).ToList());
                defaults.PreviewBeamSections = new List<BeamPreviewSection>();
                defaults.PreviewSectionDescription = beamRun == null
                    ? DetectSectionChanges(beams, doc, defaults.PreviewBeamSections) : DescribeBeamRun(beamRun);
                if (beamRun != null) defaults.PreviewBeamSections = BuildPreviewSections(beamRun);
                if (defaults.PreviewBeamSections.Count == 0)
                    defaults.PreviewBeamSections.Add(new BeamPreviewSection {
                        StartMm = 0, EndMm = defaults.PreviewBeamLengthMm,
                        WidthMm = defaults.PreviewBeamWidthMm, HeightMm = defaults.PreviewBeamHeightMm,
                        TopElevationMm = UnitUtils.ConvertFromInternalUnits(previewFrame.HeightCenter + previewFrame.HalfHeight, UnitTypeId.Millimeters),
                        BottomElevationMm = UnitUtils.ConvertFromInternalUnits(previewFrame.HeightCenter - previewFrame.HalfHeight, UnitTypeId.Millimeters),
                        Label = "Tiết diện chưa phân đoạn"
                    });
                Options options = BeamRebarEditorWindow.ShowQuick(data.Application.MainWindowHandle, types, hookTypes,
                    defaults, false, null, current => SaveSettings(doc, current));
                if (options == null) return Result.Cancelled;
                string settingSaveNotice = null;
                try { SaveSettings(doc, options); }
                catch (Exception saveError) { settingSaveNotice = "\nKhông lưu được setting cho project: " + saveError.Message; }

                int completed = 0, mainBars = 0, ties = 0, replacedBars = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Cốt thép dầm thẳng"))
                {
                    tx.Start();
                    var previousByBeam = beams.ToDictionary(beam => beam.Id, beam => FindManagedBeamBars(doc, beam.Id));
                    bool continuousRunBarsCreated = false;
                    foreach (FamilyInstance beam in beams)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                Frame frame = GetFrame(beam, doc);
                                List<ElementId> previousManagedBars = previousByBeam[beam.Id];
                                List<BeamPreviewSupport> originalSupports = options.PreviewSupports;
                                List<DetailedBarSpec> originalBars = options.DetailedBars;
                                double originalLength = options.PreviewBeamLengthMm;
                                int[] originalCounts = { options.TopCount, options.BottomCount, options.TopLayer2Count, options.BottomLayer2Count,
                                    options.TopLayer3Count, options.BottomLayer3Count };
                                bool createRunBarsNow = beamRun != null && !continuousRunBarsCreated;
                                if (createRunBarsNow)
                                    CreateBeamRebar(doc, beam, BuildRunRebarEnvelope(beamRun), options, true, false);
                                if (beamRun != null)
                                    ApplyRunSegmentOptions(options, beamRun, beamRun.Segments.First(segment => segment.Beam.Id == beam.Id));
                                else
                                {
                                    options.PreviewBeamLengthMm = UnitUtils.ConvertFromInternalUnits(frame.HalfLength * 2.0, UnitTypeId.Millimeters);
                                    options.PreviewSupports = FindBeamPreviewSupports(doc, beam, frame, new[] { beam.Id });
                                }
                                int tieCount;
                                try { tieCount = CreateBeamRebar(doc, beam, frame, options, beamRun == null, true); }
                                finally
                                {
                                    options.PreviewSupports = originalSupports;
                                    options.DetailedBars = originalBars;
                                    options.PreviewBeamLengthMm = originalLength;
                                    options.TopCount = originalCounts[0]; options.BottomCount = originalCounts[1];
                                    options.TopLayer2Count = originalCounts[2]; options.BottomLayer2Count = originalCounts[3];
                                    options.TopLayer3Count = originalCounts[4]; options.BottomLayer3Count = originalCounts[5];
                                }
                                if (tieCount <= 0) throw new InvalidOperationException("Không tạo được bộ thép mới; bộ KStruct cũ được giữ nguyên.");
                                if (previousManagedBars.Count > 0) doc.Delete(previousManagedBars);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận cốt thép dầm.");
                                if (createRunBarsNow) continuousRunBarsCreated = true;
                                completed++;
                                replacedBars += previousManagedBars.Count;
                                mainBars += options.TopCount + options.BottomCount + options.TopLayer2Count + options.BottomLayer2Count + options.TopLayer3Count + options.BottomLayer3Count + options.SideBarCount * 2;
                                ties += tieCount;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Dầm " + beam.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Cốt thép dầm", "Không tạo được cốt thép cho dầm nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận cốt thép mới.");
                }
                string report = "Đã gia cường " + completed + "/" + beams.Count + " dầm. Thép dọc: " + mainBars + " thanh; đai: " + ties + " cái.";
                if (settingSaveNotice == null) report += " Đã nhớ cấu hình nhanh và các thanh chi tiết cho project này.";
                else report += settingSaveNotice;
                if (replacedBars > 0) report += " Đã thay " + replacedBars + " thanh KStruct cũ sau khi tạo bộ mới thành công.";
                if (options.UseZonedTies) report += " Đai dùng bước riêng cho vùng đầu và vùng giữa.";
                bool hasTcvnCuts = (options.DetailedBars ?? new List<DetailedBarSpec>()).Any(b => b.Count > 0 &&
                    (b.StartMm > 0 || b.EndMm < options.PreviewBeamLengthMm));
                if (options.TcvnCutoffEnabled && hasTcvnCuts)
                    report += " Điểm cắt nội bộ đã được kéo dài neo theo TCVN 5574:2018, 10.3.5; cần kiểm tra nội lực tại mặt cắt nghiêng theo 8.1.3.4 trong hồ sơ tính toán.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " dầm:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Cốt thép dầm", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cốt thép dầm", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsStructuralBeam(FamilyInstance beam) => beam.Category != null &&
            beam.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;

        private static string DetectSectionChanges(IList<FamilyInstance> beams, Document doc,
            List<BeamPreviewSection> previewSections = null)
        {
            var descriptions = new List<string>();
            IEnumerable<Solid> ReadSolids(GeometryElement geometry)
            {
                if (geometry == null) yield break;
                foreach (GeometryObject item in geometry)
                {
                    if (item is Solid solid && solid.Volume > 1e-9) yield return solid;
                    if (item is GeometryInstance instance)
                        foreach (Solid nested in ReadSolids(instance.GetInstanceGeometry())) yield return nested;
                }
            }
            foreach (FamilyInstance beam in beams)
            {
                Frame frame = GetFrame(beam, doc);
                var solids = ReadSolids(beam.get_Geometry(new Autodesk.Revit.DB.Options {
                    DetailLevel = ViewDetailLevel.Fine })).ToList();
                var edgeSegments = new List<Tuple<XYZ, XYZ>>();
                var boundaries = new List<double> { -frame.HalfLength, frame.HalfLength };
                foreach (Solid solid in solids)
                    foreach (Edge edge in solid.Edges)
                    {
                        var points = edge.Tessellate();
                        for (int index = 0; index < points.Count; index++)
                        {
                            double station = (points[index] - frame.Origin).DotProduct(frame.Axis);
                            if (station > -frame.HalfLength && station < frame.HalfLength) boundaries.Add(station);
                            if (index > 0) edgeSegments.Add(Tuple.Create(points[index - 1], points[index]));
                        }
                    }
                double tolerance = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Millimeters);
                var nodes = new List<double>();
                foreach (double boundary in boundaries.OrderBy(value => value))
                    if (nodes.Count == 0 || boundary - nodes[nodes.Count - 1] > tolerance)
                        nodes.Add(boundary);
                var sections = new List<string>();
                var dimensions = new List<string>();
                for (int interval = 0; interval + 1 < nodes.Count; interval++)
                {
                    double station = (nodes[interval] + nodes[interval + 1]) / 2;
                    var intersections = new List<XYZ>();
                    foreach (var segment in edgeSegments)
                    {
                        double a = (segment.Item1 - frame.Origin).DotProduct(frame.Axis);
                        double b = (segment.Item2 - frame.Origin).DotProduct(frame.Axis);
                        if (Math.Abs(b - a) < 1e-9 || station < Math.Min(a, b) || station > Math.Max(a, b)) continue;
                        intersections.Add(segment.Item1 + (segment.Item2 - segment.Item1) * ((station - a) / (b - a)));
                    }
                    if (intersections.Count < 3) continue;
                    double width = UnitUtils.ConvertFromInternalUnits(
                        intersections.Max(point => point.DotProduct(frame.Width)) - intersections.Min(point => point.DotProduct(frame.Width)), UnitTypeId.Millimeters);
                    double height = UnitUtils.ConvertFromInternalUnits(
                        intersections.Max(point => point.DotProduct(frame.Up)) - intersections.Min(point => point.DotProduct(frame.Up)), UnitTypeId.Millimeters);
                    if (width > 1 && height > 1)
                    {
                        string size = Math.Round(width) + " × " + Math.Round(height);
                        dimensions.Add(size);
                        double startMm = UnitUtils.ConvertFromInternalUnits(nodes[interval] + frame.HalfLength, UnitTypeId.Millimeters);
                        double endMm = UnitUtils.ConvertFromInternalUnits(nodes[interval + 1] + frame.HalfLength, UnitTypeId.Millimeters);
                        if (previewSections != null && beam.Id == beams[0].Id)
                        {
                            double topMm = UnitUtils.ConvertFromInternalUnits(
                                intersections.Max(point => (point - frame.Origin).DotProduct(frame.Up)), UnitTypeId.Millimeters);
                            double bottomMm = UnitUtils.ConvertFromInternalUnits(
                                intersections.Min(point => (point - frame.Origin).DotProduct(frame.Up)), UnitTypeId.Millimeters);
                            var previous = previewSections.LastOrDefault();
                            if (previous != null && Math.Abs(previous.EndMm - startMm) <= 1 &&
                                Math.Abs(previous.WidthMm - width) <= 1 && Math.Abs(previous.HeightMm - height) <= 1 &&
                                Math.Abs(previous.TopElevationMm - topMm) <= 1 &&
                                Math.Abs(previous.BottomElevationMm - bottomMm) <= 1)
                                previous.EndMm = endMm;
                            else
                                previewSections.Add(new BeamPreviewSection {
                                    StartMm = startMm, EndMm = endMm, WidthMm = width, HeightMm = height,
                                    TopElevationMm = topMm, BottomElevationMm = bottomMm,
                                    Label = "Đoạn " + (previewSections.Count + 1) + " · " + size + " mm"
                                });
                        }
                        sections.Add(startMm.ToString("0") + "–" + endMm.ToString("0") + " mm: " + size);
                    }
                }
                var uniqueSections = dimensions.Distinct().ToList();
                descriptions.Add("Dầm " + beam.Id.Value + " · " +
                    (uniqueSections.Count > 1 ? "thay đổi tiết diện " : "tiết diện ") +
                    (uniqueSections.Count > 0 ? string.Join(" | ", sections) + " mm" : "chưa đo được"));
            }
            return string.Join(" | ", descriptions);
        }

        internal static string ApplySettingsToBeams(Document doc, IList<FamilyInstance> beams,
            Func<FamilyInstance, Options> settingsForBeam)
        {
            if (doc == null || beams == null || settingsForBeam == null) throw new ArgumentNullException();
            int completed = 0, mainBars = 0, ties = 0, replacedBars = 0;
            var skipped = new List<string>();
            using (Transaction tx = new Transaction(doc, "KStruct - Cốt thép dầm từ bảng Excel"))
            {
                tx.Start();
                foreach (FamilyInstance beam in beams.Distinct())
                {
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            if (!IsStructuralBeam(beam)) throw new InvalidOperationException("Cấu kiện không thuộc Structural Framing.");
                            Options options = settingsForBeam(beam);
                            if (options == null) throw new InvalidOperationException("Không có dòng cấu hình khớp Mark/ElementId của dầm.");
                            Frame frame = GetFrame(beam, doc);
                            List<ElementId> previousManagedBars = FindManagedBeamBars(doc, beam.Id);
                            List<BeamPreviewSupport> originalSupports = options.PreviewSupports;
                            options.PreviewSupports = FindBeamPreviewSupports(doc, beam, frame, new[] { beam.Id });
                            int tieCount;
                            try { tieCount = CreateBeamRebar(doc, beam, frame, options); }
                            finally { options.PreviewSupports = originalSupports; }
                            if (tieCount <= 0) throw new InvalidOperationException("Không tạo được bộ thép mới; bộ KStruct cũ được giữ nguyên.");
                            if (previousManagedBars.Count > 0) doc.Delete(previousManagedBars);
                            if (sub.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Revit không chấp nhận cốt thép dầm.");
                            completed++;
                            replacedBars += previousManagedBars.Count;
                            mainBars += options.TopCount + options.BottomCount + options.TopLayer2Count + options.BottomLayer2Count + options.TopLayer3Count + options.BottomLayer3Count + options.SideBarCount * 2;
                            ties += tieCount;
                        }
                        catch (Exception ex)
                        {
                            if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                            skipped.Add("Dầm " + beam.Id.Value + ": " + ex.Message);
                        }
                    }
                }
                if (completed == 0)
                {
                    tx.RollBack();
                    throw new InvalidOperationException("Không tạo được cốt thép cho dầm nào.\n" + string.Join("\n", skipped.Take(8)));
                }
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit không chấp nhận cốt thép mới.");
            }
            string report = "Đã gia cường " + completed + "/" + beams.Count + " dầm. Thép dọc: " + mainBars + " thanh; đai: " + ties + " cái.";
            if (replacedBars > 0) report += " Đã thay " + replacedBars + " thanh KStruct cũ sau khi tạo bộ mới thành công.";
            if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " dầm:\n" + string.Join("\n", skipped.Take(8));
            return report;
        }

        private static List<ElementId> FindManagedBeamBars(Document doc, ElementId beamId)
        {
            string partitionName = "KSTR_BEAM_" + beamId.Value;
            return new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                .Cast<Rebar>().Where(bar => bar.GetHostId() == beamId)
                .Where(bar =>
                {
                    Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    return partition != null && partition.HasValue &&
                        string.Equals(partition.AsString(), partitionName, StringComparison.OrdinalIgnoreCase);
                }).Select(bar => bar.Id).ToList();
        }

        private static Frame GetFrame(FamilyInstance beam, Document doc)
        {
            LocationCurve location = beam.Location as LocationCurve;
            Line axisLine = location == null ? null : location.Curve as Line;
            BoundingBoxXYZ box = beam.get_BoundingBox(null);
            if (axisLine == null || box == null) throw new InvalidOperationException("Chỉ hỗ trợ dầm thẳng có bounding box.");
            XYZ axis = axisLine.Direction.Normalize();
            XYZ up = XYZ.BasisZ - axis * axis.DotProduct(XYZ.BasisZ);
            if (up.GetLength() < 1e-6) throw new InvalidOperationException("Trục dầm gần thẳng đứng; không xác định được tiết diện.");
            up = up.Normalize();
            XYZ width = up.CrossProduct(axis).Normalize();
            XYZ origin = axisLine.Evaluate(0.5, true);
            var points = new List<XYZ>();
            GeometryElement geometry = beam.get_Geometry(new Autodesk.Revit.DB.Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            });
            if (geometry != null) CollectBeamGeometryPoints(geometry, Transform.Identity, points);
            if (points.Count == 0)
            {
                Transform transform = box.Transform ?? Transform.Identity;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                    points.Add(transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                        y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            }
            double minW = points.Min(p => (p - origin).DotProduct(width));
            double maxW = points.Max(p => (p - origin).DotProduct(width));
            double minH = points.Min(p => (p - origin).DotProduct(up));
            double maxH = points.Max(p => (p - origin).DotProduct(up));
            double minAxis = points.Min(p => (p - origin).DotProduct(axis));
            double maxAxis = points.Max(p => (p - origin).DotProduct(axis));
            if (maxAxis - minAxis < doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Không xác định được chiều dài hình học thực của dầm.");
            origin += axis * ((minAxis + maxAxis) / 2.0);
            double halfLength = (maxAxis - minAxis) / 2.0;
            if (maxW - minW < doc.Application.ShortCurveTolerance || maxH - minH < doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Không xác định được kích thước tiết diện dầm.");
            return new Frame
            {
                Origin = origin, Axis = axis, Width = width, Up = up, HalfLength = halfLength,
                WidthCenter = (minW + maxW) / 2.0, HeightCenter = (minH + maxH) / 2.0,
                HalfWidth = (maxW - minW) / 2.0, HalfHeight = (maxH - minH) / 2.0
            };
        }

        private static List<BeamPreviewSupport> FindBeamPreviewSupports(Document doc, FamilyInstance beam, Frame frame, IList<ElementId> selectedBeamIds)
        {
            var supports = new List<BeamPreviewSupport>();
            var selected = new HashSet<ElementId>(selectedBeamIds ?? new List<ElementId>());
            double mainMinWidth = frame.WidthCenter - frame.HalfWidth;
            double mainMaxWidth = frame.WidthCenter + frame.HalfWidth;
            double mainMinHeight = frame.HeightCenter - frame.HalfHeight;
            double mainMaxHeight = frame.HeightCenter + frame.HalfHeight;
            double contactTolerance = Math.Max(UnitUtils.ConvertToInternalUnits(100, UnitTypeId.Millimeters), frame.HalfWidth * 1.5);

            foreach (FamilyInstance candidate in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType().Cast<FamilyInstance>())
            {
                if (candidate.Id == beam.Id || selected.Contains(candidate.Id) || candidate.Category == null) continue;
                long categoryId = candidate.Category.Id.Value;
                bool isColumn = categoryId == (long)BuiltInCategory.OST_StructuralColumns || categoryId == (long)BuiltInCategory.OST_Columns;
                bool isBeam = categoryId == (long)BuiltInCategory.OST_StructuralFraming;
                if (!isColumn && !isBeam) continue;
                if (!TryProjectBounds(candidate, frame, out BeamLocalBounds bounds)) continue;

                double tol = UnitUtils.ConvertToInternalUnits(30, UnitTypeId.Millimeters);
                if (bounds.MaxWidth < mainMinWidth - tol || bounds.MinWidth > mainMaxWidth + tol ||
                    bounds.MaxHeight < mainMinHeight - tol || bounds.MinHeight > mainMaxHeight + tol ||
                    bounds.MaxAxis < -frame.HalfLength - tol || bounds.MinAxis > frame.HalfLength + tol) continue;

                if (isBeam)
                {
                    LocationCurve curve = candidate.Location as LocationCurve;
                    Line axis = curve?.Curve as Line;
                    if (axis == null) continue;
                    double parallel = Math.Abs(axis.Direction.Normalize().DotProduct(frame.Axis));
                    if (parallel >= 0.985)
                    {
                        bool joinsLeft = Math.Abs(bounds.MaxAxis + frame.HalfLength) <= contactTolerance || Math.Abs(bounds.MinAxis + frame.HalfLength) <= contactTolerance;
                        bool joinsRight = Math.Abs(bounds.MaxAxis - frame.HalfLength) <= contactTolerance || Math.Abs(bounds.MinAxis - frame.HalfLength) <= contactTolerance;
                        if (!joinsLeft && !joinsRight) continue;
                    }
                }

                double startMm = UnitUtils.ConvertFromInternalUnits(Math.Max(-frame.HalfLength, bounds.MinAxis) + frame.HalfLength, UnitTypeId.Millimeters);
                double endMm = UnitUtils.ConvertFromInternalUnits(Math.Min(frame.HalfLength, bounds.MaxAxis) + frame.HalfLength, UnitTypeId.Millimeters);
                if (endMm - startMm < 30)
                {
                    double center = (startMm + endMm) * 0.5;
                    startMm = Math.Max(0, center - 50); endMm = Math.Min(defaultsLengthMm(frame), center + 50);
                }
                string kind = isColumn ? "Cột giao" : "Dầm giao";
                string label = candidate.Name + " · " + candidate.Id.Value;
                supports.Add(new BeamPreviewSupport
                {
                    ElementId = candidate.Id.Value, StartMm = startMm, EndMm = endMm,
                    WidthMm = UnitUtils.ConvertFromInternalUnits(Math.Max(0, bounds.MaxAxis - bounds.MinAxis), UnitTypeId.Millimeters),
                    Kind = kind, Label = label
                });
            }

            foreach (BeamPreviewSupport support in supports)
                support.AxisLabel = FindNearestGridName(doc, frame, (support.StartMm + support.EndMm) * 0.5);
            return supports.OrderBy(s => s.StartMm).ToList();
        }

        private static string FindNearestGridName(Document doc, Frame frame, double axisMm)
        {
            double halfLengthMm = UnitUtils.ConvertFromInternalUnits(frame.HalfLength, UnitTypeId.Millimeters);
            double axisOffset = UnitUtils.ConvertToInternalUnits(axisMm - halfLengthMm, UnitTypeId.Millimeters);
            XYZ target = frame.Origin + frame.Axis * axisOffset;
            double maxDistance = UnitUtils.ConvertToInternalUnits(5, UnitTypeId.Millimeters);
            Autodesk.Revit.DB.Grid nearest = null;
            double nearestDistance = double.PositiveInfinity;
            foreach (Autodesk.Revit.DB.Grid grid in new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Grid)).WhereElementIsNotElementType().Cast<Autodesk.Revit.DB.Grid>())
            {
                Curve curve = grid.Curve;
                if (curve == null) continue;
                XYZ tangent;
                try { tangent = curve.ComputeDerivatives(0.5, true).BasisX.Normalize(); }
                catch { continue; }
                if (Math.Abs(tangent.DotProduct(frame.Axis)) > 0.35) continue;
                IntersectionResult projection = curve.Project(target);
                if (projection == null) continue;
                XYZ planarDelta = projection.XYZPoint - target;
                double distance = Math.Sqrt(planarDelta.X * planarDelta.X + planarDelta.Y * planarDelta.Y);
                if (distance <= maxDistance && distance < nearestDistance)
                {
                    nearest = grid;
                    nearestDistance = distance;
                }
            }
            return nearest?.Name;
        }

        private static double defaultsLengthMm(Frame frame) => UnitUtils.ConvertFromInternalUnits(frame.HalfLength * 2.0, UnitTypeId.Millimeters);

        private static bool TryProjectBounds(Element element, Frame frame, out BeamLocalBounds bounds)
        {
            bounds = null;
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null) return false;
            Transform transform = box.Transform ?? Transform.Identity;
            var projected = new BeamLocalBounds();
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            {
                XYZ point = transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                    y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z));
                XYZ delta = point - frame.Origin;
                double along = delta.DotProduct(frame.Axis), across = delta.DotProduct(frame.Width), vertical = delta.DotProduct(frame.Up);
                projected.MinAxis = Math.Min(projected.MinAxis, along); projected.MaxAxis = Math.Max(projected.MaxAxis, along);
                projected.MinWidth = Math.Min(projected.MinWidth, across); projected.MaxWidth = Math.Max(projected.MaxWidth, across);
                projected.MinHeight = Math.Min(projected.MinHeight, vertical); projected.MaxHeight = Math.Max(projected.MaxHeight, vertical);
            }
            bounds = projected;
            return true;
        }

        private static void CollectBeamGeometryPoints(GeometryElement geometry, Transform transform, List<XYZ> points)
        {
            foreach (GeometryObject obj in geometry)
            {
                GeometryInstance instance = obj as GeometryInstance;
                if (instance != null)
                {
                    CollectBeamGeometryPoints(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), points);
                    continue;
                }
                Solid solid = obj as Solid;
                if (solid == null || solid.Faces.IsEmpty) continue;
                foreach (Face face in solid.Faces)
                {
                    Mesh mesh = face.Triangulate();
                    if (mesh == null) continue;
                    foreach (XYZ vertex in mesh.Vertices) points.Add(transform.OfPoint(vertex));
                }
            }
        }

        private static int CreateBeamRebar(Document doc, FamilyInstance beam, Frame f, Options options,
            bool createLongitudinal = true, bool createTies = true)
        {
            double cover = UnitUtils.ConvertToInternalUnits(options.CoverMm, UnitTypeId.Millimeters);
            RebarBarType topType = options.TopType ?? options.MainType;
            RebarBarType bottomType = options.BottomType ?? options.MainType;
            double topDia = topType.BarNominalDiameter;
            double bottomDia = bottomType.BarNominalDiameter;
            double tieDia = options.TieType.BarNominalDiameter;
            double innerHalfW = f.HalfWidth - cover - tieDia / 2.0;
            double innerHalfH = f.HalfHeight - cover - tieDia / 2.0;
            double topInset = cover + tieDia + topDia / 2.0;
            double bottomInset = cover + tieDia + bottomDia / 2.0;
            double hTop = f.HeightCenter + f.HalfHeight - topInset;
            double hBottom = f.HeightCenter - f.HalfHeight + bottomInset;
            double tieStart = -f.HalfLength + cover + tieDia / 2.0;
            double tieEnd = f.HalfLength - cover - tieDia / 2.0;
            double topStart = -f.HalfLength + topInset, topEnd = f.HalfLength - topInset;
            double bottomStart = -f.HalfLength + bottomInset, bottomEnd = f.HalfLength - bottomInset;
            if (innerHalfW <= Math.Max(topDia, bottomDia) / 2.0 || innerHalfH <= Math.Max(topDia, bottomDia) / 2.0 ||
                tieEnd <= tieStart || topEnd <= topStart || bottomEnd <= bottomStart)
                throw new InvalidOperationException("Cover/thép không vừa tiết diện hoặc chiều dài dầm.");

            double layerGap = UnitUtils.ConvertToInternalUnits(options.LongitudinalLayerSpacingMm, UnitTypeId.Millimeters);
            List<DetailedBarSpec> Specs(string key) => (options.DetailedBars ?? new List<DetailedBarSpec>())
                .Where(x => x.Key == key && x.Count > 0).ToList();
            DetailedBarSpec Spec(string key) => Specs(key).FirstOrDefault();
            double PositionOffset(string key) => Math.Max(0, (Spec(key)?.SectionPosition ?? 1) - 1) * layerGap;
            hTop -= PositionOffset("top");
            hBottom += PositionOffset("bottom");
            RebarBarType topLayer1Type = options.TopLayer2Type ?? topType;
            RebarBarType bottomLayer1Type = options.BottomLayer2Type ?? bottomType;
            RebarBarType topLayer2Type = options.TopLayer3Type ?? topLayer1Type;
            RebarBarType bottomLayer2Type = options.BottomLayer3Type ?? bottomLayer1Type;
            double topLayer1H = hTop - (topDia + topLayer1Type.BarNominalDiameter) / 2.0 - layerGap - PositionOffset("top1");
            double topLayer2H = topLayer1H - (topLayer1Type.BarNominalDiameter + topLayer2Type.BarNominalDiameter) / 2.0 - PositionOffset("top2");
            double bottomLayer1H = hBottom + (bottomDia + bottomLayer1Type.BarNominalDiameter) / 2.0 + layerGap + PositionOffset("bottom1");
            double bottomLayer2H = bottomLayer1H + (bottomLayer1Type.BarNominalDiameter + bottomLayer2Type.BarNominalDiameter) / 2.0 + PositionOffset("bottom2");
            var topRows = new List<Tuple<string, int, RebarBarType, double>> { Tuple.Create("top", options.TopCount, topType, hTop) };
            var bottomRows = new List<Tuple<string, int, RebarBarType, double>> { Tuple.Create("bottom", options.BottomCount, bottomType, hBottom) };
            if (options.TopLayer2Count > 0) topRows.Add(Tuple.Create("top1", options.TopLayer2Count, topLayer1Type, topLayer1H));
            if (options.TopLayer3Count > 0) topRows.Add(Tuple.Create("top2", options.TopLayer3Count, topLayer2Type, topLayer2H));
            if (options.BottomLayer2Count > 0) bottomRows.Add(Tuple.Create("bottom1", options.BottomLayer2Count, bottomLayer1Type, bottomLayer1H));
            if (options.BottomLayer3Count > 0) bottomRows.Add(Tuple.Create("bottom2", options.BottomLayer3Count, bottomLayer2Type, bottomLayer2H));
            if (createLongitudinal)
            foreach (var row in topRows.Concat(bottomRows))
            {
                if (row.Item4 > f.HeightCenter + innerHalfH - row.Item3.BarNominalDiameter / 2.0 ||
                    row.Item4 < f.HeightCenter - innerHalfH + row.Item3.BarNominalDiameter / 2.0)
                    throw new InvalidOperationException("Các lớp thép dọc không vừa chiều cao tiết diện hoặc lớp bảo vệ.");
            }
            if (createLongitudinal)
            foreach (var topRow in topRows)
                foreach (var bottomRow in bottomRows)
                    if (topRow.Item4 - bottomRow.Item4 <= (topRow.Item3.BarNominalDiameter + bottomRow.Item3.BarNominalDiameter) / 2.0)
                        throw new InvalidOperationException("Các lớp thép trên và dưới chồng lấn trong tiết diện.");
            if (createLongitudinal)
            foreach (var row in topRows.Concat(bottomRows))
            {
                if (row.Item2 <= 0) continue;
                double defaultInset = cover + tieDia + row.Item3.BarNominalDiameter / 2.0;
                double totalLengthMm = UnitUtils.ConvertFromInternalUnits(2 * f.HalfLength, UnitTypeId.Millimeters);
                List<DetailedBarSpec> barSpecs = Specs(row.Item1);
                if (row.Item1 == "top")
                    foreach (DetailedBarSpec bar in barSpecs)
                    {
                        bar.StartMm = 0;
                        bar.EndMm = totalLengthMm;
                    }
                if (barSpecs.Count == 0)
                    barSpecs.Add(new DetailedBarSpec { Key = row.Item1, Count = row.Item2, Type = row.Item3,
                        StartMm = 0, EndMm = totalLengthMm, SectionPosition = 1,
                        StartHook = row.Item1.StartsWith("top", StringComparison.Ordinal) ? options.TopStartHook : options.BottomStartHook,
                        EndHook = row.Item1.StartsWith("top", StringComparison.Ordinal) ? options.TopEndHook : options.BottomEndHook });
                // Top support bars are continuous through a column/support. The other
                // reinforcement groups are clipped to individual spans at construction.
                if (row.Item1 == "top2" || row.Item1 == "bottom1" || row.Item1 == "bottom2")
                    barSpecs = SplitReinforcementBarsBySpans(barSpecs, options, totalLengthMm);
                foreach (var matchingBars in barSpecs.GroupBy(bar => new
                {
                    TypeId = (bar.Type ?? row.Item3).Id,
                    bar.StartMm, bar.EndMm, bar.SectionPosition, bar.LeftHookMm, bar.RightHookMm, bar.HookDownMm,
                    StartHookId = bar.StartHook?.Id, EndHookId = bar.EndHook?.Id, bar.BarGroupCount
                }))
                {
                bool completeSet = matchingBars.Key.BarGroupCount > 1 &&
                    matchingBars.All(bar => bar.Count == 1) &&
                    matchingBars.Select(bar => bar.BarIndex).OrderBy(index => index)
                        .SequenceEqual(Enumerable.Range(0, matchingBars.Key.BarGroupCount));
                foreach (DetailedBarSpec spec in completeSet ? matchingBars.Take(1) : matchingBars.AsEnumerable())
                {
                    RebarBarType barType = spec.Type ?? row.Item3;
                    if (barType == null) throw new InvalidOperationException("Chưa chọn đường kính cho thanh " + row.Item1 + ".");
                    double inset = cover + tieDia + barType.BarNominalDiameter / 2.0;
                    double requestedStartMm = spec.StartMm;
                    double requestedEndMm = spec.EndMm;
                    bool invalidStoredRange = double.IsNaN(requestedStartMm) || double.IsInfinity(requestedStartMm) ||
                        double.IsNaN(requestedEndMm) || double.IsInfinity(requestedEndMm) ||
                        requestedEndMm <= requestedStartMm || requestedEndMm <= 0 || requestedStartMm >= totalLengthMm;
                    bool primaryBar = row.Item1 == "top" || row.Item1 == "bottom";
                    if (invalidStoredRange && primaryBar)
                    {
                        // Main bars are continuous by default. Saved detail from a longer beam
                        // can otherwise leave the whole interval outside a shorter target beam.
                        requestedStartMm = 0;
                        requestedEndMm = totalLengthMm;
                    }
                    else if (invalidStoredRange)
                    {
                        throw new InvalidOperationException("Khoảng thanh " + row.Item1 + " (" +
                            requestedStartMm.ToString("0.##") + "–" + requestedEndMm.ToString("0.##") +
                            " mm) không nằm hợp lệ trong dầm dài " + totalLengthMm.ToString("0.##") + " mm.");
                    }
                    requestedStartMm = Math.Max(0, Math.Min(totalLengthMm, requestedStartMm));
                    requestedEndMm = Math.Max(requestedStartMm, Math.Min(totalLengthMm, requestedEndMm));
                    double start = Math.Max(-f.HalfLength + inset, -f.HalfLength + UnitUtils.ConvertToInternalUnits(requestedStartMm, UnitTypeId.Millimeters));
                    double end = Math.Min(f.HalfLength - inset, -f.HalfLength + UnitUtils.ConvertToInternalUnits(requestedEndMm, UnitTypeId.Millimeters));
                    if (options.TcvnCutoffEnabled && (requestedStartMm > 0 || requestedEndMm < totalLengthMm))
                    {
                        double anchorageMm = CalculateTcvnRequiredAnchorageMm(barType, options.TcvnConcreteRbtMpa, options.TcvnSteelRsMpa,
                            options.TcvnBondEta1, options.TcvnAreaRatio, options.TcvnAlpha);
                        double anchorage = UnitUtils.ConvertToInternalUnits(anchorageMm, UnitTypeId.Millimeters);
                        if (requestedStartMm > 0)
                        {
                            start -= anchorage;
                            if (start < -f.HalfLength + doc.Application.ShortCurveTolerance)
                                throw new InvalidOperationException("Điểm cắt đầu " + row.Item1 + " quá gần đầu dầm để neo thêm " + anchorageMm.ToString("0") + " mm trong bê tông. Dời điểm cắt vào trong nhịp.");
                        }
                        if (requestedEndMm < totalLengthMm)
                        {
                            end += anchorage;
                            if (end > f.HalfLength - doc.Application.ShortCurveTolerance)
                                throw new InvalidOperationException("Điểm cắt cuối " + row.Item1 + " quá gần đầu dầm để neo thêm " + anchorageMm.ToString("0") + " mm trong bê tông. Dời điểm cắt vào trong nhịp.");
                        }
                    }
                    bool hookDownTopMain = row.Item1.StartsWith("top", StringComparison.Ordinal);
                    double hookDownMm = hookDownTopMain ? Math.Max(0, spec.HookDownMm) : 0;
                    RebarHookType startHook = hookDownTopMain ? null : spec.StartHook;
                    RebarHookType endHook = hookDownTopMain ? null : spec.EndHook;
                    if (end <= start)
                        throw new InvalidOperationException("Khoảng thanh " + row.Item1 + " sau khi áp lớp bảo vệ/neo không còn chiều dài hợp lệ (" +
                            requestedStartMm.ToString("0.##") + "–" + requestedEndMm.ToString("0.##") + " mm trên dầm dài " +
                            totalLengthMm.ToString("0.##") + " mm).");
                    int groupCount = Math.Max(1, spec.BarGroupCount > 0 ? spec.BarGroupCount : spec.Count);
                    int barIndex = !completeSet && spec.BarIndex >= 0 ? Math.Min(groupCount - 1, spec.BarIndex) : -1;
                    double barHeight = row.Item4;
                    double baseSectionPosition = Spec(row.Item1)?.SectionPosition ?? 1.0;
                    double positionOffset = UnitUtils.ConvertToInternalUnits((spec.SectionPosition - baseSectionPosition) * options.LongitudinalLayerSpacingMm, UnitTypeId.Millimeters);
                    barHeight += row.Item1.StartsWith("top", StringComparison.Ordinal) ? -positionOffset : positionOffset;
                    if (barType.BarNominalDiameter != row.Item3.BarNominalDiameter)
                        barHeight += (row.Item1.StartsWith("top", StringComparison.Ordinal) ? -1 : 1) * (row.Item3.BarNominalDiameter - barType.BarNominalDiameter) / 2.0;
                    CreateLongitudinalRow(doc, beam, f, barType, completeSet ? groupCount : spec.Count, cover, tieDia, barHeight, start, end, startHook, endHook, barIndex, groupCount,
                        hookDownTopMain && (row.Item1 == "top" || spec.StartMm <= 1) ? hookDownMm : 0,
                        hookDownTopMain && (row.Item1 == "top" || spec.EndMm >= totalLengthMm - 1) ? hookDownMm : 0,
                        hookDownTopMain ? spec.LeftHookMm : 0, hookDownTopMain ? spec.RightHookMm : 0,
                        (row.Item1 == "bottom" || row.Item1 == "bottom1" || row.Item1 == "bottom2") ? options.PreviewBeamSections : null);
                }
                }
            }
            if (createLongitudinal && options.SideBarCount > 0)
                CreateSideBars(doc, beam, f, options.SideBarType ?? topType, options.SideBarCount, cover, tieDia, hBottom, hTop, tieStart, tieEnd,
                    options.SideBarMinimumHeightMm, options.SideBarSpacingMm, options.SideBarAnchorageMm);

            if (!createTies) return 0;

            double requestedSpacing = UnitUtils.ConvertToInternalUnits(options.TieSpacingMm, UnitTypeId.Millimeters);
            List<Tuple<double, double>> supportSpans = BuildSupportedTieSpans(doc, options, f, tieStart, tieEnd);
            var stations = new List<double>();
            foreach (Tuple<double, double> span in supportSpans)
                stations.AddRange(BuildTieStations(doc, options, span.Item1, span.Item2, requestedSpacing));
            if (stations.Count > 1000) throw new InvalidOperationException("Bước đai tạo ra hơn 1000 đai cho một dầm.");
            int tieIndex = 0;
            for (int stationIndex = 0; stationIndex < stations.Count;)
            {
                int runEnd = stationIndex;
                double spacing = 0;
                if (stationIndex + 1 < stations.Count)
                {
                    spacing = stations[stationIndex + 1] - stations[stationIndex];
                    runEnd = stationIndex + 1;
                    double tolerance = Math.Max(doc.Application.ShortCurveTolerance, spacing * 1e-6);
                    while (runEnd + 1 < stations.Count &&
                        Math.Abs((stations[runEnd + 1] - stations[runEnd]) - spacing) <= tolerance)
                        runEnd++;
                }
                int positions = runEnd - stationIndex + 1;
                tieIndex += positions;
                double station = stations[stationIndex];
                double halfW = innerHalfW;
                double halfH = innerHalfH;
                XYZ p0 = Point(f, f.WidthCenter - halfW, f.HeightCenter - halfH, station);
                XYZ p1 = Point(f, f.WidthCenter + halfW, f.HeightCenter - halfH, station);
                XYZ p2 = Point(f, f.WidthCenter + halfW, f.HeightCenter + halfH, station);
                XYZ p3 = Point(f, f.WidthCenter - halfW, f.HeightCenter + halfH, station);
                var curves = new List<Curve>
                {
                    Line.CreateBound(p0, p1), Line.CreateBound(p1, p2),
                    Line.CreateBound(p2, p3), Line.CreateBound(p3, p0)
                };
                Rebar tie = ExistingProjectRebar.Create(doc, RebarStyle.StirrupTie, options.TieType,
                    null, null, beam, f.Axis, curves, RebarHookOrientation.Left, RebarHookOrientation.Right, "Đai dầm " + beam.Id.Value + " / vị trí " + tieIndex);
                if (tie == null) throw new InvalidOperationException("Revit không tạo được đai số " + tieIndex + ".");
                if (positions > 1)
                {
                    try
                    {
                        using (RebarShapeDrivenAccessor accessor = tie.GetShapeDrivenAccessor())
                            accessor.SetLayoutAsFixedNumber(positions, stations[runEnd] - station, true, true, true);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("Không thể gom nhóm " + positions + " đai liên tiếp thành Rebar Set: " + ex.Message, ex);
                    }
                }
                SetPartition(tie, beam.Id);
                stationIndex = runEnd + 1;
            }
            return stations.Count;
        }

        private static List<DetailedBarSpec> SplitReinforcementBarsBySpans(
            IList<DetailedBarSpec> source, Options options, double totalLengthMm)
        {
            var spans = BuildBeamAxisSpans(options, totalLengthMm);
            if (spans.Count <= 1) return source.ToList();

            var result = new List<DetailedBarSpec>();
            foreach (DetailedBarSpec bar in source)
            {
                bool createdSegment = false;
                foreach (Tuple<double, double> span in spans)
                {
                    double start = Math.Max(Math.Max(0, bar.StartMm), span.Item1);
                    double end = Math.Min(Math.Min(totalLengthMm, bar.EndMm), span.Item2);
                    if (end - start < 50) continue;
                    result.Add(new DetailedBarSpec
                    {
                        Key = bar.Key, Count = bar.Count, BarIndex = bar.BarIndex, BarGroupCount = bar.BarGroupCount,
                        Type = bar.Type, IsTop = bar.IsTop, StartMm = start, EndMm = end,
                        SectionPosition = bar.SectionPosition, LeftHookMm = bar.LeftHookMm, RightHookMm = bar.RightHookMm, HookDownMm = bar.HookDownMm,
                        StartPointIndex = bar.StartPointIndex, EndPointIndex = bar.EndPointIndex, LeftRatio = bar.LeftRatio, RightRatio = bar.RightRatio,
                        StartHook = Math.Abs(start - bar.StartMm) < 1 ? bar.StartHook : null,
                        EndHook = Math.Abs(end - bar.EndMm) < 1 ? bar.EndHook : null
                    });
                    createdSegment = true;
                }
                if (!createdSegment) result.Add(bar);
            }
            return result;
        }

        internal static List<Tuple<double, double>> BuildSupportReinforcementRanges(IList<Tuple<double, double>> spans)
        {
            var ordered = (spans ?? new List<Tuple<double, double>>())
                .Where(span => span != null && span.Item2 - span.Item1 >= 250)
                .OrderBy(span => span.Item1).ToList();
            var ranges = new List<Tuple<double, double>>();
            if (ordered.Count == 0) return ranges;

            // One range per support: end supports receive a bar into the adjacent span;
            // each internal support receives one bar that continues through the support.
            for (int supportIndex = 0; supportIndex <= ordered.Count; supportIndex++)
            {
                double start = supportIndex == 0
                    ? ordered[0].Item1
                    : ordered[supportIndex - 1].Item2 - (ordered[supportIndex - 1].Item2 - ordered[supportIndex - 1].Item1) / 4.0;
                double end = supportIndex == ordered.Count
                    ? ordered[ordered.Count - 1].Item2
                    : ordered[supportIndex].Item1 + (ordered[supportIndex].Item2 - ordered[supportIndex].Item1) / 4.0;
                if (end - start >= 50) ranges.Add(Tuple.Create(start, end));
            }
            return ranges;
        }

        private static List<Tuple<double, double>> BuildBeamAxisSpans(Options options, double totalLengthMm)
        {
            var nodes = new List<double> { 0, totalLengthMm };
            foreach (BeamPreviewSupport support in (options.PreviewSupports ?? new List<BeamPreviewSupport>())
                .Where(s => s != null).OrderBy(s => s.StartMm))
            {
                double center = (support.StartMm + support.EndMm) * 0.5;
                if (center <= 150 || center >= totalLengthMm - 150) continue;
                if (!nodes.Any(node => Math.Abs(node - center) < 80)) nodes.Add(center);
            }
            nodes = nodes.OrderBy(node => node).ToList();
            var spans = new List<Tuple<double, double>>();
            for (int i = 0; i < nodes.Count - 1; i++)
                if (nodes[i + 1] - nodes[i] > 150)
                    spans.Add(Tuple.Create(nodes[i], nodes[i + 1]));
            return spans.Count > 0 ? spans : new List<Tuple<double, double>> { Tuple.Create(0.0, totalLengthMm) };
        }

        private static List<Tuple<double, double>> BuildSupportedTieSpans(Document doc, Options options, Frame frame, double fallbackStart, double fallbackEnd)
        {
            double totalMm = UnitUtils.ConvertFromInternalUnits(2 * frame.HalfLength, UnitTypeId.Millimeters);
            double offsetMm = Math.Max(0, options.StirrupSupportOffsetMm);
            double edgeMm = UnitUtils.ConvertFromInternalUnits(fallbackStart + frame.HalfLength, UnitTypeId.Millimeters);
            var columns = (options.PreviewSupports ?? new List<BeamPreviewSupport>())
                .Where(s => s.Kind == "Cột giao" && s.EndMm > 0 && s.StartMm < totalMm)
                .OrderBy(s => s.StartMm).ToList();
            if (columns.Count == 0) return new List<Tuple<double, double>> { Tuple.Create(fallbackStart, fallbackEnd) };

            var spans = new List<Tuple<double, double>>();
            double startMm = edgeMm;
            double minGap = Math.Max(50, options.CoverMm * 2);
            foreach (BeamPreviewSupport support in columns)
            {
                double supportStart = Math.Max(0, support.StartMm), supportEnd = Math.Min(totalMm, support.EndMm);
                double endMm = Math.Max(0, supportStart - offsetMm);
                if (endMm - startMm >= minGap) spans.Add(Tuple.Create(
                    -frame.HalfLength + UnitUtils.ConvertToInternalUnits(startMm, UnitTypeId.Millimeters),
                    -frame.HalfLength + UnitUtils.ConvertToInternalUnits(endMm, UnitTypeId.Millimeters)));
                startMm = Math.Min(totalMm, supportEnd + offsetMm);
            }
            double end = totalMm - edgeMm;
            if (end - startMm >= minGap) spans.Add(Tuple.Create(
                -frame.HalfLength + UnitUtils.ConvertToInternalUnits(startMm, UnitTypeId.Millimeters),
                -frame.HalfLength + UnitUtils.ConvertToInternalUnits(end, UnitTypeId.Millimeters)));
            return spans.Count == 0 ? new List<Tuple<double, double>> { Tuple.Create(fallbackStart, fallbackEnd) } : spans;
        }

        private static List<double> BuildTieStations(Document doc, Options options, double start, double end, double uniformSpacing)
        {
            if (!options.UseZonedTies)
            {
                var uniform = new List<double>();
                AddStations(uniform, start, end, uniformSpacing, doc.Application.ShortCurveTolerance);
                return uniform;
            }

            double zone = UnitUtils.ConvertToInternalUnits(options.TieEndZoneLengthMm, UnitTypeId.Millimeters);
            double endSpacing = UnitUtils.ConvertToInternalUnits(options.TieEndSpacingMm, UnitTypeId.Millimeters);
            double middleSpacing = UnitUtils.ConvertToInternalUnits(options.TieMiddleSpacingMm, UnitTypeId.Millimeters);
            double span = end - start;
            if (zone <= 0 || span <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Chiều dài vùng đầu đai và đoạn đặt đai phải lớn hơn 0.");
            if (endSpacing <= 0 || middleSpacing <= 0)
                throw new InvalidOperationException("Bước đai vùng đầu và vùng giữa phải lớn hơn 0.");

            // Short spans can be shorter than the sum of both configured end zones.
            // In that case the two dense end zones meet at midspan and cover the span;
            // they must not make the entire beam transaction fail.
            zone = Math.Min(zone, span / 2.0);
            double leftEnd = start + zone;
            double rightStart = end - zone;
            var stations = new List<double>();
            AddStations(stations, start, leftEnd, endSpacing, doc.Application.ShortCurveTolerance);
            if (rightStart - leftEnd > doc.Application.ShortCurveTolerance)
                AddStations(stations, leftEnd, rightStart, middleSpacing, doc.Application.ShortCurveTolerance);
            AddStations(stations, rightStart, end, endSpacing, doc.Application.ShortCurveTolerance);
            return stations;
        }

        private static void AddStations(List<double> stations, double start, double end, double maxSpacing, double tolerance)
        {
            if (maxSpacing <= 0 || end < start) throw new InvalidOperationException("Bước đai không hợp lệ.");
            double requestedIntervals = Math.Ceiling((end - start) / maxSpacing);
            if (requestedIntervals > 1000) throw new InvalidOperationException("Một vùng tạo ra hơn 1000 khoảng đai.");
            int intervals = Math.Max(1, (int)requestedIntervals);
            for (int i = 0; i <= intervals; i++)
            {
                double station = start + (end - start) * i / intervals;
                if (stations.Count == 0 || Math.Abs(stations[stations.Count - 1] - station) > tolerance / 10.0)
                    stations.Add(station);
            }
        }

        private static void CreateLongitudinalRow(Document doc, FamilyInstance beam, Frame f, RebarBarType type,
            int count, double cover, double tieDia, double h, double start, double end,
            RebarHookType startHook, RebarHookType endHook, int fixedIndex = -1, int fixedCount = 0,
            double startHookDownMm = 0, double endHookDownMm = 0, double leftHookFootMm = 0, double rightHookFootMm = 0,
            IList<BeamPreviewSection> bottomSections = null)
        {
            double inset = cover + tieDia + type.BarNominalDiameter / 2.0;
            double minW = f.WidthCenter - f.HalfWidth + inset;
            double maxW = f.WidthCenter + f.HalfWidth - inset;
            int distributionCount = fixedCount > 0 ? fixedCount : count;
            if (maxW < minW || (distributionCount > 1 && maxW - minW < type.BarNominalDiameter))
                throw new InvalidOperationException("Không đủ bề rộng tiết diện cho lớp thép dọc " + type.Name + ".");
            bool makeDistributedSet = fixedIndex < 0 && count > 1;
            int createCount = makeDistributedSet ? 1 : count;
            for (int i = 0; i < createCount; i++)
            {
                int laneCount = fixedCount > 0 ? fixedCount : count;
                int laneIndex = fixedCount > 0 && fixedIndex >= 0 ? fixedIndex : makeDistributedSet ? 0 : i;
                double w = laneCount == 1 ? (minW + maxW) / 2.0 : minW + (maxW - minW) * laneIndex / (laneCount - 1.0);
                XYZ a = Point(f, w, h, start), b = Point(f, w, h, end);
                double startDown = UnitUtils.ConvertToInternalUnits(Math.Max(0, startHookDownMm), UnitTypeId.Millimeters);
                double endDown = UnitUtils.ConvertToInternalUnits(Math.Max(0, endHookDownMm), UnitTypeId.Millimeters);
                double leftFoot = UnitUtils.ConvertToInternalUnits(Math.Max(0, leftHookFootMm), UnitTypeId.Millimeters);
                double rightFoot = UnitUtils.ConvertToInternalUnits(Math.Max(0, rightHookFootMm), UnitTypeId.Millimeters);
                if (startDown > 0 && endDown > 0 && start + leftFoot + rightFoot >= end)
                    throw new InvalidOperationException("Neo X trái/phải làm chồng lấn móc thép chủ trên. Giảm chiều dài neo X.");
                double bottomLimit = f.HeightCenter - f.HalfHeight + cover + tieDia + type.BarNominalDiameter / 2.0;
                if ((startDown > 0 && h - startDown < bottomLimit) || (endDown > 0 && h - endDown < bottomLimit))
                    throw new InvalidOperationException("Móc xuống thép chủ trên dài quá tiết diện. Giảm Ly hoặc tăng chiều cao dầm.");
                var curves = new List<Curve>();
                if (startDown > doc.Application.ShortCurveTolerance)
                {
                    XYZ leftDown = Point(f, w, h - startDown, start);
                    if (leftFoot > doc.Application.ShortCurveTolerance)
                        curves.Add(Line.CreateBound(Point(f, w, h - startDown, start + leftFoot), leftDown));
                    curves.Add(Line.CreateBound(leftDown, a));
                }
                curves.Add(Line.CreateBound(a, b));
                if (endDown > doc.Application.ShortCurveTolerance)
                {
                    XYZ rightDown = Point(f, w, h - endDown, end);
                    curves.Add(Line.CreateBound(b, rightDown));
                    if (rightFoot > doc.Application.ShortCurveTolerance)
                        curves.Add(Line.CreateBound(rightDown, Point(f, w, h - endDown, end - rightFoot)));
                }
                if (bottomSections != null && bottomSections.Count > 1)
                {
                    var profiles = bottomSections.Where(section =>
                        section.EndMm > UnitUtils.ConvertFromInternalUnits(start + f.HalfLength, UnitTypeId.Millimeters) &&
                        section.StartMm < UnitUtils.ConvertFromInternalUnits(end + f.HalfLength, UnitTypeId.Millimeters))
                        .OrderBy(section => section.StartMm).ToList();
                    if (profiles.Count > 0)
                    {
                        double layerOffset = h - (f.HeightCenter - f.HalfHeight + inset);
                        double BottomHeight(BeamPreviewSection section) =>
                            UnitUtils.ConvertToInternalUnits(section.BottomElevationMm, UnitTypeId.Millimeters) + inset + layerOffset;
                        var path = new List<XYZ> { Point(f, w, BottomHeight(profiles[0]), start) };
                        double lastStation = start;
                        for (int sectionIndex = 1; sectionIndex < profiles.Count; sectionIndex++)
                        {
                            double beforeHeight = BottomHeight(profiles[sectionIndex - 1]);
                            double afterHeight = BottomHeight(profiles[sectionIndex]);
                            double difference = afterHeight - beforeHeight;
                            if (Math.Abs(difference) < doc.Application.ShortCurveTolerance) continue;
                            double boundary = -f.HalfLength + UnitUtils.ConvertToInternalUnits(
                                profiles[sectionIndex].StartMm, UnitTypeId.Millimeters);
                            // The sloped transition stays in the deeper section.
                            double transitionLength = Math.Abs(difference) * 6;
                            double rampStart = difference > 0 ? boundary - transitionLength : boundary;
                            double rampEnd = difference > 0 ? boundary : boundary + transitionLength;
                            if (rampStart <= lastStation + doc.Application.ShortCurveTolerance ||
                                rampEnd >= end - doc.Application.ShortCurveTolerance)
                                throw new InvalidOperationException("Không đủ chiều dài đoạn dầm để uốn chuyển tiếp thép đáy với tỷ lệ ngang/đứng 6:1.");
                            path.Add(Point(f, w, beforeHeight, rampStart));
                            path.Add(Point(f, w, afterHeight, rampEnd));
                            lastStation = rampEnd;
                        }
                        path.Add(Point(f, w, BottomHeight(profiles[profiles.Count - 1]), end));
                        curves.Clear();
                        for (int pointIndex = 1; pointIndex < path.Count; pointIndex++)
                            if (path[pointIndex].DistanceTo(path[pointIndex - 1]) > doc.Application.ShortCurveTolerance)
                                curves.Add(Line.CreateBound(path[pointIndex - 1], path[pointIndex]));
                    }
                }
                Rebar bar = ExistingProjectRebar.Create(doc, RebarStyle.Standard, type,
                    startDown > 0 || endDown > 0 ? null : startHook,
                    startDown > 0 || endDown > 0 ? null : endHook, beam, f.Width,
                    curves, RebarHookOrientation.Left, RebarHookOrientation.Right, "Thép dọc dầm " + beam.Id.Value);
                if (bar == null) throw new InvalidOperationException("Revit không tạo được thép dọc.");
                if (makeDistributedSet)
                {
                    try
                    {
                        if (bar.IsRebarFreeForm())
                        {
                            using (RebarFreeFormAccessor accessor = bar.GetFreeFormAccessor())
                                accessor.SetLayoutAsFixedNumber(count);
                        }
                        else
                        {
                            using (RebarShapeDrivenAccessor accessor = bar.GetShapeDrivenAccessor())
                                accessor.SetLayoutAsFixedNumber(count, maxW - minW, true, true, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("Không thể gom nhóm " + count + " thanh dọc thành Rebar Set: " + ex.Message, ex);
                    }
                }
                SetPartition(bar, beam.Id);
            }
        }

        internal static double CalculateTcvnRequiredAnchorageMm(RebarBarType barType, double concreteRbtMpa,
            double steelRsMpa, double bondEta1, double areaRatio, double alpha)
        {
            if (barType == null || concreteRbtMpa <= 0 || steelRsMpa <= 0 || bondEta1 <= 0 || areaRatio <= 0 || alpha <= 0)
                throw new InvalidOperationException("Nhập Rbt, Rs, η1, As,cal/As,ef và α dương để tính neo TCVN 5574:2018.");
            double d = UnitUtils.ConvertFromInternalUnits(barType.BarNominalDiameter, UnitTypeId.Millimeters);
            double eta2 = d >= 36.0 ? 0.9 : 1.0;
            // TCVN 5574:2018, 10.3.5.4–10.3.5.5.
            double baseLength = steelRsMpa * d / (4.0 * bondEta1 * eta2 * concreteRbtMpa);
            double calculated = alpha * baseLength * areaRatio;
            return Math.Max(calculated, Math.Max(Math.Max(15.0 * d, 200.0), 0.3 * baseLength));
        }

        private static void CreateSideBars(Document doc, FamilyInstance beam, Frame f, RebarBarType type, int count,
            double cover, double tieDia, double bottom, double top, double start, double end,
            double minimumHeightMm, double spacingMm, double anchorageMm)
        {
            double clearHeightMm = UnitUtils.ConvertFromInternalUnits(2 * f.HalfHeight, UnitTypeId.Millimeters);
            if (minimumHeightMm > 0 && clearHeightMm < minimumHeightMm) return;
            double sideInset = cover + tieDia + type.BarNominalDiameter / 2.0;
            double left = f.WidthCenter - f.HalfWidth + sideInset;
            double right = f.WidthCenter + f.HalfWidth - sideInset;
            double low = bottom + type.BarNominalDiameter / 2.0;
            double high = top - type.BarNominalDiameter / 2.0;
            if (spacingMm > 0 && high > low)
            {
                int spacingRows = (int)Math.Ceiling(UnitUtils.ConvertFromInternalUnits(high - low, UnitTypeId.Millimeters) / spacingMm) + 1;
                count = Math.Max(count, Math.Min(12, spacingRows));
            }
            if (right <= left || high <= low || count < 1 || count > 12)
                throw new InvalidOperationException("Thép chống phình không vừa chiều rộng hoặc chiều cao tiết diện.");
            double anchorage = UnitUtils.ConvertToInternalUnits(Math.Max(0, anchorageMm), UnitTypeId.Millimeters);
            start = Math.Max(-f.HalfLength + cover, start - anchorage);
            end = Math.Min(f.HalfLength - cover, end + anchorage);
            for (int i=0; i<count; i++)
            {
                double height = count == 1 ? (low + high) / 2.0 : low + (high - low) * i / (count - 1.0);
                foreach (double width in new[] { left, right })
                {
                    XYZ a = Point(f, width, height, start), b = Point(f, width, height, end);
                    Rebar bar = ExistingProjectRebar.Create(doc, RebarStyle.Standard, type, null, null, beam, f.Width,
                        new List<Curve> { Line.CreateBound(a, b) }, RebarHookOrientation.Left, RebarHookOrientation.Right, "Thép chống phình dầm " + beam.Id.Value);
                    if (bar == null) throw new InvalidOperationException("Revit không tạo được thép chống phình.");
                    SetPartition(bar, beam.Id);
                }
            }
        }

        private static XYZ Point(Frame f, double width, double height, double station) =>
            f.Origin + f.Width * width + f.Up * height + f.Axis * station;

        private static void SetPartition(Rebar rebar, ElementId beamId)
        {
            Parameter p = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (p != null && !p.IsReadOnly) p.Set("KSTR_BEAM_" + beamId.Value);
        }

        internal static Options ChooseOptions(IntPtr owner, IList<RebarBarType> types, IList<RebarHookType> hookTypes,
            Options defaults, bool settingsOnly, string titleOverride = null)
        {
            return BeamRebarEditorWindow.ShowQuick(owner, types, hookTypes, defaults, settingsOnly, titleOverride);
#pragma warning disable CS0162
            List<BarChoice> choices = types.Select(t => new BarChoice(t)).ToList();
            int mainIndex = defaults == null ? 0 : choices.FindIndex(c => c.Type.Id == defaults.MainType.Id);
            int topIndex = defaults == null ? mainIndex : choices.FindIndex(c => c.Type.Id == (defaults.TopType ?? defaults.MainType).Id);
            int bottomIndex = defaults == null ? mainIndex : choices.FindIndex(c => c.Type.Id == (defaults.BottomType ?? defaults.MainType).Id);
            int tieIndex = defaults == null ? Math.Min(1, choices.Count - 1) : choices.FindIndex(c => c.Type.Id == defaults.TieType.Id);
            if (mainIndex < 0) mainIndex = 0;
            if (topIndex < 0) topIndex = mainIndex;
            if (bottomIndex < 0) bottomIndex = mainIndex;
            if (tieIndex < 0) tieIndex = Math.Min(1, choices.Count - 1);
            var window = new Window
            {
                Title = titleOverride ?? (settingsOnly ? "Cài đặt cốt thép dầm - KStruct" : "Cốt thép dầm thẳng - KStruct"),
                Width = 430,
                Height = 620,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            var topCombo = AddType(root, "Thép chủ trên:", choices, topIndex);
            var bottomCombo = AddType(root, "Thép chủ dưới:", choices, bottomIndex);
            List<HookChoice> hookChoices = new[] { new HookChoice(null) }.Concat((hookTypes ?? new List<RebarHookType>()).Select(t => new HookChoice(t))).ToList();
            var topStartHookCombo = AddHookType(root, "Móc đầu trái thép trên:", hookChoices, defaults?.TopStartHook);
            var topEndHookCombo = AddHookType(root, "Móc đầu phải thép trên:", hookChoices, defaults?.TopEndHook);
            var bottomStartHookCombo = AddHookType(root, "Móc đầu trái thép dưới:", hookChoices, defaults?.BottomStartHook);
            var bottomEndHookCombo = AddHookType(root, "Móc đầu phải thép dưới:", hookChoices, defaults?.BottomEndHook);
            var tieCombo = AddType(root, "Đai:", choices, tieIndex);
            var topBox = AddInput(root, "Số thanh lớp trên:", defaults == null ? "2" : defaults.TopCount.ToString());
            var bottomBox = AddInput(root, "Số thanh lớp dưới:", defaults == null ? "2" : defaults.BottomCount.ToString());
            var topLayer2Box = AddInput(root, "Số thanh lớp trên thứ hai (0 = tắt):", defaults == null ? "0" : defaults.TopLayer2Count.ToString());
            int topLayerTypeIndex = defaults == null ? topIndex : choices.FindIndex(choice => choice.Type.Id == (defaults.TopLayer2Type ?? defaults.TopType ?? defaults.MainType).Id);
            if (topLayerTypeIndex < 0) topLayerTypeIndex = mainIndex;
            var topLayerTypeCombo = AddType(root, "Loại thép lớp trên thứ hai:", choices, topLayerTypeIndex);
            var bottomLayer2Box = AddInput(root, "Số thanh lớp dưới thứ hai (0 = tắt):", defaults == null ? "0" : defaults.BottomLayer2Count.ToString());
            int bottomLayerTypeIndex = defaults == null ? bottomIndex : choices.FindIndex(choice => choice.Type.Id == (defaults.BottomLayer2Type ?? defaults.BottomType ?? defaults.MainType).Id);
            if (bottomLayerTypeIndex < 0) bottomLayerTypeIndex = mainIndex;
            var bottomLayerTypeCombo = AddType(root, "Loại thép lớp dưới thứ hai:", choices, bottomLayerTypeIndex);
            var layerSpacingBox = AddInput(root, "Khoảng hở giữa hai lớp dọc (mm):", defaults == null ? "30" : (defaults.LongitudinalLayerSpacingMm > 0 ? defaults.LongitudinalLayerSpacingMm : 30).ToString());
            var coverBox = AddInput(root, "Lớp bảo vệ (mm):", defaults == null ? "30" : defaults.CoverMm.ToString());
            var spacingBox = AddInput(root, "Bước đai tối đa (mm):", defaults == null ? "150" : defaults.TieSpacingMm.ToString());
            var zonedBox = new CheckBox { Content = "Chia bước đai vùng đầu / vùng giữa", IsChecked = defaults != null && defaults.UseZonedTies, Margin = new Thickness(0, 3, 0, 2) };
            root.Children.Add(zonedBox);
            var zoneLengthBox = AddInput(root, "Chiều dài mỗi vùng đầu đai (mm):", defaults == null ? "600" : (defaults.TieEndZoneLengthMm > 0 ? defaults.TieEndZoneLengthMm : 600).ToString());
            var endSpacingBox = AddInput(root, "Bước đai vùng đầu (mm):", defaults == null ? "100" : (defaults.TieEndSpacingMm > 0 ? defaults.TieEndSpacingMm : defaults.TieSpacingMm).ToString());
            var middleSpacingBox = AddInput(root, "Bước đai vùng giữa (mm):", defaults == null ? "150" : (defaults.TieMiddleSpacingMm > 0 ? defaults.TieMiddleSpacingMm : defaults.TieSpacingMm).ToString());
            zoneLengthBox.IsEnabled = endSpacingBox.IsEnabled = middleSpacingBox.IsEnabled = zonedBox.IsChecked == true;
            zonedBox.Checked += (sender, args) => zoneLengthBox.IsEnabled = endSpacingBox.IsEnabled = middleSpacingBox.IsEnabled = true;
            zonedBox.Unchecked += (sender, args) => zoneLengthBox.IsEnabled = endSpacingBox.IsEnabled = middleSpacingBox.IsEnabled = false;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = settingsOnly ? "Lưu cài đặt" : "Tạo thép", Width = 92, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                int top, bottom, topLayer2, bottomLayer2;
                double cover, spacing, zoneLength, endSpacing, middleSpacing, layerSpacing;
                if (topCombo.SelectedItem == null || bottomCombo.SelectedItem == null || tieCombo.SelectedItem == null ||
                    topLayerTypeCombo.SelectedItem == null || bottomLayerTypeCombo.SelectedItem == null ||
                    !int.TryParse(topBox.Text, out top) || top < 1 || top > 12 ||
                    !int.TryParse(bottomBox.Text, out bottom) || bottom < 1 || bottom > 12 ||
                    !int.TryParse(topLayer2Box.Text, out topLayer2) || topLayer2 < 0 || topLayer2 > 12 ||
                    !int.TryParse(bottomLayer2Box.Text, out bottomLayer2) || bottomLayer2 < 0 || bottomLayer2 > 12 ||
                    !double.TryParse(layerSpacingBox.Text, out layerSpacing) || layerSpacing <= 0 ||
                    !double.TryParse(coverBox.Text, out cover) || cover <= 0 ||
                    !double.TryParse(spacingBox.Text, out spacing) || spacing <= 0 ||
                    !double.TryParse(zoneLengthBox.Text, out zoneLength) || zoneLength <= 0 ||
                    !double.TryParse(endSpacingBox.Text, out endSpacing) || endSpacing <= 0 ||
                    !double.TryParse(middleSpacingBox.Text, out middleSpacing) || middleSpacing <= 0)
                {
                    TaskDialog.Show("KStruct - Cốt thép dầm", "Số thanh chính phải từ 1 đến 12; số thanh lớp phụ từ 0 đến 12; cover, khoảng hở lớp và bước đai phải lớn hơn 0.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new Options
            {
                MainType = ((BarChoice)topCombo.SelectedItem).Type,
                TopType = ((BarChoice)topCombo.SelectedItem).Type,
                BottomType = ((BarChoice)bottomCombo.SelectedItem).Type,
                TopStartHook = ((HookChoice)topStartHookCombo.SelectedItem).Type,
                TopEndHook = ((HookChoice)topEndHookCombo.SelectedItem).Type,
                BottomStartHook = ((HookChoice)bottomStartHookCombo.SelectedItem).Type,
                BottomEndHook = ((HookChoice)bottomEndHookCombo.SelectedItem).Type,
                TieType = ((BarChoice)tieCombo.SelectedItem).Type,
                TopCount = int.Parse(topBox.Text), BottomCount = int.Parse(bottomBox.Text),
                TopLayer2Count = int.Parse(topLayer2Box.Text), BottomLayer2Count = int.Parse(bottomLayer2Box.Text),
                TopLayer2Type = ((BarChoice)topLayerTypeCombo.SelectedItem).Type,
                BottomLayer2Type = ((BarChoice)bottomLayerTypeCombo.SelectedItem).Type,
                LongitudinalLayerSpacingMm = double.Parse(layerSpacingBox.Text),
                CoverMm = double.Parse(coverBox.Text), TieSpacingMm = double.Parse(spacingBox.Text),
                UseZonedTies = zonedBox.IsChecked == true, TieEndZoneLengthMm = double.Parse(zoneLengthBox.Text),
                TieEndSpacingMm = double.Parse(endSpacingBox.Text), TieMiddleSpacingMm = double.Parse(middleSpacingBox.Text)
            };
#pragma warning restore CS0162
        }

        internal static Options LoadSettings(Document doc, IList<RebarBarType> types, IList<RebarHookType> hookTypes)
        {
            string path = SettingsPath(doc);
            if (!File.Exists(path)) return null;
            try
            {
                BeamRebarSettingsFile saved;
                using (var input = File.OpenRead(path))
                    saved = (BeamRebarSettingsFile)new XmlSerializer(typeof(BeamRebarSettingsFile)).Deserialize(input);
                RebarBarType main = types.FirstOrDefault(t => t.Name == saved.MainTypeName);
                RebarBarType tie = types.FirstOrDefault(t => t.Name == saved.TieTypeName);
                RebarBarType top = string.IsNullOrWhiteSpace(saved.TopTypeName) ? main : types.FirstOrDefault(t => t.Name == saved.TopTypeName);
                RebarBarType bottom = string.IsNullOrWhiteSpace(saved.BottomTypeName) ? main : types.FirstOrDefault(t => t.Name == saved.BottomTypeName);
                RebarBarType topLayer = string.IsNullOrWhiteSpace(saved.TopLayer2TypeName) ? top : types.FirstOrDefault(t => t.Name == saved.TopLayer2TypeName);
                RebarBarType bottomLayer = string.IsNullOrWhiteSpace(saved.BottomLayer2TypeName) ? bottom : types.FirstOrDefault(t => t.Name == saved.BottomLayer2TypeName);
                RebarBarType topLayer3 = string.IsNullOrWhiteSpace(saved.TopLayer3TypeName) ? topLayer : types.FirstOrDefault(t => t.Name == saved.TopLayer3TypeName);
                RebarBarType bottomLayer3 = string.IsNullOrWhiteSpace(saved.BottomLayer3TypeName) ? bottomLayer : types.FirstOrDefault(t => t.Name == saved.BottomLayer3TypeName);
                RebarBarType sideBarType = string.IsNullOrWhiteSpace(saved.SideBarTypeName) ? top : types.FirstOrDefault(t => t.Name == saved.SideBarTypeName);
                RebarHookType topStartHook = ResolveHook(hookTypes, saved.TopStartHookTypeName);
                RebarHookType topEndHook = ResolveHook(hookTypes, saved.TopEndHookTypeName);
                RebarHookType bottomStartHook = ResolveHook(hookTypes, saved.BottomStartHookTypeName);
                RebarHookType bottomEndHook = ResolveHook(hookTypes, saved.BottomEndHookTypeName);
                if (HasMissingHook(hookTypes, saved.TopStartHookTypeName, topStartHook) || HasMissingHook(hookTypes, saved.TopEndHookTypeName, topEndHook) ||
                    HasMissingHook(hookTypes, saved.BottomStartHookTypeName, bottomStartHook) || HasMissingHook(hookTypes, saved.BottomEndHookTypeName, bottomEndHook)) return null;
                if (topLayer == null && saved.TopLayer2Count == 0) topLayer = top;
                if (bottomLayer == null && saved.BottomLayer2Count == 0) bottomLayer = bottom;
                if (topLayer3 == null && saved.TopLayer3Count == 0) topLayer3 = topLayer;
                if (bottomLayer3 == null && saved.BottomLayer3Count == 0) bottomLayer3 = bottomLayer;
                var detailedBars = new List<DetailedBarSpec>();
                foreach (BeamRebarDetailedBarSettings item in saved.DetailedBars ?? new List<BeamRebarDetailedBarSettings>())
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Key) || item.Count < 1) continue;
                    RebarBarType itemType = string.IsNullOrWhiteSpace(item.TypeName) ? null : types.FirstOrDefault(t => string.Equals(t.Name, item.TypeName, StringComparison.OrdinalIgnoreCase));
                    if (itemType == null)
                    {
                        if (item.Key == "top") itemType = top;
                        else if (item.Key == "bottom") itemType = bottom;
                        else if (item.Key == "top1") itemType = topLayer;
                        else if (item.Key == "top2") itemType = topLayer3;
                        else if (item.Key == "bottom1") itemType = bottomLayer;
                        else if (item.Key == "bottom2") itemType = bottomLayer3;
                    }
                    if (itemType == null) continue;
                    RebarHookType itemStartHook = ResolveHook(hookTypes, item.StartHookTypeName);
                    RebarHookType itemEndHook = ResolveHook(hookTypes, item.EndHookTypeName);
                    if (HasMissingHook(hookTypes, item.StartHookTypeName, itemStartHook) || HasMissingHook(hookTypes, item.EndHookTypeName, itemEndHook)) continue;
                    detailedBars.Add(new DetailedBarSpec { Key = item.Key, Count = item.Count, BarIndex = item.BarIndex, BarGroupCount = item.BarGroupCount,
                        Type = itemType, IsTop = item.Key.StartsWith("top", StringComparison.Ordinal), StartMm = item.StartMm, EndMm = item.EndMm, HookDownMm = item.HookDownMm,
                        SectionPosition = item.SectionPosition > 0 ? item.SectionPosition : 1, LeftHookMm = item.LeftHookMm, RightHookMm = item.RightHookMm,
                        StartPointIndex = item.StartPointIndex, EndPointIndex = item.EndPointIndex, LeftRatio = item.LeftRatio, RightRatio = item.RightRatio,
                        StartHook = itemStartHook, EndHook = itemEndHook });
                }
                if (main == null || tie == null || saved.TopCount < 1 || saved.TopCount > 12 ||
                    saved.BottomCount < 1 || saved.BottomCount > 12 || saved.TopLayer2Count < 0 || saved.TopLayer2Count > 12 ||
                    saved.BottomLayer2Count < 0 || saved.BottomLayer2Count > 12 || saved.TopLayer3Count < 0 || saved.TopLayer3Count > 12 ||
                    saved.BottomLayer3Count < 0 || saved.BottomLayer3Count > 12 || top == null || bottom == null || topLayer == null || bottomLayer == null || topLayer3 == null || bottomLayer3 == null ||
                    saved.CoverMm <= 0 || saved.TieSpacingMm <= 0)
                    return null;
                return new Options
                {
                    MainType = top, TopType = top, BottomType = bottom, TieType = tie, TopCount = saved.TopCount, BottomCount = saved.BottomCount,
                    TopLayer2Count = Math.Max(0, saved.TopLayer2Count), BottomLayer2Count = Math.Max(0, saved.BottomLayer2Count),
                    TopLayer3Count = Math.Max(0, saved.TopLayer3Count), BottomLayer3Count = Math.Max(0, saved.BottomLayer3Count),
                    TopLayer2Type = topLayer, BottomLayer2Type = bottomLayer,
                    TopLayer3Type = topLayer3, BottomLayer3Type = bottomLayer3,
                    SideBarCount = Math.Max(0, saved.SideBarCount), SideBarType = sideBarType ?? top,
                    TopStartHook = topStartHook, TopEndHook = topEndHook, BottomStartHook = bottomStartHook, BottomEndHook = bottomEndHook,
                    LongitudinalLayerSpacingMm = saved.LongitudinalLayerSpacingMm > 0 ? saved.LongitudinalLayerSpacingMm : 30,
                    CoverMm = saved.CoverMm, TieSpacingMm = saved.TieSpacingMm,
                    UseZonedTies = saved.UseZonedTies,
                    TieEndZoneLengthMm = saved.TieEndZoneLengthMm > 0 ? saved.TieEndZoneLengthMm : 600,
                    TieEndSpacingMm = saved.TieEndSpacingMm > 0 ? saved.TieEndSpacingMm : saved.TieSpacingMm,
                    TieMiddleSpacingMm = saved.TieMiddleSpacingMm > 0 ? saved.TieMiddleSpacingMm : saved.TieSpacingMm,
                    StirrupSupportOffsetMm = saved.StirrupSupportOffsetMm > 0 ? saved.StirrupSupportOffsetMm : 50,
                    SideBarMinimumHeightMm = saved.SideBarMinimumHeightMm > 0 ? saved.SideBarMinimumHeightMm : 700,
                    SideBarSpacingMm = saved.SideBarSpacingMm > 0 ? saved.SideBarSpacingMm : 400,
                    SideBarAnchorageMm = saved.SideBarAnchorageMm > 0 ? saved.SideBarAnchorageMm : 100,
                    TcvnCutoffEnabled = saved.TcvnCutoffEnabled,
                    TcvnConcreteRbtMpa = Math.Max(0,saved.TcvnConcreteRbtMpa), TcvnSteelRsMpa = Math.Max(0,saved.TcvnSteelRsMpa),
                    TcvnBondEta1 = saved.TcvnBondEta1 > 0 ? saved.TcvnBondEta1 : 2.5,
                    TcvnAreaRatio = saved.TcvnAreaRatio > 0 ? saved.TcvnAreaRatio : 1.0, TcvnAlpha = saved.TcvnAlpha > 0 ? saved.TcvnAlpha : 1.0,
                    DetailedBars = detailedBars,
                    DeletedReinforcementRanges = saved.DeletedReinforcementRanges ?? new List<string>()
                };
            }
            catch { return null; }
        }

        internal static void SaveSettings(Document doc, Options options)
        {
            string path = SettingsPath(doc);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var saved = new BeamRebarSettingsFile
            {
                DeletedReinforcementRanges = options.DeletedReinforcementRanges,
                MainTypeName = (options.TopType ?? options.MainType).Name,
                TopTypeName = (options.TopType ?? options.MainType).Name,
                BottomTypeName = (options.BottomType ?? options.MainType).Name,
                TopStartHookTypeName = options.TopStartHook?.Name,
                TopEndHookTypeName = options.TopEndHook?.Name,
                BottomStartHookTypeName = options.BottomStartHook?.Name,
                BottomEndHookTypeName = options.BottomEndHook?.Name,
                TieTypeName = options.TieType.Name,
                TopCount = options.TopCount,
                BottomCount = options.BottomCount,
                TopLayer2Count = options.TopLayer2Count,
                BottomLayer2Count = options.BottomLayer2Count,
                TopLayer3Count = options.TopLayer3Count,
                BottomLayer3Count = options.BottomLayer3Count,
                TopLayer2TypeName = options.TopLayer2Type?.Name ?? options.MainType.Name,
                BottomLayer2TypeName = options.BottomLayer2Type?.Name ?? options.MainType.Name,
                TopLayer3TypeName = options.TopLayer3Type?.Name ?? options.TopLayer2Type?.Name ?? options.MainType.Name,
                BottomLayer3TypeName = options.BottomLayer3Type?.Name ?? options.BottomLayer2Type?.Name ?? options.MainType.Name,
                SideBarCount = options.SideBarCount,
                SideBarTypeName = options.SideBarType?.Name ?? options.MainType.Name,
                LongitudinalLayerSpacingMm = options.LongitudinalLayerSpacingMm,
                CoverMm = options.CoverMm,
                TieSpacingMm = options.TieSpacingMm,
                UseZonedTies = options.UseZonedTies,
                TieEndZoneLengthMm = options.TieEndZoneLengthMm,
                TieEndSpacingMm = options.TieEndSpacingMm,
                TieMiddleSpacingMm = options.TieMiddleSpacingMm,
                StirrupSupportOffsetMm = options.StirrupSupportOffsetMm,
                SideBarMinimumHeightMm = options.SideBarMinimumHeightMm,
                SideBarSpacingMm = options.SideBarSpacingMm,
                SideBarAnchorageMm = options.SideBarAnchorageMm,
                TcvnCutoffEnabled = options.TcvnCutoffEnabled,
                TcvnConcreteRbtMpa = options.TcvnConcreteRbtMpa,
                TcvnSteelRsMpa = options.TcvnSteelRsMpa,
                TcvnBondEta1 = options.TcvnBondEta1,
                TcvnAreaRatio = options.TcvnAreaRatio,
                TcvnAlpha = options.TcvnAlpha,
                DetailedBars = (options.DetailedBars ?? new List<DetailedBarSpec>()).Select(bar => new BeamRebarDetailedBarSettings
                {
                    Key = bar.Key, TypeName = bar.Type?.Name, Count = bar.Count, BarIndex = bar.BarIndex, BarGroupCount = bar.BarGroupCount,
                    StartMm = bar.StartMm, EndMm = bar.EndMm, SectionPosition = bar.SectionPosition, LeftHookMm = bar.LeftHookMm, RightHookMm = bar.RightHookMm, HookDownMm = bar.HookDownMm,
                    StartPointIndex = bar.StartPointIndex, EndPointIndex = bar.EndPointIndex, LeftRatio = bar.LeftRatio, RightRatio = bar.RightRatio,
                    StartHookTypeName = bar.StartHook?.Name, EndHookTypeName = bar.EndHook?.Name
                }).ToList()
            };
            string temporary = path + ".tmp";
            using (var output = File.Create(temporary))
                new XmlSerializer(typeof(BeamRebarSettingsFile)).Serialize(output, saved);
            File.Copy(temporary, path, true);
            File.Delete(temporary);
        }

        private static string SettingsPath(Document doc)
        {
            string key = doc.ProjectInformation.UniqueId;
            using (var sha = SHA256.Create())
                key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KStruct", "BeamRebarSettings", key + ".xml");
        }

        private static System.Windows.Controls.ComboBox AddType(System.Windows.Controls.Panel root, string label, IList<BarChoice> choices, int selected)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 390, MinHeight = 24, DisplayMemberPath = "Display", ItemsSource = choices, SelectedIndex = selected, Margin = new Thickness(0, 0, 0, 5) };
            root.Children.Add(combo);
            return combo;
        }

        private static System.Windows.Controls.ComboBox AddHookType(System.Windows.Controls.Panel root, string label,
            IList<HookChoice> choices, RebarHookType selected)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 390, MinHeight = 24, DisplayMemberPath = "Display",
                ItemsSource = choices, SelectedIndex = Math.Max(0, choices.ToList().FindIndex(c => c.Type?.Id == selected?.Id)), Margin = new Thickness(0, 0, 0, 5) };
            root.Children.Add(combo);
            return combo;
        }

        private static RebarHookType ResolveHook(IList<RebarHookType> hookTypes, string name) =>
            string.IsNullOrWhiteSpace(name) ? null : hookTypes?.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));

        private static bool HasMissingHook(IList<RebarHookType> hookTypes, string name, RebarHookType resolved) =>
            !string.IsNullOrWhiteSpace(name) && (hookTypes == null || resolved == null);

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 2) });
            var box = new System.Windows.Controls.TextBox { MinWidth = 390, MinHeight = 22, Text = value };
            root.Children.Add(box);
            return box;
        }
    }
}
