using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class PickFaceAreaCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            try
            {
                IList<Reference> references = ui.Selection.PickObjects(
                    ObjectType.Face, "Chọn một hoặc nhiều mặt để cộng diện tích; nhấn Finish để xem kết quả");
                if (references == null || references.Count == 0) return Result.Cancelled;

                Document doc = ui.Document;
                var rows = new List<FaceAreaRow>();
                foreach (Reference reference in references)
                {
                    Element host = doc.GetElement(reference.ElementId);
                    Face face = host == null ? null : host.GetGeometryObjectFromReference(reference) as Face;
                    if (face == null) continue;
                    rows.Add(new FaceAreaRow
                    {
                        ElementId = host.Id.Value,
                        Category = host.Category == null ? "(không có Category)" : host.Category.Name,
                        ElementName = host.Name ?? "",
                        AreaSquareMeters = UnitUtils.ConvertFromInternalUnits(face.Area, UnitTypeId.SquareMeters)
                    });
                }
                if (rows.Count == 0)
                {
                    TaskDialog.Show("KStruct - Diện tích mặt", "Không đọc được Face từ các đối tượng đã chọn.");
                    return Result.Cancelled;
                }

                double total = rows.Sum(row => row.AreaSquareMeters);
                string details = string.Join("\n", rows.Select((row, index) =>
                    (index + 1).ToString(CultureInfo.InvariantCulture) + ". " + row.Category +
                    " | " + row.ElementName +
                    " | Id " + row.ElementId.ToString(CultureInfo.InvariantCulture) +
                    " | " + row.AreaSquareMeters.ToString("N3", CultureInfo.CurrentCulture) + " m²"));
                TaskDialog.Show("KStruct - Diện tích mặt",
                    "Đã đọc " + rows.Count.ToString(CultureInfo.InvariantCulture) + " mặt." +
                    "\nTổng diện tích: " + total.ToString("N3", CultureInfo.CurrentCulture) + " m²" +
                    "\n\n" + details);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Diện tích mặt", ex.Message);
                return Result.Failed;
            }
        }

        private sealed class FaceAreaRow
        {
            public long ElementId;
            public string Category;
            public string ElementName;
            public double AreaSquareMeters;
        }
    }
}
