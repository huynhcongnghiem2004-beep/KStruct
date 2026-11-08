using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class StructuralElementControl
    {
        private static readonly HashSet<long> Categories = new HashSet<long>
        {
            (long)BuiltInCategory.OST_StructuralFraming,
            (long)BuiltInCategory.OST_StructuralColumns,
            (long)BuiltInCategory.OST_StructuralFoundation,
            (long)BuiltInCategory.OST_Floors,
            (long)BuiltInCategory.OST_Walls,
            (long)BuiltInCategory.OST_Stairs,
            (long)BuiltInCategory.OST_StairsRuns,
            (long)BuiltInCategory.OST_StairsLandings,
            (long)BuiltInCategory.OST_Rebar
        };

        private sealed class StructuralFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => IsStructural(element);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public static bool IsStructural(Element element) =>
            element is AreaReinforcement || element is PathReinforcement ||
            (element?.Category?.Id != null && Categories.Contains(element.Category.Id.Value));

        public static List<Element> GetSelection(UIDocument ui, Document doc, string prompt)
        {
            List<Element> selected = ui.Selection.GetElementIds().Select(doc.GetElement)
                .Where(IsStructural).Distinct().ToList();
            if (selected.Count != 0) return selected;
            IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new StructuralFilter(), prompt);
            return picked.Select(r => doc.GetElement(r)).Where(IsStructural).Distinct().ToList();
        }

        public static Result ChangePinState(ExternalCommandData data, ref string message, bool pin)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> elements = GetSelection(ui, doc, pin
                    ? "Chọn cấu kiện kết cấu cần ghim (Pin)"
                    : "Chọn cấu kiện kết cấu cần bỏ ghim (Unpin)");
                if (elements.Count == 0) return Result.Cancelled;
                int changed = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, pin ? "KStruct - Ghim cấu kiện" : "KStruct - Bỏ ghim cấu kiện"))
                {
                    tx.Start();
                    foreach (Element element in elements)
                    {
                        try
                        {
                            if (element.Pinned == pin) continue;
                            element.Pinned = pin;
                            changed++;
                        }
                        catch (Exception ex) { skipped.Add(element.Id.Value + ": " + ex.Message); }
                    }
                    if (changed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Khóa cấu kiện", "Không có phần tử nào cần thay đổi trạng thái.");
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi Pin/Unpin.");
                }
                ShowReport("Khóa cấu kiện", pin ? "Đã ghim" : "Đã bỏ ghim", changed, elements.Count, skipped);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Khóa cấu kiện", ex.Message);
                return Result.Failed;
            }
        }

        public static Result ChangeVisibility(ExternalCommandData data, ref string message, bool show)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = doc.ActiveView;
            try
            {
                List<Element> elements;
                if (show)
                {
                    elements = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                        .WhereElementIsViewIndependent().Where(IsStructural).Where(e => e.IsHidden(view)).ToList();
                    if (elements.Count == 0)
                    {
                        TaskDialog.Show("KStruct - Hiển thị cấu kiện", "Không có cấu kiện kết cấu nào đang bị ẩn trong view hiện tại.");
                        return Result.Cancelled;
                    }
                }
                else
                {
                    elements = GetSelection(ui, doc, "Chọn cấu kiện kết cấu cần ẩn trong view hiện tại");
                    if (elements.Count == 0) return Result.Cancelled;
                }

                List<ElementId> candidates = elements.Select(e => e.Id).Distinct()
                    .Where(id => show || !doc.GetElement(id).IsHidden(view)).ToList();
                if (candidates.Count == 0)
                {
                    TaskDialog.Show("KStruct - Hiển thị cấu kiện", show
                        ? "Các cấu kiện đã chọn không bị ẩn trong view hiện tại."
                        : "Các cấu kiện đã chọn đều đã bị ẩn trong view hiện tại.");
                    return Result.Cancelled;
                }

                using (Transaction tx = new Transaction(doc, show ? "KStruct - Hiện cấu kiện" : "KStruct - Ẩn cấu kiện"))
                {
                    tx.Start();
                    if (show) view.UnhideElements(candidates);
                    else view.HideElements(candidates);
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi hiển thị.");
                }
                ui.RefreshActiveView();
                TaskDialog.Show("KStruct - Hiển thị cấu kiện", (show ? "Đã hiện " : "Đã ẩn ") + candidates.Count + " cấu kiện trong view hiện tại.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Hiển thị cấu kiện", ex.Message);
                return Result.Failed;
            }
        }

        private static void ShowReport(string title, string verb, int changed, int total, IList<string> skipped)
        {
            string report = verb + " " + changed + "/" + total + " cấu kiện kết cấu.";
            if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " phần tử:\n" + string.Join("\n", skipped.Take(8));
            TaskDialog.Show("KStruct - " + title, report);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class HideStructuralElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            StructuralElementControl.ChangeVisibility(data, ref message, false);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class ShowStructuralElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            StructuralElementControl.ChangeVisibility(data, ref message, true);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class PinStructuralElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            StructuralElementControl.ChangePinState(data, ref message, true);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class UnpinStructuralElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            StructuralElementControl.ChangePinState(data, ref message, false);
    }
}
