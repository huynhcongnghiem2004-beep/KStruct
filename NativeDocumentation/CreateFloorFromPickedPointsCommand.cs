using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateFloorFromPickedPointsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                var points = new List<XYZ>();
                double tolerance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
                while (points.Count < 500)
                {
                    try
                    {
                        XYZ point = ui.Selection.PickPoint(points.Count == 0
                            ? "Chọn lần lượt ít nhất 3 đỉnh theo chu vi sàn; nhấn Esc để kết thúc"
                            : "Chọn đỉnh tiếp theo theo chu vi; nhấn Esc khi đã chọn xong");
                        if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > tolerance)
                            points.Add(point);
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        if (points.Count >= 3) break;
                        return Result.Cancelled;
                    }
                }
                if (points.Count > 1 && points[0].DistanceTo(points[points.Count - 1]) <= tolerance)
                    points.RemoveAt(points.Count - 1);
                if (points.Count < 3) return Result.Cancelled;
                double elevation = points[0].Z;
                if (points.Any(p => Math.Abs(p.Z - elevation) > tolerance))
                    throw new InvalidOperationException("Các đỉnh phải nằm trên cùng một mặt phẳng ngang. Hãy pick trong mặt bằng hoặc work plane ngang.");

                var loop = new CurveLoop();
                for (int i = 0; i < points.Count; i++)
                {
                    XYZ start = points[i];
                    XYZ end = points[(i + 1) % points.Count];
                    if (start.DistanceTo(end) <= tolerance)
                        throw new InvalidOperationException("Profile có hai đỉnh liên tiếp trùng nhau.");
                    loop.Append(Line.CreateBound(start, end));
                }
                IList<CurveLoop> profile = new List<CurveLoop> { loop };
                if (!BoundaryValidation.IsValidHorizontalBoundary(profile))
                    throw new InvalidOperationException("Các điểm tạo profile tự giao hoặc không hợp lệ theo Revit.");

                List<FloorType> types = new FilteredElementCollector(doc).OfClass(typeof(FloorType))
                    .Cast<FloorType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>().OrderBy(l => l.Elevation).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có FloorType.");
                if (levels.Count == 0) throw new InvalidOperationException("Project chưa có Level.");
                CreateFloorFromPickedLinesCommand.Options options = CreateFloorFromPickedLinesCommand.ChooseOptions(
                    data.Application.MainWindowHandle, types, levels, elevation, doc.ActiveView.GenLevel);
                if (options == null) return Result.Cancelled;

                using (Transaction tx = new Transaction(doc, "KStruct - Tạo sàn từ điểm"))
                {
                    tx.Start();
                    Floor floor = Floor.Create(doc, profile, options.Type.Id, options.Level.Id, options.Structural, null, 0);
                    if (floor == null) throw new InvalidOperationException("Floor.Create trả về null.");
                    Parameter offset = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                    if (offset != null && !offset.IsReadOnly) offset.Set(elevation - options.Level.Elevation);
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận sàn mới.");
                }
                TaskDialog.Show("KStruct - Tạo sàn từ điểm", "Đã tạo sàn từ " + points.Count + " đỉnh.\nLoại: " + options.Type.Name + "\nLevel: " + options.Level.Name);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tạo sàn từ điểm", ex.Message);
                return Result.Failed;
            }
        }
    }
}
