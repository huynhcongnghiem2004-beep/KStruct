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
    public sealed class DimensionSelectedLevelsCommand : IExternalCommand
    {
        private sealed class LevelFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Level;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = ui.ActiveView;
            try
            {
                if (!(view is ViewSection) || Math.Abs(Math.Abs(view.UpDirection.Normalize().DotProduct(XYZ.BasisZ)) - 1.0) > 1e-4 ||
                    Math.Abs(view.ViewDirection.Normalize().DotProduct(XYZ.BasisZ)) > 1e-4)
                    throw new InvalidOperationException("Mở Elevation hoặc Section đứng, có phương đứng song song trục Z, trước khi tạo dim Level.");

                List<Level> levels = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Level>().Distinct().ToList();
                if (levels.Count < 2)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new LevelFilter(),
                        "Chọn ít nhất hai Level để tạo chuỗi kích thước cao độ");
                    levels = picked.Select(reference => doc.GetElement(reference)).OfType<Level>().Distinct().ToList();
                }
                levels = levels.OrderBy(level => level.Elevation).ToList();
                if (levels.Count < 2) throw new InvalidOperationException("Cần chọn ít nhất hai Level.");
                if (levels.Last().Elevation - levels.First().Elevation < doc.Application.ShortCurveTolerance * 2)
                    throw new InvalidOperationException("Các Level được chọn trùng cao độ.");

                XYZ point = ui.Selection.PickPoint("Chọn vị trí đặt đường kích thước cho chuỗi Level");
                XYZ right = view.RightDirection.Normalize();
                XYZ viewDirection = view.ViewDirection.Normalize();
                double rightOffset = point.DotProduct(right);
                double depth = point.DotProduct(viewDirection);
                XYZ basePoint = right * rightOffset + viewDirection * depth;
                double extension = UnitUtils.ConvertToInternalUnits(600, UnitTypeId.Millimeters);
                XYZ start = basePoint + XYZ.BasisZ * (levels.First().Elevation - extension);
                XYZ end = basePoint + XYZ.BasisZ * (levels.Last().Elevation + extension);

                var references = new ReferenceArray();
                foreach (Level level in levels) references.Append(level.GetPlaneReference());
                using (Transaction transaction = new Transaction(doc, "KStruct - Dimension Level"))
                {
                    transaction.Start();
                    Dimension dimension = doc.Create.NewDimension(view, Line.CreateBound(start, end), references);
                    if (dimension == null)
                    {
                        transaction.RollBack();
                        throw new InvalidOperationException("Revit không tạo được chuỗi kích thước cho các Level đã chọn.");
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận kích thước Level.");
                }
                TaskDialog.Show("KStruct - Dimension Level", "Đã tạo chuỗi kích thước cho " + levels.Count + " Level.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Dimension Level", ex.Message);
                return Result.Failed;
            }
        }
    }
}
