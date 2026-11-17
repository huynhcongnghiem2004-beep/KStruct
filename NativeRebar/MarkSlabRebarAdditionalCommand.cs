using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class MarkSlabRebarAdditionalCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Rebar> bars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (bars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(),
                        "Chọn các bộ Rebar sàn cần đưa vào mặt bằng thép tăng cường");
                    bars = picked.Select(reference => doc.GetElement(reference)).OfType<Rebar>().Distinct().ToList();
                }
                bars = bars.Where(bar => doc.GetElement(bar.GetHostId()) is Floor).ToList();
                if (bars.Count == 0) throw new InvalidOperationException("Không có Rebar nào được host bởi Floor trong lựa chọn.");

                int marked = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Đánh dấu thép sàn tăng cường"))
                {
                    tx.Start();
                    foreach (Rebar bar in bars)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                ReinforceSlabRegionCommand.MarkAsManualAdditionalBar(bar);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không lưu được metadata của Rebar.");
                                marked++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add(bar.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (marked == 0)
                    {
                        tx.RollBack();
                        throw new InvalidOperationException("Không đánh dấu được bộ thép nào.");
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận metadata thép tăng cường.");
                }
                string report = "Đã đánh dấu " + marked + " bộ Rebar sàn là thép tăng cường. Chạy ‘Lập hồ sơ cốt thép sàn’ để tạo/cập nhật sheet riêng.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " bộ: " + string.Join(", ", skipped.Take(6));
                TaskDialog.Show("KStruct - Thép tăng cường sàn", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thép tăng cường sàn", ex.Message);
                return Result.Failed;
            }
        }
    }
}
