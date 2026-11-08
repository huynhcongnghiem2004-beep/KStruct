using System;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
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
                Add(panel, dll, "KStruct_CreateRetainingWallSheets", "Lập hồ sơ\ntường chắn", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateRetainingWallDrawingSheetsCommand), "Settings");
                Add(panel, dll, "KStruct_Settings", "Thông tin\nKStruct", typeof(SettingsCommand), "Settings");
                var columnPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "Thép cột")
                    ?? app.CreateRibbonPanel(tab, "Thép cột");
                var columnRebarSplit = columnPanel.AddItem(new SplitButtonData("KStruct_ColumnRebarSplit", "Tạo thép cột")) as SplitButton;
                Add(columnRebarSplit, dll, "KStruct_ColumnRebarV3", "Thép cột & triển khai V3", typeof(global::ColumnRebarPro.V3.ColumnRebarV3Command), "RebarColumnV3");
                Add(columnRebarSplit, dll, "KStruct_RoundColumnRebar", "Thép cột tròn", typeof(global::SDK_HD.KStruct.NativeRebar.RoundColumnRebarCommand), "RebarColumnV3");
                Add(columnRebarSplit, dll, "KStruct_CreateColumnDrawingsNative", "Tạo bộ bản vẽ thép cột", typeof(global::ColumnRebarPro.V3.CreateColumnDrawingsCommand), "RebarColumnV3");
                Add(columnRebarSplit, dll, "KStruct_UpdateColumnDrawings", "Cập nhật bản vẽ V3", typeof(global::ColumnRebarPro.V3.UpdateColumnDrawingsCommand), "RebarColumnV3");
                Add(columnRebarSplit, dll, "KStruct_RefreshColumnDrawingsNative", "Làm mới bản vẽ cột", typeof(global::ColumnRebarPro.V3.RefreshColumnDrawingsCommand), "RebarColumnV3");
                Add(columnRebarSplit, dll, "KStruct_StructuralPlanDimensionNative", "Dim cột/dầm/móng theo lưới", typeof(global::ColumnRebarPro.V3.V3StructuralPlanDimensionCommand), "RebarColumnV3");
                Add(columnRebarSplit, dll, "KStruct_ColumnRebarSchedule", "Thống kê cốt thép", typeof(global::SDK_HD.KStruct.NativeRebar.RebarScheduleCommand), "RebarColumnV3");
                var footingRebarPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "Thép móng")
                    ?? app.CreateRibbonPanel(tab, "Thép móng");
                Add(footingRebarPanel, dll, "KStruct_FootingRebar", "Thép móng\ncọc & đơn", typeof(global::SDK_HD.KStruct.FootingRebar.FootingRebarCommand), "Footing");
                Add(footingRebarPanel, dll, "KStruct_UpdateFootingRebar", "Cập nhật\nthép móng", typeof(global::SDK_HD.KStruct.FootingRebar.UpdateFootingRebarCommand), "Footing");
                Add(footingRebarPanel, dll, "KStruct_CreateStripFootingDrawings", "Lập bản vẽ\nmóng băng", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateStripFootingDrawingsCommand), "Settings");
                Add(footingRebarPanel, dll, "KStruct_UpdateStripFootingDrawings", "Cập nhật bản vẽ\nmóng băng", typeof(global::SDK_HD.KStruct.NativeDocumentation.UpdateStripFootingDrawingsCommand), "Settings");
                Add(footingRebarPanel, dll, "KStruct_CreateIsolatedFootingSheets", "Tạo hồ sơ\nmóng cọc, đơn", typeof(global::SDK_HD.KStruct.NativeDocumentation.FootingDrawingCommand), "Footing");
                Add(footingRebarPanel, dll, "KStruct_UpdateIsolatedFootingSheets", "Cập nhật hồ sơ\nmóng cọc, đơn", typeof(global::SDK_HD.KStruct.NativeDocumentation.UpdateFootingDrawingsCommand), "Settings");
                Add(footingRebarPanel, dll, "KStruct_DeleteFoundationDrawingViews", "Xóa hồ sơ móng\nđã tạo", typeof(global::SDK_HD.KStruct.NativeDocumentation.DeleteFootingDrawingsCommand), "Settings");
                var beamPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "KStruct Dầm")
                    ?? app.CreateRibbonPanel(tab, "KStruct Dầm");
                var beamSplit = beamPanel.AddItem(new SplitButtonData("KStruct_BeamRebarSplit", "Thép dầm")) as SplitButton;
                Add(beamSplit, dll, "KStruct_BeamRebar", "Thép dầm", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceStraightBeamsCommand), "Beam");
                Add(beamSplit, dll, "KStruct_BeamRebarAxis", "Thép dầm theo trục", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceBeamAxisCommand), "RebarColumnV3");
                Add(beamSplit, dll, "KStruct_CurvedBeamRebar", "Thép dầm cong", typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceHorizontalCurvedBeamsCommand), "Beam");
                Add(beamSplit, dll, "KStruct_CutBeamRebar", "Cắt thép ShopDrawing", typeof(global::SDK_HD.KStruct.NativeRebar.OptimizeRebarCuttingCommand), "Settings");
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
                Add(nativeDocumentationPanel, dll, "KStruct_CreatePlansForLevels", "Tạo mặt bằng Level", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreatePlansForLevelsCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_NumberSelectedElements", "Đánh mã cấu kiện", typeof(global::SDK_HD.KStruct.NativeDocumentation.NumberSelectedElementsCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_ExportSelectionCsv", "Xuất dữ liệu\nExcel/CSV", typeof(global::SDK_HD.KStruct.NativeDocumentation.ExportSelectionCsvCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_ImportSelectionCsv", "Nhập tham số\nExcel/CSV", typeof(global::SDK_HD.KStruct.NativeDocumentation.ImportSelectionCsvCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_CopyInstanceParameter", "Sao chép tham số", typeof(global::SDK_HD.KStruct.NativeDocumentation.CopyInstanceParameterCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_CreateBeamSections", "Tạo mặt cắt dầm/cột", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateBeamSectionsCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_CreateSectionReference", "Tham chiếu mặt cắt", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateSectionReferenceCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_ChangeSectionReference", "Đổi view tham chiếu mặt cắt", typeof(global::SDK_HD.KStruct.NativeDocumentation.ChangeSectionReferenceCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_CreateBeamDetailSheets", "Lập hồ sơ\nchi tiết dầm", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateBeamDetailSheetsCommand), "Beam");
                Add(nativeDocumentationPanel, dll, "KStruct_CreateSlabRebarDrawingSheets", "Lập hồ sơ\ncốt thép sàn", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateSlabRebarDrawingSheetsCommand), "Floor");
                Add(nativeDocumentationPanel, dll, "KStruct_TagElementAtPickedPoint", "Gắn tag cấu kiện", typeof(global::SDK_HD.KStruct.NativeDocumentation.TagElementAtPickedPointCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_DimensionBeamSection", "Dim mặt cắt dầm", typeof(global::SDK_HD.KStruct.NativeDocumentation.DimensionBeamSectionCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_CreateFloorOpeningSections", "Mặt cắt lỗ mở sàn", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateFloorOpeningSectionsCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_RoundActiveViewCrop", "Crop tròn view", typeof(global::SDK_HD.KStruct.NativeDocumentation.RoundActiveViewCropCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_MoveActiveViewCrop", "Di chuyển crop view", typeof(global::SDK_HD.KStruct.NativeDocumentation.MoveActiveViewCropCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_SectionViewManager", "Quản lý mặt cắt", typeof(global::SDK_HD.KStruct.NativeDocumentation.SectionViewManagerCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_SplitSectionViewport", "Chia section view", typeof(global::SDK_HD.KStruct.NativeDocumentation.SplitSectionViewportCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_SelectViewSpecificElements", "Chọn đối tượng 2D trong view", typeof(global::SDK_HD.KStruct.NativeDocumentation.SelectViewSpecificElementsCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_MergeLinearDimensions", "Gộp kích thước thẳng", typeof(global::SDK_HD.KStruct.NativeDocumentation.MergeLinearDimensionsCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_DimensionSelectedGrids", "Dim các Grid đã chọn", typeof(global::SDK_HD.KStruct.NativeDocumentation.DimensionSelectedGridsCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_DimensionSelectedLevels", "Dim các Level đã chọn", typeof(global::SDK_HD.KStruct.NativeDocumentation.DimensionSelectedLevelsCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_DimensionPileCenters", "Dim tim cọc", typeof(global::SDK_HD.KStruct.NativeDocumentation.DimensionPileCentersCommand), "Footing");
                Add(nativeDocumentationPanel, dll, "KStruct_SyncTextAndLineStyles", "Đổi kiểu Text/Line hàng loạt", typeof(global::SDK_HD.KStruct.NativeDocumentation.SyncTextAndLineStylesCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_ReplaceTextNotes", "Tìm/thay nội dung\nTextNote", typeof(global::SDK_HD.KStruct.NativeDocumentation.ReplaceTextNotesCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_AlignFloorToBeam", "Căn cao độ sàn theo dầm", typeof(global::SDK_HD.KStruct.NativeDocumentation.AlignFloorToBeamCommand), "Settings");
                Add(nativeDocumentationPanel, dll, "KStruct_AlignBeamToSlab", "Căn dầm theo sàn", typeof(global::SDK_HD.KStruct.NativeDocumentation.AlignBeamsToSlabCommand), "Beam");
                var nativeQuantityPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "KStruct Khối lượng")
                    ?? app.CreateRibbonPanel(tab, "KStruct Khối lượng");
                Add(nativeQuantityPanel, dll, "KStruct_PickFaceArea", "Tính diện tích\nFace đã chọn", typeof(global::SDK_HD.KStruct.NativeDocumentation.PickFaceAreaCommand), "Settings");
                Add(nativeQuantityPanel, dll, "KStruct_CalculateFormworkArea", "Tính diện tích\nván khuôn", typeof(global::SDK_HD.KStruct.NativeDocumentation.CalculateFormworkAreaCommand), "Settings");
                Add(nativeQuantityPanel, dll, "KStruct_MultiCategoryQuantityReport", "Thống kê nhiều\ncategory", typeof(global::SDK_HD.KStruct.NativeDocumentation.MultiCategoryQuantityReportCommand), "Settings");
                Add(nativeQuantityPanel, dll, "KStruct_CheckStructuralTypeNames", "Kiểm tra tên\ntype kết cấu", typeof(global::SDK_HD.KStruct.NativeDocumentation.CheckStructuralTypeNamesCommand), "Settings");
                Add(nativeQuantityPanel, dll, "KStruct_ExportPileCoordinates", "Xuất tọa độ\ntim cọc", typeof(global::SDK_HD.KStruct.NativeDocumentation.ExportPileCoordinatesCommand), "Footing");
                Add(nativeQuantityPanel, dll, "KStruct_CreateExcavationPits", "Tạo/cập nhật\nhố đào móng", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateExcavationPitsCommand), "Footing");
                Add(nativeQuantityPanel, dll, "KStruct_SplitZone", "Tạo/cập nhật\nSplit Zone", typeof(global::SDK_HD.KStruct.NativeDocumentation.SplitZoneCommand), "Settings");
                Add(nativeQuantityPanel, dll, "KStruct_SplitZoneElement", "Sao chép cấu kiện\ntheo zone", typeof(global::SDK_HD.KStruct.NativeDocumentation.SplitZoneElementCommand), "Settings");
                Add(nativeQuantityPanel, dll, "KStruct_SplitZoneElementSettings", "Cài đặt category\nSplit Zone", typeof(global::SDK_HD.KStruct.NativeDocumentation.SplitZoneElementSettingsCommand), "Settings");
                var nativeModelPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "KStruct Mô hình")
                    ?? app.CreateRibbonPanel(tab, "KStruct Mô hình");
                Add(nativeModelPanel, dll, "KStruct_CreateFloorFromPickedFace", "Tạo sàn từ face ngang", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateFloorFromPickedFaceCommand), "Floor");
                Add(nativeModelPanel, dll, "KStruct_CreateFloorFromPickedLines", "Tạo sàn từ Model Lines", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateFloorFromPickedLinesCommand), "Floor");
                Add(nativeModelPanel, dll, "KStruct_CreateFloorFromPickedPoints", "Tạo sàn từ điểm", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateFloorFromPickedPointsCommand), "Floor");
                Add(nativeModelPanel, dll, "KStruct_CreateWallOpening", "Tạo lỗ mở\ntường", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateRectangularWallOpeningCommand), "Settings");
                Add(nativeModelPanel, dll, "KStruct_CreateLintels", "Tạo lintel\ntrên cửa", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateLintelsCommand), "Beam");
                Add(nativeModelPanel, dll, "KStruct_CreateLeanConcrete", "Tạo bê tông lót\ndưới móng", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateLeanConcreteCommand), "Footing");
                Add(nativeModelPanel, dll, "KStruct_CreateFloorOpening", "Tạo lỗ mở\nsàn", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateRectangularFloorOpeningCommand), "Floor");
                Add(nativeModelPanel, dll, "KStruct_CreateCircularFloorSleeve", "Lỗ sleeve tròn\nxuyên sàn", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateCircularFloorSleeveCommand), "Floor");
                Add(nativeModelPanel, dll, "KStruct_CreateMushroomDropPanels", "Tạo drop panel\nmushroom", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateMushroomDropPanelsCommand), "Floor");
                Add(nativeModelPanel, dll, "KStruct_CreateBubbleSlabGeometry", "Tạo hình\nBubble slab", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateBubbleSlabGeometryCommand), "Floor");
                Add(nativeModelPanel, dll, "KStruct_ReviewSlabModules", "Review module\nsàn", typeof(global::SDK_HD.KStruct.NativeDocumentation.ReviewSlabModulesCommand), "Settings");
                Add(nativeModelPanel, dll, "KStruct_PaintWallFaces", "Tô vật liệu\nmặt tường", typeof(global::SDK_HD.KStruct.NativeDocumentation.PaintWallFacesCommand), "Settings");
                Add(nativeModelPanel, dll, "KStruct_ClearWallFacePaint", "Xóa paint\nmặt tường", typeof(global::SDK_HD.KStruct.NativeDocumentation.ClearWallFacePaintCommand), "Settings");
                Add(nativeModelPanel, dll, "KStruct_ArrayElements3D", "Mảng sao chép 3D", typeof(global::SDK_HD.KStruct.NativeDocumentation.ArrayElements3DCommand), "Settings");
                Add(nativeModelPanel, dll, "KStruct_QuickStructuralModel", "Quick Model\nkết cấu", typeof(global::SDK_HD.KStruct.NativeDocumentation.QuickStructuralModelCommand), "Settings");
                Add(nativeModelPanel, dll, "KStruct_Move3DView", "Di chuyển view 3D", typeof(global::SDK_HD.KStruct.NativeDocumentation.Move3DViewCommand), "Settings");
                Add(nativeModelPanel, dll, "KStruct_CreatePurlinsFromRoof", "Tạo xà gồ mái (3 lớp)", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreatePurlinsFromRoofCommand), "Beam");
                var nativeElementControlPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "KStruct Quản lý cấu kiện")
                    ?? app.CreateRibbonPanel(tab, "KStruct Quản lý cấu kiện");
                Add(nativeElementControlPanel, dll, "KStruct_HideStructuralElements", "Ẩn cấu kiện\nkết cấu", typeof(global::SDK_HD.KStruct.NativeDocumentation.HideStructuralElementsCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_ShowStructuralElements", "Hiện cấu kiện\nkết cấu", typeof(global::SDK_HD.KStruct.NativeDocumentation.ShowStructuralElementsCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_PinStructuralElements", "Ghim cấu kiện", typeof(global::SDK_HD.KStruct.NativeDocumentation.PinStructuralElementsCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_UnpinStructuralElements", "Bỏ ghim cấu kiện", typeof(global::SDK_HD.KStruct.NativeDocumentation.UnpinStructuralElementsCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_SelectWallsByType", "Chọn tường\ncùng loại", typeof(global::SDK_HD.KStruct.NativeDocumentation.SelectWallsByTypeCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_CopyBasicWallsToLevels", "Sao chép tường\nsang Level", typeof(global::SDK_HD.KStruct.NativeDocumentation.CopyBasicWallsToLevelsCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_SplitStraightWall", "Chia tường thẳng", typeof(global::SDK_HD.KStruct.NativeDocumentation.SplitStraightWallCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_AlignWallsByPickedPoints", "Căn tường\ntheo điểm", typeof(global::SDK_HD.KStruct.NativeDocumentation.AlignWallsByPickedPointsCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_AlignStructuralElementsToGrid", "Căn cột/dầm/tường\ntheo Grid", typeof(global::SDK_HD.KStruct.NativeDocumentation.AlignStructuralElementsToGridCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_TagBeamsInView", "Tự gắn tag dầm\ntrong view", typeof(global::SDK_HD.KStruct.NativeDocumentation.TagBeamsInViewCommand), "Beam");
                Add(nativeElementControlPanel, dll, "KStruct_SplitStraightBeam", "Chia dầm thẳng", typeof(global::SDK_HD.KStruct.NativeDocumentation.SplitStraightBeamCommand), "Beam");
                Add(nativeElementControlPanel, dll, "KStruct_JoinSelectedBeams", "Ghép hình học dầm", typeof(global::SDK_HD.KStruct.NativeDocumentation.JoinSelectedBeamsCommand), "Beam");
                Add(nativeElementControlPanel, dll, "KStruct_ConnectBeams", "Nối dầm đầu-cuối", typeof(global::SDK_HD.KStruct.NativeDocumentation.ConnectBeamsCommand), "Beam");
                Add(nativeElementControlPanel, dll, "KStruct_ManageStructuralJoins", "Join/Unjoin\ncấu kiện", typeof(global::SDK_HD.KStruct.NativeDocumentation.ManageStructuralJoinsCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_ConvertStructuralFramingType", "Đổi loại dầm", typeof(global::SDK_HD.KStruct.NativeDocumentation.ConvertStructuralFramingTypeCommand), "Beam");
                Add(nativeElementControlPanel, dll, "KStruct_SyncStructuralColumns", "Đồng bộ cột", typeof(global::SDK_HD.KStruct.NativeDocumentation.SyncStructuralColumnsCommand), "Column");
                Add(nativeElementControlPanel, dll, "KStruct_SplitStructuralColumnAtLevel", "Chia cột\ntại Level", typeof(global::SDK_HD.KStruct.NativeDocumentation.SplitStructuralColumnAtLevelCommand), "Column");
                Add(nativeElementControlPanel, dll, "KStruct_SyncIsolatedFoundations", "Đồng bộ móng đơn", typeof(global::SDK_HD.KStruct.NativeDocumentation.SyncIsolatedFoundationsCommand), "Footing");
                var nativeLoadPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "KStruct Tải trọng")
                    ?? app.CreateRibbonPanel(tab, "KStruct Tải trọng");
                Add(nativeLoadPanel, dll, "KStruct_ApplyFloorAreaLoad", "Tải phân bố sàn", typeof(global::SDK_HD.KStruct.NativeDocumentation.ApplyFloorAreaLoadCommand), "Settings");
                Add(nativeLoadPanel, dll, "KStruct_CalculateFloorDeadLoad", "Tính tải tĩnh sàn", typeof(global::SDK_HD.KStruct.NativeDocumentation.CalculateFloorDeadLoadCommand), "Settings");
                Add(nativeLoadPanel, dll, "KStruct_ApplyWallAreaLoad", "Tải phân bố tường", typeof(global::SDK_HD.KStruct.NativeDocumentation.ApplyWallAreaLoadCommand), "Settings");
                Add(nativeLoadPanel, dll, "KStruct_CalculateWallLineLoad", "Tính tải tường lên dầm", typeof(global::SDK_HD.KStruct.NativeDocumentation.CalculateWallLineLoadCommand), "Settings");
                var nativeAnalysisPanel = app.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "KStruct Phân tích")
                    ?? app.CreateRibbonPanel(tab, "KStruct Phân tích");
                Add(nativeAnalysisPanel, dll, "KStruct_CreateAnalyticalModels", "Tạo Analytical Member/Panel", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateAnalyticalModelsCommand), "Settings");
                Add(nativeAnalysisPanel, dll, "KStruct_AnalyticalPanelFromFaces", "Analytical Panel\ntừ Face", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateAnalyticalPanelsFromFacesCommand), "Settings");
                Add(nativeAnalysisPanel, dll, "KStruct_AnalyticalPanelFromMembers", "Analytical Panel\ntừ Member", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateAnalyticalPanelsFromMembersCommand), "Settings");
                Add(nativeAnalysisPanel, dll, "KStruct_AnalyticalPanelFromNodes", "Analytical Panel\ntừ Node", typeof(global::SDK_HD.KStruct.NativeDocumentation.CreateAnalyticalPanelsFromNodesCommand), "Settings");
                Add(nativeAnalysisPanel, dll, "KStruct_CheckAnalyticalAssociations", "Kiểm tra liên kết Analytical", typeof(global::SDK_HD.KStruct.NativeDocumentation.CheckAnalyticalAssociationsCommand), "Settings");
                Add(nativeAnalysisPanel, dll, "KStruct_OptimizeAnalyticalMembers", "Tối ưu Analytical\nMember", typeof(global::SDK_HD.KStruct.NativeDocumentation.OptimizeAnalyticalMembersCommand), "Settings");
                Add(nativeAnalysisPanel, dll, "KStruct_OptimizeAnalyticalPanels", "Tối ưu Analytical\nPanel", typeof(global::SDK_HD.KStruct.NativeDocumentation.OptimizeAnalyticalPanelsCommand), "Settings");
                Add(nativeAnalysisPanel, dll, "KStruct_AlignAnalyticalNodes", "Căn Analytical\nNode", typeof(global::SDK_HD.KStruct.NativeDocumentation.AlignAnalyticalNodesCommand), "Settings");
                Add(nativeAnalysisPanel, dll, "KStruct_ExportAnalyticalModelCsv", "Xuất mô hình phân tích CSV", typeof(global::SDK_HD.KStruct.NativeDocumentation.ExportAnalyticalModelCsvCommand), "Settings");
                Add(nativeAnalysisPanel, dll, "KStruct_ExportAnalyticalModelEtabs", "Xuất mô hình phân tích ETABS", typeof(global::SDK_HD.KStruct.NativeDocumentation.ExportAnalyticalModelToEtabsCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_HidePickedCategory", "Ẩn category\ntrong view", typeof(global::SDK_HD.KStruct.NativeDocumentation.HidePickedCategoryCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_ShowPickedCategory", "Hiện category\ntrong view", typeof(global::SDK_HD.KStruct.NativeDocumentation.ShowPickedCategoryCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_SelectSameCategory", "Chọn cùng category", typeof(global::SDK_HD.KStruct.NativeDocumentation.SelectSameCategoryCommand), "Settings");
                Add(nativeElementControlPanel, dll, "KStruct_SelectSameType", "Chọn cùng type", typeof(global::SDK_HD.KStruct.NativeDocumentation.SelectSameTypeCommand), "Settings");
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
            data.Image = Icon(icon + "_16.png");
            data.LargeImage = Icon(icon + "_32.png");
            panel.AddItem(data);
        }
        private static void Add(SplitButton split, string dll, string id, string title, Type type, string icon)
        {
            if (split == null) throw new InvalidOperationException("Không tạo được nút nhóm thép dầm KStruct.");
            var data = new PushButtonData(id, title, dll, type.FullName);
            data.ToolTip = "KStruct - " + title.Replace('\n', ' ');
            data.Image = Icon(icon + "_16.png");
            data.LargeImage = Icon(icon + "_32.png");
            split.AddPushButton(data);
        }
        private static BitmapImage Icon(string file)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var name = asm.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith(file, StringComparison.OrdinalIgnoreCase));
                if (name == null) return null;
                using (var stream = asm.GetManifestResourceStream(name))
                {
                    var img = new BitmapImage(); img.BeginInit(); img.StreamSource = stream;
                    img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit(); img.Freeze(); return img;
                }
            }
            catch { return null; }
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






