using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Text.RegularExpressions;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class BeamRebarSettingsCommand : IExternalCommand
    {
        private sealed class BeamFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance beam && beam.Category != null &&
                beam.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            try
            {
                Document doc = ui.Document;
                var types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                    .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                var hookTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                    .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0)
                    throw new InvalidOperationException("Project chưa có RebarBarType.");

                var action = new TaskDialog("KStruct - Cài đặt thép dầm")
                {
                    MainInstruction = "Chọn thao tác cấu hình cốt thép dầm",
                MainContent = "Excel/CSV dùng các cột MainTypeName, TieTypeName, TopCount, BottomCount, CoverMm, TieSpacingMm; có thể thêm TopTypeName, BottomTypeName, TopStartHookTypeName, TopEndHookTypeName, BottomStartHookTypeName, BottomEndHookTypeName, TopLayer2Count, TopLayer2TypeName, BottomLayer2Count, BottomLayer2TypeName, TopLayer3Count, TopLayer3TypeName, BottomLayer3Count, BottomLayer3TypeName, SideBarCount, SideBarTypeName, LongitudinalLayerSpacingMm, UseZonedTies, TieEndZoneLengthMm, TieEndSpacingMm, TieMiddleSpacingMm và BeamMark/BeamId để áp dụng riêng từng dầm. Tên loại thép/móc phải trùng project; để trống tên móc nếu không dùng móc."
                };
                action.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Nhập Excel/CSV và áp dụng cho dầm đã chọn");
                action.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Nhập Excel/CSV, lưu làm cấu hình mặc định");
                action.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Chỉnh thông số thủ công");
                TaskDialogResult selectedAction = action.Show();
                if (selectedAction == TaskDialogResult.CommandLink1)
                {
                    var picker = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Chọn bảng cấu hình thép dầm",
                        Filter = "Excel/CSV (*.xlsx;*.csv;*.txt)|*.xlsx;*.csv;*.txt|Excel Workbook (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",
                        CheckFileExists = true,
                        Multiselect = false
                    };
                    if (picker.ShowDialog() != true) return Result.Cancelled;
                    List<BeamRebarSettingsRow> rows = BeamRebarSettingsImport.ReadRows(picker.FileName);
                    List<FamilyInstance> beams = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                        .Where(beam => new BeamFilter().AllowElement(beam)).Distinct().ToList();
                    if (beams.Count == 0)
                    {
                        beams = ui.Selection.PickObjects(ObjectType.Element, new BeamFilter(),
                                "Chọn dầm Structural Framing để áp dụng dữ liệu Excel, nhấn Finish khi xong")
                            .Select(reference => doc.GetElement(reference)).OfType<FamilyInstance>().Distinct().ToList();
                    }
                    if (beams.Count == 0) return Result.Cancelled;
                    ValidateUniqueRows(rows);
                    string report = ReinforceStraightBeamsCommand.ApplySettingsToBeams(doc, beams,
                        beam => ResolveOptionsForBeam(beam, rows, types, hookTypes));
                    TaskDialog.Show("KStruct - Cốt thép dầm từ Excel", report);
                    return Result.Succeeded;
                }
                if (selectedAction == TaskDialogResult.CommandLink2)
                {
                    var picker = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Chọn bảng cấu hình mặc định thép dầm",
                        Filter = "Excel/CSV (*.xlsx;*.csv;*.txt)|*.xlsx;*.csv;*.txt|Excel Workbook (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",
                        CheckFileExists = true,
                        Multiselect = false
                    };
                    if (picker.ShowDialog() != true) return Result.Cancelled;
                    BeamRebarSettingsFile imported = BeamRebarSettingsImport.Read(picker.FileName);
                    ReinforceStraightBeamsCommand.SaveSettings(doc, ResolveOptions(imported, types, hookTypes));
                    TaskDialog.Show("KStruct - Cài đặt thép dầm", "Đã nhập và lưu cấu hình mặc định cho project hiện tại.");
                    return Result.Succeeded;
                }
                if (selectedAction != TaskDialogResult.CommandLink3) return Result.Cancelled;

                var defaults = ReinforceStraightBeamsCommand.LoadSettings(doc, types, hookTypes);
                var options = ReinforceStraightBeamsCommand.ChooseOptions(
                    data.Application.MainWindowHandle, types, hookTypes, defaults, true);
                if (options == null) return Result.Cancelled;
                ReinforceStraightBeamsCommand.SaveSettings(doc, options);
                TaskDialog.Show("KStruct - Cài đặt thép dầm", "Đã lưu cấu hình cho project hiện tại.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cài đặt thép dầm", ex.Message);
                return Result.Failed;
            }
        }

        private static RebarBarType ResolveType(IList<RebarBarType> types, string requested)
        {
            if (string.IsNullOrWhiteSpace(requested)) return null;
            RebarBarType exact = types.FirstOrDefault(type => string.Equals(type.Name.Trim(), requested.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            Match diameterMatch = Regex.Match(requested, @"(?:D|Ø|phi|fi)\s*(\d+(?:[\.,]\d+)?)", RegexOptions.IgnoreCase);
            double diameter;
            if (!diameterMatch.Success ||
                !double.TryParse(diameterMatch.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out diameter)) return null;
            return types.OrderBy(type => Math.Abs(UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters) - diameter))
                .FirstOrDefault(type => Math.Abs(UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters) - diameter) < 0.05);
        }

        private static ReinforceStraightBeamsCommand.Options ResolveOptions(BeamRebarSettingsFile imported,
            IList<RebarBarType> types, IList<RebarHookType> hookTypes)
        {
            RebarBarType main = ResolveType(types, imported.MainTypeName);
            RebarBarType tie = ResolveType(types, imported.TieTypeName);
            RebarBarType top = string.IsNullOrWhiteSpace(imported.TopTypeName) ? main : ResolveType(types, imported.TopTypeName);
            RebarBarType bottom = string.IsNullOrWhiteSpace(imported.BottomTypeName) ? main : ResolveType(types, imported.BottomTypeName);
            RebarBarType topLayer = string.IsNullOrWhiteSpace(imported.TopLayer2TypeName) ? top : ResolveType(types, imported.TopLayer2TypeName);
            RebarBarType bottomLayer = string.IsNullOrWhiteSpace(imported.BottomLayer2TypeName) ? bottom : ResolveType(types, imported.BottomLayer2TypeName);
            RebarBarType topLayer3 = string.IsNullOrWhiteSpace(imported.TopLayer3TypeName) ? topLayer : ResolveType(types, imported.TopLayer3TypeName);
            RebarBarType bottomLayer3 = string.IsNullOrWhiteSpace(imported.BottomLayer3TypeName) ? bottomLayer : ResolveType(types, imported.BottomLayer3TypeName);
            RebarHookType topStartHook = ResolveHookType(hookTypes, imported.TopStartHookTypeName);
            RebarHookType topEndHook = ResolveHookType(hookTypes, imported.TopEndHookTypeName);
            RebarHookType bottomStartHook = ResolveHookType(hookTypes, imported.BottomStartHookTypeName);
            RebarHookType bottomEndHook = ResolveHookType(hookTypes, imported.BottomEndHookTypeName);
            if (topLayer == null && imported.TopLayer2Count == 0) topLayer = top;
            if (bottomLayer == null && imported.BottomLayer2Count == 0) bottomLayer = bottom;
            if (topLayer3 == null && imported.TopLayer3Count == 0) topLayer3 = topLayer;
            if (bottomLayer3 == null && imported.BottomLayer3Count == 0) bottomLayer3 = bottomLayer;
            if (main == null || tie == null || top == null || bottom == null || topLayer == null || bottomLayer == null || topLayer3 == null || bottomLayer3 == null)
                throw new InvalidOperationException("Không tìm thấy loại thép trong project: " +
                    (main == null ? imported.MainTypeName : "") + (main == null && tie == null ? "; " : "") +
                    (tie == null ? imported.TieTypeName : "") +
                    (top == null ? "; " + imported.TopTypeName : "") +
                    (bottom == null ? "; " + imported.BottomTypeName : "") +
                    (topLayer == null ? "; " + imported.TopLayer2TypeName : "") +
                    (bottomLayer == null ? "; " + imported.BottomLayer2TypeName : "") +
                    (topLayer3 == null ? "; " + imported.TopLayer3TypeName : "") +
                    (bottomLayer3 == null ? "; " + imported.BottomLayer3TypeName : ""));
            return new ReinforceStraightBeamsCommand.Options
            {
                MainType = top,
                TopType = top,
                BottomType = bottom,
                TieType = tie,
                TopCount = imported.TopCount,
                BottomCount = imported.BottomCount,
                TopLayer2Count = imported.TopLayer2Count,
                BottomLayer2Count = imported.BottomLayer2Count,
                TopLayer3Count = imported.TopLayer3Count,
                BottomLayer3Count = imported.BottomLayer3Count,
                TopLayer2Type = topLayer,
                BottomLayer2Type = bottomLayer,
                TopLayer3Type = topLayer3,
                BottomLayer3Type = bottomLayer3,
                SideBarCount = imported.SideBarCount,
                SideBarType = string.IsNullOrWhiteSpace(imported.SideBarTypeName) ? top : ResolveType(types, imported.SideBarTypeName),
                TopStartHook = topStartHook,
                TopEndHook = topEndHook,
                BottomStartHook = bottomStartHook,
                BottomEndHook = bottomEndHook,
                LongitudinalLayerSpacingMm = imported.LongitudinalLayerSpacingMm,
                CoverMm = imported.CoverMm,
                TieSpacingMm = imported.TieSpacingMm,
                UseZonedTies = imported.UseZonedTies,
                TieEndZoneLengthMm = imported.TieEndZoneLengthMm,
                TieEndSpacingMm = imported.TieEndSpacingMm,
                TieMiddleSpacingMm = imported.TieMiddleSpacingMm
            };
        }

        private static ReinforceStraightBeamsCommand.Options ResolveOptionsForBeam(FamilyInstance beam,
            IList<BeamRebarSettingsRow> rows, IList<RebarBarType> types, IList<RebarHookType> hookTypes)
        {
            string mark = beam.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()?.Trim();
            string id = beam.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            BeamRebarSettingsRow matched = rows.FirstOrDefault(row =>
                !string.IsNullOrWhiteSpace(row.BeamId) && string.Equals(row.BeamId.Trim(), id, StringComparison.OrdinalIgnoreCase)) ??
                rows.FirstOrDefault(row => !string.IsNullOrWhiteSpace(row.BeamMark) &&
                    !string.IsNullOrWhiteSpace(mark) && string.Equals(row.BeamMark.Trim(), mark, StringComparison.OrdinalIgnoreCase)) ??
                rows.FirstOrDefault(row => string.IsNullOrWhiteSpace(row.BeamId) && string.IsNullOrWhiteSpace(row.BeamMark));
            return matched == null ? null : ResolveOptions(matched.Settings, types, hookTypes);
        }

        private static RebarHookType ResolveHookType(IList<RebarHookType> types, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            RebarHookType hook = types.FirstOrDefault(type => string.Equals(type.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (hook == null) throw new InvalidOperationException("Không tìm thấy kiểu móc neo trong project: " + name);
            return hook;
        }

        private static void ValidateUniqueRows(IList<BeamRebarSettingsRow> rows)
        {
            var duplicateId = rows.Where(row => !string.IsNullOrWhiteSpace(row.BeamId)).GroupBy(row => row.BeamId.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            var duplicateMark = rows.Where(row => !string.IsNullOrWhiteSpace(row.BeamMark)).GroupBy(row => row.BeamMark.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateId != null) throw new InvalidOperationException("Excel có BeamId trùng: " + duplicateId.Key);
            if (duplicateMark != null) throw new InvalidOperationException("Excel có BeamMark trùng: " + duplicateMark.Key);
        }
    }

    public sealed class BeamRebarSettingsFile
    {
        public string MainTypeName { get; set; }
        public List<string> DeletedReinforcementRanges { get; set; } = new List<string>();
        public string TopTypeName { get; set; }
        public string BottomTypeName { get; set; }
        public string TopStartHookTypeName { get; set; }
        public string TopEndHookTypeName { get; set; }
        public string BottomStartHookTypeName { get; set; }
        public string BottomEndHookTypeName { get; set; }
        public string TieTypeName { get; set; }
        public int TopCount { get; set; }
        public int BottomCount { get; set; }
        public int TopLayer2Count { get; set; }
        public int BottomLayer2Count { get; set; }
        public int TopLayer3Count { get; set; }
        public int BottomLayer3Count { get; set; }
        public int SideBarCount { get; set; }
        public string TopLayer2TypeName { get; set; }
        public string BottomLayer2TypeName { get; set; }
        public string TopLayer3TypeName { get; set; }
        public string BottomLayer3TypeName { get; set; }
        public string SideBarTypeName { get; set; }
        public double LongitudinalLayerSpacingMm { get; set; }
        public double CoverMm { get; set; }
        public double TieSpacingMm { get; set; }
        public bool UseZonedTies { get; set; }
        public double TieEndZoneLengthMm { get; set; }
        public double TieEndSpacingMm { get; set; }
        public double TieMiddleSpacingMm { get; set; }
        public double StirrupSupportOffsetMm { get; set; }
        public double SideBarMinimumHeightMm { get; set; }
        public double SideBarSpacingMm { get; set; }
        public double SideBarAnchorageMm { get; set; }
        public bool TcvnCutoffEnabled { get; set; } = true;
        public double TcvnConcreteRbtMpa { get; set; }
        public double TcvnSteelRsMpa { get; set; }
        public double TcvnBondEta1 { get; set; } = 2.5;
        public double TcvnAreaRatio { get; set; } = 1.0;
        public double TcvnAlpha { get; set; } = 1.0;
        public List<BeamRebarDetailedBarSettings> DetailedBars { get; set; } = new List<BeamRebarDetailedBarSettings>();
    }

    public sealed class BeamRebarDetailedBarSettings
    {
        public string Key { get; set; }
        public string TypeName { get; set; }
        public int Count { get; set; }
        public int BarIndex { get; set; } = -1;
        public int BarGroupCount { get; set; }
        public double StartMm { get; set; }
        public double EndMm { get; set; }
        public double SectionPosition { get; set; } = 1;
        public string StartHookTypeName { get; set; }
        public string EndHookTypeName { get; set; }
        public double LeftHookMm { get; set; }
        public double RightHookMm { get; set; }
        public double HookDownMm { get; set; }
        public int StartPointIndex { get; set; }
        public int EndPointIndex { get; set; } = -1;
        public double LeftRatio { get; set; } = 0.25;
        public double RightRatio { get; set; } = 0.25;
    }
}
