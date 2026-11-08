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
    public sealed class CopySelectedRebarCommand : IExternalCommand
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
                List<Rebar> rebars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (rebars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(), "Chọn các thanh thép cần sao chép");
                    rebars = picked.Select(r => doc.GetElement(r)).OfType<Rebar>().Distinct().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;
                BoundingBoxXYZ box = rebars[0].get_BoundingBox(null);
                if (box == null) throw new InvalidOperationException("Không xác định được phạm vi của thanh thép đầu tiên.");
                XYZ basePoint = (box.Min + box.Max) * 0.5;
                XYZ targetPoint = ui.Selection.PickPoint("Chọn vị trí đặt bản sao của thanh thép đầu tiên");
                XYZ translation = targetPoint - basePoint;
                if (translation.GetLength() < 1e-9) return Result.Cancelled;

                ICollection<ElementId> copies;
                using (Transaction transaction = new Transaction(doc, "KStruct - Sao chép cốt thép"))
                {
                    transaction.Start();
                    copies = ElementTransformUtils.CopyElements(doc, rebars.Select(x => x.Id).ToList(), translation);
                    if (copies == null || copies.Count == 0)
                        throw new InvalidOperationException("Revit không tạo được bản sao cốt thép.");
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thao tác sao chép.");
                }
                TaskDialog.Show("KStruct - Sao chép cốt thép",
                    "Đã sao chép " + rebars.Count + " Rebar; Revit tạo " + copies.Count + " phần tử. Kiểm tra lại host và vị trí trước khi phát hành.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Sao chép cốt thép", ex.Message);
                return Result.Failed;
            }
        }
    }
}