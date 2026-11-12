using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Collections.Generic;

namespace SDK_HD.KStruct
{
    public class App : IExternalApplication
    {

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                const string tab = "KStruct";
                if (!app.GetRibbonPanels().Any(p => p.Name == tab))
                {
                    try { app.CreateRibbonTab(tab); } catch (Autodesk.Revit.Exceptions.ArgumentException) { }
                }
                var panel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "Vẽ kết cấu nhanh")
                    ?? app.CreateRibbonPanel(tab, "Vẽ kết cấu nhanh");
                string dll = Assembly.GetExecutingAssembly().Location;
                Add(panel, dll, "KStruct_Column", "Vẽ\nCột", typeof(DrawColumnsCommand), "Column");
                Add(panel, dll, "KStruct_Beam", "Vẽ\nDầm", typeof(DrawBeamsCommand), "Beam");
                Add(panel, dll, "KStruct_Floor", "Vẽ\nSàn", typeof(DrawFloorCommand), "Floor");
                Add(panel, dll, "KStruct_Footing", "Móng\ndưới cột", typeof(DrawFootingsCommand), "Footing");
                Add(panel, dll, "KStruct_PlaceSpreadFootings", "Rải móng đơn\nhàng loạt", typeof(global::SDK_HD.KStruct.NativeDocumentation.PlaceSpreadFootingsCommand), "Footing");
                Add(panel, dll, "KStruct_CreateStripFootings", "Tạo móng băng\ntheo tường", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateStripFootingsCommand), "Footing");
                Add(panel, dll, "KStruct_Settings", "Thông tin\nKStruct", typeof(SettingsCommand), "Settings");
                var columnPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "Thép cột")
                    ?? app.CreateRibbonPanel(tab, "Thép cột");
                var columnRebarSplit = columnPanel.AddItem(new SplitButtonData("KStruct_ColumnRebarSplit", "Tạo thép cột")) as SplitButton;
                Add(columnRebarSplit, dll, "KStruct_ColumnRebarV3", "Tạo thép cột V4", typeof(global::ColumnRebarPro.V3.ColumnRebarV3Command), "RebarColumnV3");
                Add(columnRebarSplit, dll, "KStruct_RoundColumnRebar", "Thép cột tròn", typeof(global::SDK_HD.KStruct.NativeRebar.RoundColumnRebarCommand), "RebarColumnV3");
                Add(columnRebarSplit, dll, "KStruct_CreateColumnDrawingsNative", "Tạo bộ bản vẽ thép cột", typeof(global::ColumnRebarPro.V3.CreateColumnDrawingsCommand), "RebarColumnV3");
                Add(columnRebarSplit, dll, "KStruct_UpdateColumnDrawings", "Cập nhật bản vẽ cột", typeof(global::ColumnRebarPro.V3.UpdateColumnDrawingsCommand), "RebarColumnV3");
                Add(columnRebarSplit, dll, "KStruct_StructuralPlanDimensionNative", "Dim cột/dầm/móng theo lưới", typeof(global::ColumnRebarPro.V3.V3StructuralPlanDimensionCommand), "RebarColumnV3");
                Add(columnRebarSplit, dll, "KStruct_ColumnRebarSchedule", "Thống kê cốt thép", typeof(global::SDK_HD.KStruct.NativeRebar.RebarScheduleCommand), "RebarColumnV3");
                var footingRebarPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "Thép móng")
                    ?? app.CreateRibbonPanel(tab, "Thép móng");
                // One visible drop-down with all seven existing footing commands.
                // Keep command IDs and handlers unchanged to preserve user workflows.
                var footingDropdown = footingRebarPanel.AddItem(
                    new PulldownButtonData("KStruct_FootingDropdown", "Thép móng")) as PulldownButton;
                if (footingDropdown == null)
                    throw new InvalidOperationException("Không tạo được nút xổ xuống Thép móng KStruct.");
                footingDropdown.Image = FlatRibbonIcons.Create("KStruct_FootingDropdown", "Footing", 16);
                footingDropdown.LargeImage = FlatRibbonIcons.Create("KStruct_FootingDropdown", "Footing", 32);
                footingDropdown.ToolTip = "KStruct - Vẽ thép móng và quản lý hồ sơ móng";
                Add(footingDropdown, dll, "KStruct_FootingRebar", "Thép móng cọc & đơn", typeof(global::SDK_HD.KStruct.FootingRebar.FootingRebarCommand), "Footing");
                Add(footingDropdown, dll, "KStruct_UpdateFootingRebar", "Cập nhật thép móng", typeof(global::SDK_HD.KStruct.FootingRebar.UpdateFootingRebarCommand), "Footing");
                Add(footingDropdown, dll, "KStruct_CreateStripFootingDrawings", "Lập bản vẽ móng băng", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateStripFootingDrawingsCommand), "Settings");
                Add(footingDropdown, dll, "KStruct_UpdateStripFootingDrawings", "Cập nhật bản vẽ móng băng", typeof(global::SDK_HD.KStruct.NativeDocumentation.UpdateStripFootingDrawingsCommand), "Settings");
                Add(footingDropdown, dll, "KStruct_CreateIsolatedFootingSheets", "Tạo hồ sơ móng cọc, đơn", typeof(global::SDK_HD.KStruct.NativeDocumentation.FootingDrawingCommand), "Footing");
                Add(footingDropdown, dll, "KStruct_UpdateIsolatedFootingSheets", "Cập nhật hồ sơ móng cọc, đơn", typeof(global::SDK_HD.KStruct.NativeDocumentation.UpdateFootingDrawingsCommand), "Settings");
                Add(footingDropdown, dll, "KStruct_DeleteFoundationDrawingViews", "Xóa hồ sơ móng đã tạo", typeof(global::SDK_HD.KStruct.NativeDocumentation.DeleteFootingDrawingsCommand), "Settings");
                var beamPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "KStruct Dầm")
                    ?? app.CreateRibbonPanel(tab, "KStruct Dầm");
                var beamSplit = beamPanel.AddItem(new SplitButtonData("KStruct_BeamRebarSplit", "Thép dầm")) as SplitButton;
                Add(beamSplit, dll, "KStruct_BeamRebar", "Thép dầm", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceStraightBeamsCommand), "Beam");
                Add(beamSplit, dll, "KStruct_CurvedBeamRebar", "Thép dầm cong", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceHorizontalCurvedBeamsCommand), "Beam");
                Add(beamSplit, dll, "KStruct_LintelRebar", "Thép lanh tô", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceLintelsCommand), "Beam");
                Add(beamSplit, dll, "KStruct_BeamRebarSetting", "Cài đặt vẽ thép dầm", typeof(global::SDK_HD.KStruct.NativeRebar.BeamRebarSettingsCommand), "Settings");
                Add(beamSplit, dll, "KStruct_BeamDrawing", "Tạo bản vẽ dầm", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateBeamDetailSheetsCommand), "Beam");
                Add(beamSplit, dll, "KStruct_BeamSectionDimension", "Dim mặt cắt dầm", typeof(global::SDK_HD.KStruct.NativeDocumentation.DimensionBeamSectionCommand), "Settings");
                Add(beamSplit, dll, "KStruct_BeamDetailDimension", "Dim chi tiết dầm", typeof(global::SDK_HD.KStruct.NativeDocumentation.DimensionBeamDetailCommand), "Settings");
                Add(beamSplit, dll, "KStruct_BeamCreateSectionsViews", "Tạo mặt cắt dầm", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateBeamSectionsCommand), "Settings");
                Add(beamPanel, dll, "KStruct_BeamPlanDimension", "Dim mặt bằng dầm", typeof(global::ColumnRebarPro.V3.V3StructuralPlanDimensionCommand), "Beam");
                var nativeRebarPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "KStruct Cốt thép")
                    ?? app.CreateRibbonPanel(tab, "KStruct Cốt thép");
                Add(nativeRebarPanel, dll, "KStruct_RebarSchedule", "Thống kê cốt thép", typeof(global::SDK_HD.KStruct.NativeRebar.RebarScheduleCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ExportRebarCutList", "Xuất phiếu cắt thép CSV", typeof(global::SDK_HD.KStruct.NativeRebar.ExportRebarCutListCommand), "Settings");
                Add(nativeRebarPanel, dll, "KStruct_OptimizeRebarCutting", "Tối ưu cắt thép cây", typeof(global::SDK_HD.KStruct.NativeRebar.OptimizeRebarCuttingCommand), "Settings");
                Add(nativeRebarPanel, dll, "KStruct_PreviewBeamRebarSplices", "Preview nối thép dầm", typeof(global::SDK_HD.KStruct.NativeRebar.PreviewBeamRebarSplicesCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_CheckCurvedBeamRebar", "Kiểm tra thép\ndầm cong", typeof(global::SDK_HD.KStruct.NativeRebar.CheckCurvedBeamRebarCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_DrawRebarShape2D", "Vẽ hình thép 2D", typeof(global::SDK_HD.KStruct.NativeRebar.DrawRebarShape2DCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_RebarShape2DSettings", "Cài đặt hình thép 2D", typeof(global::SDK_HD.KStruct.NativeRebar.RebarShape2DSettingsCommand), "Settings");
                Add(nativeRebarPanel, dll, "KStruct_RebarDiameterSettings", "Cấu hình đường kính / uốn thép", typeof(global::SDK_HD.KStruct.NativeRebar.RebarDiameterSettingsCommand), "Settings");
                Add(nativeRebarPanel, dll, "KStruct_RebarShapeCatalog", "Thư viện hình thép", typeof(global::SDK_HD.KStruct.NativeRebar.RebarShapeCatalogCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_RebarShapeImageManager", "Quản lý ảnh hình thép", typeof(global::SDK_HD.KStruct.NativeRebar.RebarShapeImageManagerCommand), "Settings");
                Add(nativeRebarPanel, dll, "KStruct_ShowRebarGroupName", "Xem tên nhóm thép", typeof(global::SDK_HD.KStruct.NativeRebar.ShowRebarGroupNameCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_GroupSelectedRebar", "Nhóm Rebar", typeof(global::SDK_HD.KStruct.NativeRebar.GroupSelectedRebarCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_SelectSlabRebarByLocation", "Chọn thép sàn theo vị trí", typeof(global::SDK_HD.KStruct.NativeRebar.SelectSlabRebarByLocationCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_SelectRebarByMark", "Chọn thép theo mã", typeof(global::SDK_HD.KStruct.NativeRebar.SelectRebarByMarkCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_SelectRebarByNumber", "Chọn thép theo Rebar Number", typeof(global::SDK_HD.KStruct.NativeRebar.SelectRebarByNumberCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_DeleteSelectedRebar", "Xóa thép đã chọn", typeof(global::SDK_HD.KStruct.NativeRebar.DeleteSelectedRebarCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_AssignRebarMark", "Gán mã thống kê", typeof(global::SDK_HD.KStruct.NativeRebar.AssignRebarMarkCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ManageRebarNumbers", "Quản lý mã thép theo cấu kiện", typeof(global::SDK_HD.KStruct.NativeRebar.ManageRebarNumbersCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_CountHostMembersOnRebar", "Ghi số cấu kiện lên Rebar", typeof(global::SDK_HD.KStruct.NativeRebar.CountHostMembersOnRebarCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_CopySelectedRebar", "Sao chép thép", typeof(global::SDK_HD.KStruct.NativeRebar.CopySelectedRebarCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ToggleRebarVisibility", "Đổi hiển thị thép", typeof(global::SDK_HD.KStruct.NativeRebar.ToggleRebarVisibilityCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ColorSelectedRebar", "Tô màu Rebar", typeof(global::SDK_HD.KStruct.NativeRebar.ColorSelectedRebarCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_MatchRebarProperties", "Khớp thuộc tính Rebar", typeof(global::SDK_HD.KStruct.NativeRebar.MatchRebarPropertiesCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_EditRebarSetInclusion", "Loại trừ/khôi phục\nvị trí Rebar", typeof(global::SDK_HD.KStruct.NativeRebar.EditRebarSetInclusionCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_SelectRebarByDiameter", "Chọn thép cùng đường kính", typeof(global::SDK_HD.KStruct.NativeRebar.SelectRebarByDiameterCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_FilterRebar", "Lọc / cô lập Rebar", typeof(global::SDK_HD.KStruct.NativeRebar.FilterRebarCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_HideRebarByHost", "Ẩn Rebar theo host", typeof(global::SDK_HD.KStruct.NativeRebar.HideRebarByHostCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_AssignRebarPartition", "Gán Partition", typeof(global::SDK_HD.KStruct.NativeRebar.AssignRebarPartitionCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ChangeRebarType", "Đổi loại thép", typeof(global::SDK_HD.KStruct.NativeRebar.ChangeRebarTypeCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_EditRebarParameters", "Sửa tham số Rebar", typeof(global::SDK_HD.KStruct.NativeRebar.EditRebarParametersCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_BendStraightRebar", "Bẻ Rebar\ntheo đoạn thẳng", typeof(global::SDK_HD.KStruct.NativeRebar.BendStraightRebarCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_CountRebar", "Thống kê Rebar", typeof(global::SDK_HD.KStruct.NativeRebar.CountRebarCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_TagSelectedRebar", "Gắn tag cốt thép", typeof(global::SDK_HD.KStruct.NativeRebar.TagSelectedRebarCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_DistributedRebarTag", "Dàn đều tag thép", typeof(global::SDK_HD.KStruct.NativeRebar.DistributedRebarTagCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_TagSlabRebarSets", "Tag bộ thép sàn", typeof(global::SDK_HD.KStruct.NativeRebar.TagSlabRebarSetsCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_RebarSegmentLength", "Đo chiều dài từng đoạn", typeof(global::SDK_HD.KStruct.NativeRebar.RebarSegmentLengthCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ShowRebarRoundedLength", "Xem chiều dài thép đã làm tròn", typeof(global::SDK_HD.KStruct.NativeRebar.ShowRebarRoundedLengthCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_SetRebarHook", "Đổi móc thép", typeof(global::SDK_HD.KStruct.NativeRebar.SetRebarHookCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_PlaceRebarEndSymbol", "Ký hiệu đầu thép", typeof(global::SDK_HD.KStruct.NativeRebar.PlaceRebarEndSymbolCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ReinforceFloorOpening", "Gia cường lỗ mở sàn", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceFloorOpeningCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ReinforceIsolatedFootings", "Thép móng / đài cọc", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceIsolatedFootingsCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ReinforceStripFootings", "Cốt thép móng băng", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceStripFootingsCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_SplitStraightRebar", "Chia thanh thẳng", typeof(global::SDK_HD.KStruct.NativeRebar.SplitStraightRebarCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ExtendStraightRebar", "Kéo dài thanh thẳng", typeof(global::SDK_HD.KStruct.NativeRebar.ExtendStraightRebarCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ExplodeRebarSet", "Tách Rebar set", typeof(global::SDK_HD.KStruct.NativeRebar.ExplodeRebarSetCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ReinforceRectangularSlab", "Cốt thép sàn 2 phương", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceRectangularSlabCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ReinforceSlabRegion", "Thép sàn theo vùng", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceSlabRegionCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_MarkSlabRebarAdditional", "Đánh dấu thép tăng cường", typeof(global::SDK_HD.KStruct.NativeRebar.MarkSlabRebarAdditionalCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ReinforceBubbleSlabs", "Thép sàn Bubble", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceBubbleSlabsCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_PlaceSlabChairBars", "Thép chân chó sàn", typeof(global::SDK_HD.KStruct.NativeRebar.PlaceSlabChairBarsCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_PlaceSlabModules", "Bố trí module sàn", typeof(global::SDK_HD.KStruct.NativeRebar.PlaceSlabModulesCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_LoadUBootSlabFamilies", "Nạp family U-Boot\nKStruct", typeof(global::SDK_HD.KStruct.NativeRebar.LoadUBootSlabFamiliesCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ReinforceUBootSlabs", "Thép sàn U-Boot", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceUBootSlabsCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_DeleteManagedSlabModules", "Xóa module sàn\nKStruct đã tạo", typeof(global::SDK_HD.KStruct.NativeRebar.DeleteManagedSlabModulesCommand), "Settings");
                Add(nativeRebarPanel, dll, "KStruct_ReinforceMushroomDropPanels", "Thép drop panel\nMushroom", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceMushroomDropPanelsCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ReinforceStraightWall", "Cốt thép tường thẳng", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceStraightWallCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ReinforceWallPilasters", "Thép bổ trụ tường", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceWallPilastersCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ReinforceStraightStair", "Cốt thép cầu thang\n(vế & chiếu nghỉ)", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceStraightStairCommand), "RebarColumnV3");
                Add(nativeRebarPanel, dll, "KStruct_ReinforceRetainingWall", "Cốt thép tường chắn", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceRetainingWallCommand), "RebarColumnV3");
                var nativeDocumentationPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "KStruct Hồ sơ")
                    ?? app.CreateRibbonPanel(tab, "KStruct Hồ sơ");
                Add(nativeDocumentationPanel, dll, "KStruct_NumberSelectedElements", "Đánh mã cấu kiện", typeof(global::SDK_HD.KStruct.NativeDocumentation.NumberSelectedElementsCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_TagElementAtPickedPoint", "Gắn tag cấu kiện", typeof(global::SDK_HD.KStruct.NativeDocumentation.TagElementAtPickedPointCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_AuditModel", "Kiểm tra\nmô hình", typeof(global::SDK_HD.KStruct.NativeDocumentation.AuditModelCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_AuditModelV2", "Audit V2\nRebar & Geometry", typeof(global::SDK_HD.KStruct.NativeDocumentation.AuditModelV2Command), "Settings");
                var nativeSettingsPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "KStruct Thiết lập")
                    ?? app.CreateRibbonPanel(tab, "KStruct Thiết lập");
                Add(nativeSettingsPanel, dll, "KStruct_RibbonPanelManager", "Quản lý panel", typeof(global::SDK_HD.KStruct.NativeDocumentation.RibbonPanelManagerCommand), "Settings");
                Add(nativeSettingsPanel, dll, "KStruct_CreateSharedParameter", "Tạo Shared\nParameter", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateSharedParameterCommand), "Settings");
                return Result.Succeeded;
            }
            catch (Exception ex) { TaskDialog.Show("KStruct - Startup", ex.ToString()); return Result.Failed; }
        }
        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
        private static void Add(RibbonPanel panel, string dll, string id, string title, Type type, string icon)
        {
            var data = new PushButtonData(id, title, dll, type.FullName);
            data.ToolTip = "KStruct - " + title.Replace('\n', ' ');
            data.Image = FlatRibbonIcons.Create(id, icon, 16);
            data.LargeImage = FlatRibbonIcons.Create(id, icon, 32);
            panel.AddItem(data);
        }
        private static void Add(SplitButton split, string dll, string id, string title, Type type, string icon)
        {
            if (split == null) throw new InvalidOperationException("Không tạo được nút nhóm thép dầm KStruct.");
            var data = new PushButtonData(id, title, dll, type.FullName);
            data.ToolTip = "KStruct - " + title.Replace('\n', ' ');
            data.Image = FlatRibbonIcons.Create(id, icon, 16);
            data.LargeImage = FlatRibbonIcons.Create(id, icon, 32);
            split.AddPushButton(data);
        }
        private static void Add(PulldownButton dropdown, string dll, string id, string title, Type type, string icon)
        {
            if (dropdown == null) throw new InvalidOperationException("Không tạo được nhóm nút xổ xuống KStruct.");
            var data = new PushButtonData(id, title, dll, type.FullName);
            data.ToolTip = "KStruct - " + title.Replace('\n', ' ');
            data.Image = FlatRibbonIcons.Create(id, icon, 16);
            data.LargeImage = FlatRibbonIcons.Create(id, icon, 32);
            dropdown.AddPushButton(data);
        }

    }

    internal static class Helpers
    {
        internal static Level Level(Document doc, View view) =>
            (view.GenLevel != null ? view.GenLevel : new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(x => x.Elevation).FirstOrDefault());
        internal static FamilySymbol Symbol(Document doc, BuiltInCategory category) =>
            new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(category).Cast<FamilySymbol>().FirstOrDefault();
        internal static Result Error(Exception ex, ref string message)
        {
            if (ex is Autodesk.Revit.Exceptions.OperationCanceledException) return Result.Cancelled;
            message = ex.ToString(); TaskDialog.Show("KStruct - Error", ex.Message); return Result.Failed;
        }
        internal static XYZ AtLevel(XYZ p, Level l) => new XYZ(p.X, p.Y, l.Elevation);
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawColumnsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var ui = data.Application.ActiveUIDocument;
                if (ui == null) { TaskDialog.Show("KStruct", "Hãy mở một project trước."); return Result.Cancelled; }
                var doc = ui.Document;
                var level = Helpers.Level(doc, doc.ActiveView);
                var symbol = Helpers.Symbol(doc, BuiltInCategory.OST_StructuralColumns);
                if (level == null || symbol == null) { TaskDialog.Show("KStruct", "Cần có Level và Family Structural Column đã Load."); return Result.Cancelled; }
                XYZ point = ui.Selection.PickPoint("Chọn vị trí đặt cột");
                using (var tx = new Transaction(doc, "KStruct - Vẽ cột"))
                {
                    tx.Start(); if (!symbol.IsActive) symbol.Activate();
                    doc.Create.NewFamilyInstance(Helpers.AtLevel(point, level), symbol, level, StructuralType.Column);
                    tx.Commit();
                }
                return Result.Succeeded;
            }
            catch (Exception ex) { return Helpers.Error(ex, ref message); }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawBeamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var ui = data.Application.ActiveUIDocument;
                if (ui == null) return Result.Cancelled;
                var doc = ui.Document; var level = Helpers.Level(doc, doc.ActiveView);
                var symbol = Helpers.Symbol(doc, BuiltInCategory.OST_StructuralFraming);
                if (level == null || symbol == null) { TaskDialog.Show("KStruct", "Cần có Level và Family Structural Framing đã Load."); return Result.Cancelled; }
                var a = ui.Selection.PickPoint("Chọn điểm đầu dầm");
                var b = ui.Selection.PickPoint("Chọn điểm cuối dầm");
                if (a.DistanceTo(b) < 0.001) { TaskDialog.Show("KStruct", "Hai điểm không được trùng nhau."); return Result.Cancelled; }
                using (var tx = new Transaction(doc, "KStruct - Vẽ dầm"))
                {
                    tx.Start(); if (!symbol.IsActive) symbol.Activate();
                    doc.Create.NewFamilyInstance(Line.CreateBound(Helpers.AtLevel(a, level), Helpers.AtLevel(b, level)), symbol, level, StructuralType.Beam);
                    tx.Commit();
                }
                return Result.Succeeded;
            }
            catch (Exception ex) { return Helpers.Error(ex, ref message); }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawFloorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var ui = data.Application.ActiveUIDocument;
                if (ui == null) return Result.Cancelled;
                var doc = ui.Document; var level = Helpers.Level(doc, doc.ActiveView);
                var floorType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault();
                if (level == null || floorType == null) { TaskDialog.Show("KStruct", "Cần có Level và Floor Type."); return Result.Cancelled; }
                var a = ui.Selection.PickPoint("Chọn góc thứ nhất của sàn");
                var b = ui.Selection.PickPoint("Chọn góc đối diện của sàn");
                if (Math.Abs(a.X - b.X) < 0.001 || Math.Abs(a.Y - b.Y) < 0.001)
                { TaskDialog.Show("KStruct", "Hình chữ nhật phải có chiều dài và chiều rộng khác 0."); return Result.Cancelled; }
                double z = level.Elevation;
                var p = new[] { new XYZ(a.X, a.Y, z), new XYZ(b.X, a.Y, z), new XYZ(b.X, b.Y, z), new XYZ(a.X, b.Y, z) };
                var loop = new CurveLoop();
                for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(p[i], p[(i + 1) % 4]));
                using (var tx = new Transaction(doc, "KStruct - Vẽ sàn"))
                {
                    tx.Start();
                    Floor.Create(doc, new List<CurveLoop> { loop }, floorType.Id, level.Id);
                    tx.Commit();
                }
                return Result.Succeeded;
            }
            catch (Exception ex) { return Helpers.Error(ex, ref message); }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawFootingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var ui = data.Application.ActiveUIDocument;
                if (ui == null) return Result.Cancelled;
                var doc = ui.Document;
                var symbol = Helpers.Symbol(doc, BuiltInCategory.OST_StructuralFoundation);
                if (symbol == null) { TaskDialog.Show("KStruct", "Cần Load Family móng đơn (Structural Foundations)."); return Result.Cancelled; }
                var reference = ui.Selection.PickObject(ObjectType.Element, "Chọn cột kết cấu để đặt móng");
                var column = doc.GetElement(reference);
                if (column.Category == null || column.Category.Id.Value != (long)BuiltInCategory.OST_StructuralColumns)
                { TaskDialog.Show("KStruct", "Phần tử được chọn không phải Structural Column."); return Result.Cancelled; }
                var location = column.Location as LocationPoint;
                if (location == null) { TaskDialog.Show("KStruct", "Chỉ hỗ trợ cột dạng điểm."); return Result.Cancelled; }
                var level = Helpers.Level(doc, doc.ActiveView);
                if (level == null) return Result.Cancelled;
                using (var tx = new Transaction(doc, "KStruct - Đặt móng"))
                {
                    tx.Start(); if (!symbol.IsActive) symbol.Activate();
                    doc.Create.NewFamilyInstance(Helpers.AtLevel(location.Point, level), symbol, level, StructuralType.Footing);
                    tx.Commit();
                }
                return Result.Succeeded;
            }
            catch (Exception ex) { return Helpers.Error(ex, ref message); }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            TaskDialog.Show("KStruct", "Bản kiểm tra hoạt động Revit 2024.\nCác lệnh hiện chọn Family Type đầu tiên có sẵn trong project.\nGiao diện WPF chọn Type/Level/Offset sẽ được phát triển riêng.");
            return Result.Succeeded;
        }
    }
}






