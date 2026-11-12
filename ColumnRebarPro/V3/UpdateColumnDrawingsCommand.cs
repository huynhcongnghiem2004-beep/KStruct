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
    public sealed class UpdateColumnDrawingsCommand : IExternalCommand
    {
        private sealed class ColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance instance && instance.Category != null &&
                instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class DrawingJob
        {
            public List<V3SegmentWork> Works;
            public V3DrawingBuilder Builder;
            public string Label;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                DeploymentSettingsFile deployment = V3DeploymentControl.LoadSavedSettings(doc);
                if (!V3DeploymentControl.IsSavedDeploymentEnabled(deployment))
                {
                    TaskDialog.Show("KStruct - Triển khai cột", DeploymentSetupMessage(deployment));
                    return Result.Cancelled;
                }

                List<FamilyInstance> picked = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>().Where(c => new ColumnFilter().AllowElement(c)).Distinct().ToList();
                if (picked.Count == 0)
                {
                    picked = ui.Selection.PickObjects(ObjectType.Element, new ColumnFilter(),
                            "Chọn ít nhất một cột thuộc tuyến cần cập nhật bản vẽ, nhấn Finish khi xong")
                        .Select(reference => doc.GetElement(reference)).OfType<FamilyInstance>().Distinct().ToList();
                }
                if (picked.Count == 0) return Result.Cancelled;

                var jobs = new List<DrawingJob>();
                var seen = new HashSet<ElementId>();
                foreach (FamilyInstance seed in picked)
                {
                    List<ColumnSegment> stack = ColumnStackFinder.Find(doc, seed);
                    if (stack.Count == 0 || stack.All(segment => seen.Contains(segment.Id))) continue;
                    foreach (ColumnSegment segment in stack) seen.Add(segment.Id);

                    var works = new List<V3SegmentWork>();
                    foreach (ColumnSegment segment in stack.OrderBy(s => s.BottomMm).ThenBy(s => s.Id.Value))
                    {
                        FamilyInstance column = doc.GetElement(segment.Id) as FamilyInstance;
                        if (column == null) throw new InvalidOperationException("Một cột trong tuyến đã bị xóa.");
                        V3RebarConfig config = V3RebarConfigStorage.Read(column);
                        if (config == null)
                            throw new InvalidOperationException("Cột " + segment.Id.Value + " chưa có cấu hình KStruct V3 lưu trong mô hình. Hãy chạy V3 một lần để lưu cấu hình trước khi cập nhật.");
                        if (!HasV3Rebar(doc, column.Id))
                            throw new InvalidOperationException("Cột " + segment.Id.Value + " không còn Rebar V3 để triển khai lại.");
                        works.Add(new V3SegmentWork
                        {
                            Column = column,
                            Segment = segment,
                            Geometry = V3GeometryReader.Read(column),
                            Config = config
                        });
                    }

                    V3StarterConnection.Resolve(works);
                    V3RebarValidator.Preflight(works);
                    jobs.Add(new DrawingJob
                    {
                        Works = works,
                        Builder = new V3DrawingBuilder(deployment, true),
                        Label = GetLabel(works)
                    });
                }
                if (jobs.Count == 0) throw new InvalidOperationException("Không tìm được tuyến cột cần cập nhật.");
                if (jobs.Any(job => !job.Builder.Enabled))
                    throw new InvalidOperationException("Thiết lập triển khai đang tắt. Bật Triển khai trong lệnh V3 rồi xác nhận để lưu cấu hình.");

                var failures = new List<string>();
                int updated = 0;
                using (var group = new TransactionGroup(doc, "KStruct V3 - Cập nhật bản vẽ cột"))
                {
                    group.Start();
                    for (int index = 0; index < jobs.Count; index++)
                    {
                        DrawingJob job = jobs[index];
                        job.Builder.UseSheetIdentity(job.Works, index, jobs.Count);
                        try
                        {
                            job.Builder.Validate(doc, job.Works);
                            using (var transaction = new Transaction(doc, "KStruct V3 - Cập nhật " + job.Label))
                            {
                                transaction.Start();
                                job.Builder.Build(doc, job.Works);
                                if (transaction.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận bản vẽ mới.");
                                updated++;
                            }
                        }
                        catch (Exception ex) { failures.Add(job.Label + ": " + ex.Message); }
                    }
                    if (updated > 0) group.Assimilate();
                    else group.RollBack();
                }

                string report = "Đã cập nhật " + updated + "/" + jobs.Count + " tuyến cột từ Rebar V3 hiện có.";
                if (failures.Count > 0) report += "\nBỏ qua:\n" + string.Join("\n", failures.Take(10));
                TaskDialog.Show("KStruct V3 - Cập nhật bản vẽ", report);
                return updated > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct V3 - Cập nhật bản vẽ", ex.Message);
                return Result.Failed;
            }
        }

        private static string DeploymentSetupMessage(DeploymentSettingsFile settings)
        {
            string status = settings == null
                ? "Project này chưa có thiết lập Triển khai đã lưu."
                : "Thiết lập Triển khai của project đang tắt.";
            return status + " Mở KStruct > Tạo thép cột > Triển khai, bật triển khai, chọn Title Block và loại View mặt cắt, rồi nhấn Lưu thiết lập triển khai. Sau đó chạy lại lệnh này. Không cần tạo lại thép cột.";
        }

        private static bool HasV3Rebar(Document doc, ElementId columnId)
        {
            string partition = V3RebarBuilder.Partition(columnId);
            return new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Any(bar => bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString() == partition);
        }

        private static string GetLabel(IList<V3SegmentWork> works)
        {
            string mark = works.Select(work => work.Column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return string.IsNullOrWhiteSpace(mark) ? "Tuyến " + works[0].Column.Id.Value : mark.Trim();
        }
    }
}
