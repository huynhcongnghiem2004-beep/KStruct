using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.FootingRebar
{
    [Transaction(TransactionMode.Manual)]
    public class FootingRebarCommand : IExternalCommand
    {
        private static readonly Guid LeanConcreteSchemaId = new Guid("A1D688B8-04F7-4F60-A29D-4316BAF4FE12");

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            return ExecuteCore(data, ref message, false);
        }

        internal static Result ExecuteUpdate(ExternalCommandData data, ref string message) => ExecuteCore(data, ref message, true);

        private static Result ExecuteCore(ExternalCommandData data, ref string message, bool updateOnly)
        {
            try { return Run(data, updateOnly); }
            catch (Exception ex) { return global::SDK_HD.KStruct.Helpers.Error(ex, ref message); }
        }

        private static Result Run(ExternalCommandData data, bool updateOnly)
        {
            UIDocument uidoc = data.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = uidoc.Document;

            // 1. Chọn móng đơn hoặc đài cọc
            FamilyInstance fi = null;
            var selected = uidoc.Selection.GetElementIds();
            if (updateOnly && selected.Count > 0)
            {
                var hosts = selected.Select(id => ResolveUpdateHost(doc.GetElement(id))).ToList();
                if (hosts.Any(host => host == null))
                    return Fail("Chọn đối tượng cập nhật", "Hãy chọn móng hoặc thép do lệnh Thép móng cọc & đơn tạo. Không cập nhật thép thủ công/lệnh khác.");
                List<FamilyInstance> distinct = hosts.GroupBy(host => host.Id).Select(group => group.First()).ToList();
                if (distinct.Count != 1)
                    return Fail("Chọn một móng", "Lệnh cập nhật xử lý một móng mỗi lần. Có thể chọn các nhóm thép thuộc cùng móng đó.");
                fi = distinct[0];
            }
            else if (selected.Count == 1) fi = doc.GetElement(selected.First()) as FamilyInstance;
            if (fi == null || !IsFoundation(fi))
            {
                try
                {
                    Reference r = uidoc.Selection.PickObject(
                        ObjectType.Element, updateOnly ? (ISelectionFilter)new UpdateFilter() : new FoundationFilter(),
                        updateOnly ? "Chọn móng hoặc nhóm thép KStruct cần cập nhật" : "Chọn móng đơn hoặc đài cọc");
                    fi = updateOnly ? ResolveUpdateHost(doc.GetElement(r)) : doc.GetElement(r) as FamilyInstance;
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (fi == null) return Result.Cancelled;

            // 2. Đọc hình học móng
            FootingFrame frame;
            try { frame = GeometryReader.Read(fi); }
            catch (InvalidOperationException ex) { return Fail("Không đọc được móng", ex.Message); }

            // Nhận diện loại móng: Móng cọc hay Móng đơn
            string famName = fi.Symbol?.FamilyName ?? "";
            string typeName = fi.Name ?? "";
            bool isPileCap = famName.IndexOf("cọc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             famName.IndexOf("coc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             famName.IndexOf("pile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             famName.IndexOf("cap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             famName.IndexOf("đài", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             famName.IndexOf("dai", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             typeName.IndexOf("cọc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             typeName.IndexOf("pile", StringComparison.OrdinalIgnoreCase) >= 0;

            var modelWithType = new FootingModel(
                frame.Model.LengthX, frame.Model.WidthY, frame.Model.Height,
                isPileCap ? FootingType.PileCap : FootingType.SpreadFooting,
                $"{famName} : {typeName}");

            // 3. Lấy thông số từ cửa sổ cấu hình và các loại thép trong dự án
            List<double> availableDiameters = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.Structure.RebarBarType))
                .Cast<Autodesk.Revit.DB.Structure.RebarBarType>()
                .Select(bt => UnitConv.ToMm(bt.BarModelDiameter))
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (availableDiameters.Count == 0)
                return Fail("Thiếu loại thép", "Dự án chưa có RebarBarType nào. Hãy nạp loại thép trước khi tạo bố trí.");

            bool hasLeanConcrete = frame.HasEmbeddedLeanConcrete || HasLeanConcreteBelowFooting(doc, fi, frame);
            string projectKey = doc.ProjectInformation.UniqueId + "|" +
                (string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName).ToUpperInvariant();
            var memoryWarnings = new List<string>();
            FootingRebarPreference hostPreference = null;
            bool brokenHistory = false;
            try { hostPreference = FootingRebarHistoryStorage.ReadHost(fi); }
            catch (Exception ex) { brokenHistory = true; memoryWarnings.Add("Không đọc được lịch sử móng: " + ex.Message); }
            FootingRebarSession session = null;
            try { session = FootingRebarPreferenceStore.Load(projectKey, hostPreference?.FootingType ?? modelWithType.FootingType); }
            catch (Exception ex) { memoryWarnings.Add("Không đọc được lựa chọn gần nhất: " + ex.Message); }
            List<Rebar> hosted = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(r => r.GetHostId() == fi.Id).ToList();
            List<Rebar> owned = hosted.Where(r => FootingRebarHistoryStorage.IsOwned(r, fi)).ToList();
            int otherCount = hosted.Count - owned.Count;
            if (updateOnly && (owned.Count == 0 || hostPreference == null))
                return Fail("Không có bộ thép để cập nhật",
                    "Móng cần có thép và thông số đã được lệnh Thép móng cọc & đơn nhận diện. " +
                    "Thép từ phiên bản cũ, thép thủ công hoặc lệnh khác được giữ nguyên; lệnh cập nhật không tạo mới thay thế." +
                    (brokenHistory ? "\n" + string.Join("\n", memoryWarnings) : string.Empty));
            FootingRebarPreference restored = hostPreference ?? (!brokenHistory && session?.Remember == true ? session.Preference : null);
            string memorySource = hostPreference != null ? "Đã nạp thông số lần tạo của móng này." :
                restored != null ? "Đã nạp lần tạo thành công gần nhất của loại móng này trong project." : "Chưa có thông số cũ để nạp; dùng cấu hình ban đầu.";
            memorySource += "\nKích thước luôn lấy từ mô hình hiện tại. Thép nhận diện: " + owned.Count + " nhóm; chưa nhận diện: " + otherCount + " nhóm.";
            if (otherCount > 0) memorySource += "\nThép phiên bản cũ/thủ công/lệnh khác không được tự nhận là thép của lệnh này.";
            if (memoryWarnings.Count > 0) memorySource += "\n" + string.Join("\n", memoryWarnings);
            var settingsWindow = new FootingRebarWindow(modelWithType, availableDiameters, hasLeanConcrete,
                restored, memorySource, session?.Remember ?? true, owned.Count, otherCount, hostPreference != null);
            if (data.Application.MainWindowHandle != IntPtr.Zero)
                new System.Windows.Interop.WindowInteropHelper(settingsWindow).Owner = data.Application.MainWindowHandle;
            if (updateOnly) settingsWindow.ConfigureForUpdate();
            if (settingsWindow.ShowDialog() != true) return Result.Cancelled;

            FootingRebarFullSettings fullSettings = settingsWindow.OutputFull;
            if (fullSettings == null)
            {
                var dummyTop = new TopLayerSettings(false, new BarLayerSettings(false, 12, 200), new BarLayerSettings(false, 12, 200));
                var dummySide = new SideTiesSettings(false);
                fullSettings = new FootingRebarFullSettings(
                    modelWithType.FootingType,
                    settingsWindow.OutputCover ?? FootingDefaults.Cover(),
                    settingsWindow.OutputBottom ?? FootingDefaults.Bottom(isPileCap),
                    dummyTop, dummySide);
            }

            // UI may explicitly switch the footing classification; validate/build/report the same classification.
            modelWithType = new FootingModel(modelWithType.LengthX, modelWithType.WidthY, modelWithType.Height,
                fullSettings.FootingType, modelWithType.Name);
            if (owned.Count > 0 && !settingsWindow.ReplaceOwnedRebar)
                return Fail("Thép đã tồn tại", "Hãy chọn cập nhật bộ thép đã nhận diện; không tạo trùng.");
            if (!updateOnly && otherCount > 0 && !settingsWindow.AllowAlongsideOtherRebar)
                return Fail("Thép chưa nhận diện", "Giữ nguyên thép hiện có. Chỉ tạo bổ sung khi bạn chủ động chọn cho phép trong cửa sổ.");

            // 4. Kiểm tra quy tắc thiết kế theo TCVN 5574:2018 & TCVN 198:1997
            IReadOnlyList<RuleIssue> issues = new Tcvn5574FootingRules().ValidateFull(modelWithType, fullSettings);

            var errors = issues.Where(i => i.Severity == Severity.Error).ToList();
            if (errors.Count > 0)
                return Fail("Vi phạm tiêu chuẩn kỹ thuật (TCVN)", string.Join("\n", errors.Select(e => "• " + e.Message)));

            var warnings = issues.Where(i => i.Severity == Severity.Warning).ToList();
            if (warnings.Count > 0)
            {
                var td = new TaskDialog("Cảnh báo thiết kế (TCVN)")
                {
                    MainInstruction = $"Có {warnings.Count} khuyến nghị theo TCVN 5574:2018 / TCVN 198:1997. Bạn vẫn muốn tiếp tục tạo cốt thép?",
                    MainContent = string.Join("\n", warnings.Select(w => "• " + w.Message)),
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;
            }

            // 5. Lập kế hoạch thép (thuần C#, chưa đụng Revit)
            RebarPlan plan;
            try { plan = new BottomLayerBuilder().Build(modelWithType, fullSettings); }
            catch (ArgumentException ex) { return Fail("Không lập được kế hoạch thép", ex.Message); }

            // 6. Commit trong MỘT transaction duy nhất; xác minh hình học thực tế trước commit.
            var collector = new WarningCollector();
            List<CreatedSet> created;
            List<string> post = new List<string>();
            string txName = updateOnly ? "KStruct - Cập nhật thép móng cọc, móng đơn" : modelWithType.FootingType == FootingType.PileCap
                ? "KStruct - Tạo thép đài cọc theo TCVN"
                : "KStruct - Tạo thép móng đơn theo TCVN";

            using (var tx = new Transaction(doc, txName))
            {
                tx.Start();
                FailureHandlingOptions fho = tx.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(collector);
                tx.SetFailureHandlingOptions(fho);
                try
                {
                    if (owned.Count > 0) FootingRebarHistoryStorage.DeleteOwned(doc, fi, owned);
                    created = RebarFactory.Create(doc, frame, plan);
                    if (created.Count == 0 || created.Count != plan.Sets.Count)
                        throw new InvalidOperationException("Không tạo đủ nhóm thép theo kế hoạch; đã hủy và giữ bộ thép cũ.");
                    doc.Regenerate();
                    post = PostCheck.Verify(doc, created, frame, fullSettings.Cover);
                    if (post.Count > 0)
                    {
                        tx.RollBack();
                        return Fail("Thép vượt lớp bảo vệ", string.Join("\n", post));
                    }
                    FootingRebarHistoryStorage.Record(doc, fi, created, fullSettings);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return Fail("Không tạo được thép", ex.Message);
                }
                if (tx.Commit() != TransactionStatus.Committed)
                    return Fail("Không tạo được thép", "Transaction không được commit.");
            }

            // Persist project defaults only after the Revit transaction has committed successfully.
            try
            {
                FootingRebarPreferenceStore.Save(projectKey, fullSettings.FootingType,
                    new FootingRebarSession { Remember = settingsWindow.RememberForNextFooting,
                        Preference = settingsWindow.RememberForNextFooting ? FootingRebarPreference.From(fullSettings) : null });
            }
            catch (Exception ex) { collector.Warnings.Add("Đã tạo thép nhưng không lưu được lựa chọn cho móng kế tiếp: " + ex.Message); }
            if (otherCount > 0 && !updateOnly) collector.Warnings.Add("Giữ nguyên " + otherCount + " nhóm thép chưa nhận diện; cần kiểm tra khả năng trùng với bộ mới.");

            // 7. Báo cáo sau khi hậu kiểm thực tế đã đạt trong transaction.
            ShowReport(plan, modelWithType, frame.Source, fullSettings.Cover.HasLeanConcrete, created, collector.Warnings, post,
                owned.Count > 0 ? "Đã cập nhật bộ thép nhận diện của móng; giữ nguyên " + otherCount + " nhóm thép khác." : "Đã lưu lần tạo và mã nhận diện cho bộ thép mới.");
            return Result.Succeeded;
        }

        private static bool HasLeanConcreteBelowFooting(Document doc, FamilyInstance foundation, FootingFrame frame)
        {
            if (HasKStructLeanConcrete(doc, foundation)) return true;

            // Fallback for manually modeled blinding concrete: a thin Floor in contact with
            // the underside and covering most of the footing plan is treated as the bedding layer.
            frame.WorldEnvelope(out XYZ hostMin, out XYZ hostMax);
            double hostArea = Math.Max(1e-9, (hostMax.X - hostMin.X) * (hostMax.Y - hostMin.Y));
            double contactTolerance = UnitUtils.ConvertToInternalUnits(25, UnitTypeId.Millimeters);
            double overlapTolerance = UnitUtils.ConvertToInternalUnits(10, UnitTypeId.Millimeters);
            double minLayerThickness = UnitUtils.ConvertToInternalUnits(20, UnitTypeId.Millimeters);
            double maxLayerThickness = UnitUtils.ConvertToInternalUnits(200, UnitTypeId.Millimeters);

            foreach (Floor floor in new FilteredElementCollector(doc).OfClass(typeof(Floor))
                         .WhereElementIsNotElementType().Cast<Floor>())
            {
                BoundingBoxXYZ box = floor.get_BoundingBox(null);
                if (box == null) continue;
                double thickness = box.Max.Z - box.Min.Z;
                double gap = hostMin.Z - box.Max.Z;
                if (thickness < minLayerThickness || thickness > maxLayerThickness ||
                    gap < -overlapTolerance || gap > contactTolerance) continue;

                double overlapX = Math.Max(0, Math.Min(hostMax.X, box.Max.X) - Math.Max(hostMin.X, box.Min.X));
                double overlapY = Math.Max(0, Math.Min(hostMax.Y, box.Max.Y) - Math.Max(hostMin.Y, box.Min.Y));
                if (overlapX * overlapY / hostArea >= 0.70) return true;
            }
            return false;
        }

        private static bool HasKStructLeanConcrete(Document doc, FamilyInstance foundation)
        {
            Schema schema = Schema.Lookup(LeanConcreteSchemaId);
            Field field = schema?.GetField("SourceFoundationUniqueId");
            if (schema == null || field == null) return false;

            foreach (Floor floor in new FilteredElementCollector(doc).OfClass(typeof(Floor))
                         .WhereElementIsNotElementType().Cast<Floor>())
            {
                try
                {
                    Entity entity = floor.GetEntity(schema);
                    if (entity.IsValid() && string.Equals(
                        entity.Get<string>(field), foundation.UniqueId, StringComparison.Ordinal))
                        return true;
                }
                catch { /* Ignore floors without this extensible-storage schema. */ }
            }
            return false;
        }

        private static bool IsFoundation(FamilyInstance fi) =>
            fi.Category != null &&
            fi.Category.Id.Equals(new ElementId(BuiltInCategory.OST_StructuralFoundation));

        private static Result Fail(string title, string detail)
        {
            TaskDialog.Show(title, detail);
            return Result.Failed;
        }

        private static void ShowReport(RebarPlan plan, FootingModel model, string source, bool hasLeanConcrete,
                                       IList<CreatedSet> created, IList<string> revitWarnings, IList<string> postIssues, string operationInfo = null)
        {
            var sb = new StringBuilder();
            string typeDesc = model.FootingType == FootingType.PileCap ? "MÓNG CỌC / ĐÀI CỌC" : "MÓNG ĐƠN";
            sb.AppendLine($"{typeDesc}: {model.LengthX:0} x {model.WidthY:0} x {model.Height:0} mm. (Nguồn: {source})");
            if (!string.IsNullOrWhiteSpace(operationInfo)) sb.AppendLine(operationInfo);
            sb.AppendLine("Tuân thủ: TCVN 5574:2018 & TCVN 198:1997");
            sb.AppendLine("Bê tông lót: " + (hasLeanConcrete ? "Có (dùng xác định lớp bảo vệ; không tính vào kích thước móng)." : "Không phát hiện lớp lót bên dưới móng."));
            sb.AppendLine();
            sb.AppendLine("Các nhóm cốt thép đã tạo:");
            foreach (BarSet s in plan.Sets)
            {
                string desc = s.IsStirrup ? "đai" : "thanh";
                sb.AppendLine($"• {s.Name}: {s.Count} {desc} Ø{s.Diameter:0.#}");
            }

            int problems = revitWarnings.Count + postIssues.Count;
            if (problems == 0)
            {
                sb.AppendLine();
                sb.Append("Kiểm tra hình học sau khi tạo: Hoàn toàn chính xác, không có cảnh báo.");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine($"CẦN KIỂM TRA LẠI ({problems}):");
                foreach (string w in revitWarnings.Distinct()) sb.AppendLine("• Revit: " + w);
                foreach (string p in postIssues) sb.AppendLine("• " + p);
            }
            TaskDialog.Show("KStruct - Cốt thép móng theo TCVN", sb.ToString());
        }

        private static FamilyInstance ResolveUpdateHost(Element element)
        {
            if (element is FamilyInstance foundation && IsFoundation(foundation)) return foundation;
            if (element is Rebar bar && bar.Document.GetElement(bar.GetHostId()) is FamilyInstance host &&
                IsFoundation(host) && FootingRebarHistoryStorage.IsOwned(bar, host)) return host;
            return null;
        }

        private sealed class UpdateFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => ResolveUpdateHost(element) != null;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class FoundationFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is FamilyInstance fi && IsFoundation(fi);
            public bool AllowReference(Reference r, XYZ p) => false;
        }
    }
}
