using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ToggleRebarVisibilityCommand : IExternalCommand
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
            View view = doc.ActiveView;
            try
            {
                List<Rebar> rebars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (rebars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(), "Chọn các thanh thép cần đổi hiển thị trong view hiện tại");
                    rebars = picked.Select(r => doc.GetElement(r)).OfType<Rebar>().Distinct().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;

                bool makeUnobscured = rebars.Any(rebar => !rebar.IsUnobscuredInView(view));
                int changed = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Đổi hiển thị cốt thép"))
                {
                    transaction.Start();
                    foreach (Rebar rebar in rebars)
                    {
                        try
                        {
                            rebar.SetUnobscuredInView(view, makeUnobscured);
                            changed++;
                        }
                        catch (Exception ex) { skipped.Add(rebar.Id + ": " + ex.Message); }
                    }
                    if (changed == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Hiển thị cốt thép", "Không thể đổi hiển thị cho các thanh đã chọn trong view này.");
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi hiển thị.");
                }
                ui.RefreshActiveView();
                string state = makeUnobscured ? "không bị che khuất" : "theo hiển thị thông thường";
                string report = "Đã cập nhật " + changed + "/" + rebars.Count + " thanh: " + state + ".";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " thanh không hỗ trợ trong view này.";
                TaskDialog.Show("KStruct - Hiển thị cốt thép", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Hiển thị cốt thép", ex.Message);
                return Result.Failed;
            }
        }
    }
}