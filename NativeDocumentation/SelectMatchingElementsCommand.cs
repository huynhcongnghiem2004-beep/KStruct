using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class SelectSameCategoryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            SelectMatchingElements.Execute(data, ref message, sameType: false);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class SelectSameTypeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            SelectMatchingElements.Execute(data, ref message, sameType: true);
    }

    internal static class SelectMatchingElements
    {
        public static Result Execute(ExternalCommandData data, ref string message, bool sameType)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Element seed = ui.Selection.GetElementIds().Select(doc.GetElement).FirstOrDefault(element =>
                    element != null && element.Category != null && element.Category.CategoryType == CategoryType.Model);
                if (seed == null)
                {
                    Reference reference = ui.Selection.PickObject(ObjectType.Element,
                        "Chọn cấu kiện làm mẫu để chọn cùng " + (sameType ? "loại" : "category"));
                    seed = doc.GetElement(reference);
                }
                if (seed?.Category == null)
                {
                    TaskDialog.Show("KStruct - Chọn tương tự", "Phần tử mẫu không có category.");
                    return Result.Cancelled;
                }

                ElementId categoryId = seed.Category.Id;
                ElementId typeId = seed.GetTypeId();
                IEnumerable<Element> query = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(element => element.Category != null && element.Category.Id == categoryId && !element.ViewSpecific);
                if (sameType)
                {
                    if (typeId == ElementId.InvalidElementId)
                    {
                        TaskDialog.Show("KStruct - Chọn cùng loại", "Phần tử mẫu không có type để đối chiếu.");
                        return Result.Cancelled;
                    }
                    query = query.Where(element => element.GetTypeId() == typeId);
                }

                List<ElementId> ids = query.Select(element => element.Id).Distinct().ToList();
                if (ids.Count == 0) return Result.Cancelled;
                ui.Selection.SetElementIds(ids);
                ui.ShowElements(ids);
                string label = sameType ? seed.Name : seed.Category.Name;
                TaskDialog.Show("KStruct - Chọn tương tự",
                    "Đã chọn " + ids.Count + " phần tử " + (sameType ? "cùng type" : "cùng category") + ": " + label + ".");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Chọn tương tự", ex.Message);
                return Result.Failed;
            }
        }
    }
}
