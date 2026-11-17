// UnifiedColumnRebarCommand.cs
// Lệnh "Tạo thép cột" thống nhất – KStruct
// Phát hiện loại cột (tròn / chữ nhật) và điều phối đến flow phù hợp.
// Tuân thủ TCVN 5574:2018 (bê tông cốt thép), TCVN 4453:1995 (thi công đổ bê tông),
//           TCVN 1651 (thép thanh), TCVN 198:1997 (cấu tạo).
//
// Chiến lược:
//   1. Người dùng chọn một hoặc nhiều cột kết cấu (OST_StructuralColumns).
//   2. Lệnh tự phân loại mỗi cột:
//      • Bounding box ~vuông → cột tròn → chạy RoundColumnRebarCommand flow.
//      • Còn lại               → cột chữ nhật → chạy ColumnRebarV3Command flow.
//   3. Từng nhóm được xử lý độc lập; báo cáo tổng hợp cuối cùng.
//
// Lưu ý: ColumnTransitionCommand (cột đổi tiết diện) là workflow độc lập chuyên dụng,
//         vẫn giữ nút riêng vì cần UI tương tác khác biệt hoàn toàn.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace ColumnRebarPro.V3
{
    /// <summary>
    /// Lệnh thống nhất "Tạo thép cột" – phát hiện loại cột và dispatch đến
    /// flow thép cột chữ nhật V3 hoặc thép cột tròn.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class UnifiedColumnRebarCommand : IExternalCommand
    {
        // Ngưỡng tỉ lệ bounding box để phân biệt cột tròn (width/depth ≈ 1)
        private const double RoundColumnRatio = 0.08;

        private sealed class ColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) =>
                e is FamilyInstance fi && fi.Category != null &&
                fi.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;

            try
            {
                // ── 1. Chọn cột ────────────────────────────────────────────────────────
                List<FamilyInstance> picked = ui.Selection.GetElementIds()
                    .Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(c => c.Category != null &&
                                c.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns)
                    .Distinct().ToList();

                if (picked.Count == 0)
                {
                    picked = ui.Selection
                        .PickObjects(ObjectType.Element, new ColumnFilter(),
                            "Chọn cột cần bố trí cốt thép (chữ nhật & tròn đều được), nhấn Finish khi xong")
                        .Select(r => doc.GetElement(r)).OfType<FamilyInstance>()
                        .Distinct().ToList();
                }

                if (picked.Count == 0) return Result.Cancelled;

                // ── 2. Phân loại ────────────────────────────────────────────────────────
                var rectColumns  = new List<FamilyInstance>(); // cột chữ nhật
                var roundColumns = new List<FamilyInstance>(); // cột tròn
                var unclassified = new List<string>();

                foreach (var col in picked)
                {
                    BoundingBoxXYZ box = col.get_BoundingBox(null);
                    if (box == null) { unclassified.Add("Cột " + col.Id.Value + ": không đọc được BoundingBox"); continue; }
                    double w = box.Max.X - box.Min.X;
                    double d = box.Max.Y - box.Min.Y;
                    if (w <= 0 || d <= 0) { unclassified.Add("Cột " + col.Id.Value + ": BoundingBox không hợp lệ"); continue; }

                    double ratio = Math.Abs(w - d) / Math.Max(w, d);
                    if (ratio <= RoundColumnRatio)
                        roundColumns.Add(col);
                    else
                        rectColumns.Add(col);
                }

                if (rectColumns.Count == 0 && roundColumns.Count == 0)
                {
                    TaskDialog.Show("KStruct – Tạo thép cột",
                        "Không phân loại được cột nào.\n" + string.Join("\n", unclassified.Take(8)));
                    return Result.Cancelled;
                }

                // ── 3. Thông báo phân loại (nếu có cả hai loại) ─────────────────────
                if (rectColumns.Count > 0 && roundColumns.Count > 0)
                {
                    var msg = $"Phát hiện:\n• {rectColumns.Count} cột chữ nhật → dùng quy trình V3\n" +
                              $"• {roundColumns.Count} cột tròn → dùng quy trình cột tròn\n\n" +
                              "Tiếp tục xử lý tuần tự từng nhóm?";
                    var dlg = new TaskDialog("KStruct – Phân loại cột") { MainContent = msg };
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Tiếp tục");
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Hủy");
                    dlg.CommonButtons = TaskDialogCommonButtons.None;
                    if (dlg.Show() != TaskDialogResult.CommandLink1)
                        return Result.Cancelled;
                }

                var report = new List<string>();

                // ── 4a. Xử lý cột chữ nhật V3 ──────────────────────────────────────
                if (rectColumns.Count > 0)
                {
                    // Cập nhật selection để ColumnRebarV3Command.PickStacks() lấy đúng
                    ui.Selection.SetElementIds(rectColumns.Select(c => c.Id).ToList());
                    string rectMsg = "";
                    Result rectResult = new ColumnRebarV3Command().Execute(data, ref rectMsg, elements);
                    if (rectResult == Result.Succeeded)
                        report.Add($"✓ Cột chữ nhật: {rectColumns.Count} cột đã xử lý.");
                    else if (rectResult == Result.Cancelled)
                        report.Add($"⊘ Cột chữ nhật: đã hủy ({rectColumns.Count} cột).");
                    else
                        report.Add($"✗ Cột chữ nhật lỗi: {rectMsg}");
                }

                // ── 4b. Xử lý cột tròn ──────────────────────────────────────────────
                if (roundColumns.Count > 0)
                {
                    ui.Selection.SetElementIds(roundColumns.Select(c => c.Id).ToList());
                    string roundMsg = "";
                    Result roundResult = new global::SDK_HD.KStruct.NativeRebar.RoundColumnRebarCommand()
                        .Execute(data, ref roundMsg, elements);
                    if (roundResult == Result.Succeeded)
                        report.Add($"✓ Cột tròn: {roundColumns.Count} cột đã xử lý.");
                    else if (roundResult == Result.Cancelled)
                        report.Add($"⊘ Cột tròn: đã hủy ({roundColumns.Count} cột).");
                    else
                        report.Add($"✗ Cột tròn lỗi: {roundMsg}");
                }

                // ── 5. Báo cáo tổng hợp ─────────────────────────────────────────────
                if (unclassified.Count > 0)
                    report.Add("\nBỏ qua:\n" + string.Join("\n", unclassified.Take(8)));

                // Chỉ hiện tổng hợp khi cả hai loại chạy, không cần thiết nếu chỉ 1 loại
                if (rectColumns.Count > 0 && roundColumns.Count > 0 && report.Count > 0)
                    TaskDialog.Show("KStruct – Tổng hợp thép cột", string.Join("\n", report));

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct – Tạo thép cột", ex.Message);
                return Result.Failed;
            }
        }
    }
}
