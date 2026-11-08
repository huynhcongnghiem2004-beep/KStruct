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
    [Transaction(TransactionMode.Manual)]
    public sealed class ColumnRebarV3Command : IExternalCommand
    {
        private sealed class StackJob
        {
            public List<ColumnSegment> Stack;
            public List<V3SegmentWork> Works;
            public V3DrawingBuilder Drawings;
            public bool ReplaceOld;
            public bool OpenAnchorModel;
            public string Label;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null) return Result.Cancelled;

            Document doc = ui.Document;
            try
            {
                List<List<ColumnSegment>> stacks = PickStacks(ui, doc);
                if (stacks == null || stacks.Count == 0)
                    throw new InvalidOperationException("Không có tuyến cột nào được chọn.");

                var loadedDiameters = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>()
                    .Select(x => x.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER))
                    .Where(x => x != null && x.HasValue)
                    .Select(x => V3Units.Mm(x.AsDouble()))
                    .Select(x => Math.Round(x, 2))
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                var jobs = new List<StackJob>();
                var failures = new List<string>();

                var firstWorks = MakeWorks(doc, stacks[0]);
                var firstWindow = ShowWindow(data, stacks[0], loadedDiameters, doc);
                if (firstWindow == null) return Result.Cancelled;
                V3DeploymentControl.PersistSettings(doc, firstWindow.DeploymentSettings);

                var firstConfigMap = firstWindow.Configurations;
                var firstConfigs = stacks[0]
                    .Select(s => firstConfigMap[s.Id.Value])
                    .ToList();

                for (int i = 0; i < firstWorks.Count; i++)
                    firstWorks[i].Config = firstConfigs[i].Copy();

                V3FoundationAnchor.Attach(doc, firstWorks.FirstOrDefault(), firstWindow.FoundationAnchorSettings);
                jobs.Add(NewJob(stacks[0], firstWorks, firstWindow));

                bool asked = false, reuse = false;
                for (int k = 1; k < stacks.Count; k++)
                {
                    var stack = stacks[k];
                    List<V3SegmentWork> works;
                    try { works = MakeWorks(doc, stack); }
                    catch (Exception ex) { failures.Add(StackLabel(stack) + ": " + ex.Message); continue; }

                    if (SameShape(works, firstWorks))
                    {
                        if (!asked)
                        {
                            asked = true;
                            reuse = TaskDialog.Show("KStruct V3 - Nhiều tuyến cột",
                                "Có tuyến cột cùng số tầng và cùng tiết diện với tuyến đầu.\n" +
                                "Dùng lại cấu hình thép và thiết lập triển khai của tuyến đầu cho các tuyến như vậy?\n" +
                                "(Chọn No để cấu hình riêng từng tuyến.)",
                                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) == TaskDialogResult.Yes;
                        }

                        if (reuse)
                        {
                            for (int i = 0; i < works.Count; i++)
                                works[i].Config = firstConfigs[i].Copy();
                            V3FoundationAnchor.Attach(doc, works.FirstOrDefault(), firstWindow.FoundationAnchorSettings);
                            jobs.Add(NewJob(stack, works, firstWindow));
                            continue;
                        }
                    }

                    var window = ShowWindow(data, stack, loadedDiameters, doc);
                    if (window == null)
                    {
                        failures.Add(StackLabel(stack) + ": bỏ qua (không xác nhận cấu hình).");
                        continue;
                    }
                    V3DeploymentControl.PersistSettings(doc, window.DeploymentSettings);

                    var configMap = window.Configurations;
                    for (int i = 0; i < works.Count; i++)
                        works[i].Config = configMap[stack[i].Id.Value];

                    V3FoundationAnchor.Attach(doc, works.FirstOrDefault(), window.FoundationAnchorSettings);
                    jobs.Add(NewJob(stack, works, window));
                }

                var valid = new List<StackJob>();
                foreach (var job in jobs)
                {
                    try
                    {
                        V3StarterConnection.Resolve(job.Works);
                        V3RebarValidator.Preflight(job.Works);
                        V3RebarBuilder.ValidateProjectTypes(doc,job.Works);
                        valid.Add(job);
                    }
                    catch (Exception ex) { failures.Add(job.Label + ": " + ex.Message); }
                }

                jobs = valid;

                for (int j = 0; j < jobs.Count; j++)
                {
                    try
                    {
                        jobs[j].Drawings.UseSheetIdentity(jobs[j].Works, j, jobs.Count);
                        jobs[j].Drawings.Validate(doc, jobs[j].Works);
                    }
                    catch (Exception ex)
                    {
                        failures.Add(jobs[j].Label + ": " + ex.Message);
                        jobs[j] = null;
                    }
                }

                jobs = jobs.Where(x => x != null).ToList();
                if (jobs.Count == 0)
                    throw new InvalidOperationException("Không còn tuyến cột hợp lệ để tạo thép.\n" + string.Join("\n", failures));

                var allWorks = jobs.SelectMany(x => x.Works).ToList();
                if (allWorks.Any(w => w.Config.StarterBars))
                {
                    var confirm = TaskDialog.Show("KStruct - Thép chờ theo thiết kế",
                        "KStruct sẽ tạo thép chờ theo đúng chiều dài đã nhập từ hồ sơ thiết kế. " +
                        "Không tự tính nối chồng hoặc cấu tạo chuyển tiếp tiết diện.\n\n" +
                        "Tạo Rebar 3D dạng DRAFT cho " + jobs.Count + " tuyến cột?",
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                    if (confirm != TaskDialogResult.Yes) return Result.Cancelled;
                }

                var reports = new List<V3BuildReport>();
                int sheetCount = 0;
                ElementId modelPreviewId = ElementId.InvalidElementId;

                using (var group = new TransactionGroup(doc, "KStruct V3 - Cốt thép nhiều tuyến cột"))
                {
                    group.Start();

                    for (int j = 0; j < jobs.Count; j++)
                    {
                        var job = jobs[j];
                        using (var transaction = new Transaction(doc, "KStruct V3 - " + job.Label))
                        {
                            transaction.Start();
                            try
                            {
                                ApplyColumnMark(job.Works);
                                foreach (V3SegmentWork work in job.Works)
                                    V3RebarConfigStorage.Store(work.Column, work.Config);
                                var report = V3RebarBuilder.Build(doc, job.Works, job.ReplaceOld);
                                if (report.ColumnCount == 0)
                                    throw new InvalidOperationException("Không tạo được cốt thép: kiểm tra loại thép đúng đường kính và cấu hình thiết kế.");
                                doc.Regenerate();

                                job.Drawings.UseSheetIdentity(job.Works, j, jobs.Count);
                                var sheets = job.Drawings.Build(doc, job.Works);
                                ElementId preview = ElementId.InvalidElementId;
                                if (job.OpenAnchorModel && job.Works[0].FoundationHost != null)
                                {
                                    using (var sub = new SubTransaction(doc))
                                    {
                                        sub.Start();
                                        try
                                        {
                                            preview = V3AnchorModelView.Create(doc, job.Works[0]);
                                            if (sub.Commit() != TransactionStatus.Committed)
                                                throw new InvalidOperationException("Không lưu được view kiểm tra neo.");
                                        }
                                        catch (Exception ex)
                                        {
                                            if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                            preview = ElementId.InvalidElementId;
                                            report.Warnings.Add("Không tạo được view 3D kiểm tra: " + ex.Message);
                                        }
                                    }
                                }

                                if (transaction.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                                sheetCount += sheets.Count;
                                if (modelPreviewId == ElementId.InvalidElementId && preview != ElementId.InvalidElementId)
                                    modelPreviewId = preview;
                                reports.Add(report);
                            }
                            catch (Exception ex)
                            {
                                if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                                failures.Add(job.Label + ": " + ex.Message);
                            }
                        }
                    }

                    if (reports.Count > 0) group.Assimilate();
                    else group.RollBack();
                }

                string summary;
                if (reports.Count == 0)
                {
                    summary = "Không tạo được tuyến nào.\n\n" + string.Join("\n", failures);
                    TaskDialog.Show("KStruct V3 - Lỗi", summary);
                    return Result.Failed;
                }

                if (modelPreviewId != ElementId.InvalidElementId)
                {
                    try
                    {
                        ui.ActiveView = (View3D)doc.GetElement(modelPreviewId);
                        ui.GetOpenUIViews().FirstOrDefault(v => v.ViewId == modelPreviewId)?.ZoomToFit();
                    }
                    catch (Exception ex) { reports[0].Warnings.Add("Thép đã tạo; mở view KSTR_V3_NEO_L thủ công: " + ex.Message); }
                }

                var stackNames = jobs.Select(j => j.Label).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                summary =
                    "Đã tạo " + reports.Count + "/" + stacks.Count + " nhóm cột (" + reports.Sum(r => r.ColumnCount) + " cột).\n" +
                    "Mark/nhóm: " + string.Join(", ", stackNames) + "\n" +
                    "Thép dọc: " + reports.Sum(r => r.MainBars) + " thanh (" + reports.Sum(r => r.MainSets) + " nhóm)\n" +
                    "Đai bao: " + reports.Sum(r => r.Ties) + " thanh (" + reports.Sum(r => r.TieSets) + " nhóm)\n" +
                    "Đai phụ: " + reports.Sum(r => r.CrossTies) + " thanh (" + reports.Sum(r => r.CrossTieSets) + " nhóm)\n" +
                    (sheetCount > 0 ? "Sheet đã tạo: " + sheetCount + "\n" : "") +
                    "\n" + V3RebarValidator.Notice;

                var warnings = reports.SelectMany(r => r.Warnings).Distinct().ToList();
                if (warnings.Count > 0) summary += "\n\n" + string.Join("\n", warnings);
                if (failures.Count > 0) summary += "\n\nTUYẾN KHÔNG TẠO ĐƯỢC:\n" + string.Join("\n", failures);

                TaskDialog.Show("KStruct - Đã dựng thép 3D", summary);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct V3 - Lỗi", ex.Message);
                return Result.Failed;
            }
        }

        private static string GetColumnMark(FamilyInstance column)
        {
            if (column == null) return "UNKNOWN";

            var p = column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (p != null)
            {
                var value = p.AsString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "C" + column.Id.Value;
        }

        private static string GetStackMarkLabel(List<V3SegmentWork> works)
        {
            if (works == null || works.Count == 0)
                return "Tuyến cột";

            var marks = works
                .Select(w => GetColumnMark(w.Column))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (marks.Count == 1)
                return marks[0];

            return string.Join(" / ", marks.Take(3));
        }

        // Phải gọi trong Transaction. Chỉ điền Mark còn trống bằng Mark người dùng đã đặt cho tuyến;
        // không ghi đè Mark có sẵn của các tầng khác.
        private static void ApplyColumnMark(IList<V3SegmentWork> works)
        {
            if (works == null || works.Count == 0) return;

            string common = null;
            foreach (var item in works)
            {
                var p = item.Column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                string value = p == null ? null : p.AsString();
                if (!string.IsNullOrWhiteSpace(value)) { common = value.Trim(); break; }
            }
            if (common == null) return;

            foreach (var item in works)
            {
                var param = item.Column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                if (param == null || param.IsReadOnly) continue;
                if (string.IsNullOrWhiteSpace(param.AsString())) param.Set(common);
            }
        }

        private static List<List<ColumnSegment>> PickStacks(UIDocument ui, Document doc)
        {
            var filter = new StructuralColumnFilter();

            var picked = ui.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(e => e != null && filter.AllowElement(e))
                .Cast<FamilyInstance>()
                .ToList();

            if (picked.Count == 0)
            {
                picked = ui.Selection.PickObjects(ObjectType.Element, filter,
                        "Chọn các cột (mỗi tuyến chọn 1 cột bất kỳ), nhấn Finish khi xong")
                    .Select(r => (FamilyInstance)doc.GetElement(r))
                    .ToList();
            }

            if (picked.Count == 0)
                throw new InvalidOperationException("Không phát hiện cột phù hợp.");

            var result = new List<List<ColumnSegment>>();
            var seen = new HashSet<ElementId>();

            var groupedByMark = picked
                .GroupBy(c => GetColumnMark(c), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var markGroup in groupedByMark)
            {
                foreach (var column in markGroup)
                {
                    if (seen.Contains(column.Id))
                        continue;

                    List<ColumnSegment> stack = ColumnStackFinder.Find(doc, column);
                    if (stack.Count == 0)
                        continue;

                    var ordered = stack
                        .OrderBy(s => s.BottomMm)
                        .ThenBy(s => s.Id.Value)
                        .ToList();

                    foreach (var seg in ordered)
                        seen.Add(seg.Id);

                    result.Add(ordered);
                }
            }

            if (result.Count == 0)
                throw new InvalidOperationException("Không phát hiện cột phù hợp.");

            return result;
        }

        private static List<V3SegmentWork> MakeWorks(Document doc, List<ColumnSegment> stack)
        {
            var works = new List<V3SegmentWork>();
            foreach (var segment in stack)
            {
                var column = doc.GetElement(segment.Id) as FamilyInstance;
                if (column == null) throw new InvalidOperationException("Cột đã bị xóa trong Project.");
                works.Add(new V3SegmentWork
                {
                    Column = column,
                    Segment = segment,
                    Geometry = V3GeometryReader.Read(column)
                });
            }
            return works;
        }

        private static bool SameShape(List<V3SegmentWork> a, List<V3SegmentWork> b)
        {
            if (a.Count != b.Count) return false;

            const double tol = 0.003;
            for (int i = 0; i < a.Count; i++)
            {
                var x = a[i].Geometry;
                var y = b[i].Geometry;

                if (Math.Abs(x.Width - y.Width) > tol ||
                    Math.Abs(x.Depth - y.Depth) > tol ||
                    Math.Abs(x.Height - y.Height) > tol)
                    return false;
            }

            return true;
        }

        private static ColumnRebarV3Window ShowWindow(ExternalCommandData data, List<ColumnSegment> stack,
            List<double> loadedDiameters, Document doc)
        {
            var window = new ColumnRebarV3Window(stack, loadedDiameters, doc);
            new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;

            if (window.ShowDialog() != true || !window.Accepted) return null;
            return window;
        }

        private static StackJob NewJob(List<ColumnSegment> stack, List<V3SegmentWork> works, ColumnRebarV3Window window)
        {
            var mark = GetStackMarkLabel(works);

            return new StackJob
            {
                Stack = stack,
                Works = works,
                ReplaceOld = window.ReplaceOld,
                OpenAnchorModel = window.OpenAnchorModel,
                Drawings = new V3DrawingBuilder(window.DeploymentSettings),
                Label = mark
            };
        }

        private static string StackLabel(List<ColumnSegment> stack)
        {
            if (stack == null || stack.Count == 0) return "Tuyến cột";
            return "Tuyến cột #" + stack[0].Id.Value;
        }

        private static string StackLabel(List<V3SegmentWork> works)
        {
            if (works == null || works.Count == 0)
                return "Tuyến cột";

            var marks = works
                .Select(w => GetColumnMark(w.Column))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (marks.Count == 1)
                return marks[0];

            return string.Join(" / ", marks.Take(3));
        }

        private sealed class StructuralColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                return e is FamilyInstance && e.Category != null &&
                       e.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
            }

            public bool AllowReference(Reference r, XYZ p) { return false; }
        }
    }
}
