using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal sealed class WallOpeningFilter : ISelectionFilter
    {
        public bool AllowElement(Element element) => element is Wall;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    internal sealed class FloorOpeningFilter : ISelectionFilter
    {
        public bool AllowElement(Element element) => element is Floor;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CreateRectangularWallOpeningCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Wall wall = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Wall>().FirstOrDefault();
                if (wall == null)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new WallOpeningFilter(), "Chọn tường đứng để tạo lỗ mở");
                    wall = doc.GetElement(picked) as Wall;
                }
                if (wall == null) return Result.Cancelled;
                Line axis = (wall.Location as LocationCurve)?.Curve as Line;
                if (axis == null) throw new InvalidOperationException("Chỉ hỗ trợ tường thẳng.");
                XYZ axisVector = axis.GetEndPoint(1) - axis.GetEndPoint(0);
                XYZ planVector = new XYZ(axisVector.X, axisVector.Y, 0);
                if (planVector.GetLength() <= 1e-8 || Math.Abs(axisVector.Z) > UnitUtils.ConvertToInternalUnits(5, UnitTypeId.Millimeters))
                    throw new InvalidOperationException("Chỉ hỗ trợ tường thẳng đứng, không nghiêng.");

                XYZ pickedA = ui.Selection.PickPoint("Chọn góc thứ nhất trên mặt tường trong view đứng");
                XYZ pickedB = ui.Selection.PickPoint("Chọn góc đối diện trên mặt tường");
                XYZ wallAxis = planVector.Normalize();
                XYZ origin = axis.GetEndPoint(0);
                double uA = (new XYZ(pickedA.X - origin.X, pickedA.Y - origin.Y, 0)).DotProduct(wallAxis);
                double uB = (new XYZ(pickedB.X - origin.X, pickedB.Y - origin.Y, 0)).DotProduct(wallAxis);
                double length = planVector.GetLength();
                double u0 = Math.Max(0, Math.Min(uA, uB));
                double u1 = Math.Min(length, Math.Max(uA, uB));
                double z0 = Math.Min(pickedA.Z, pickedB.Z);
                double z1 = Math.Max(pickedA.Z, pickedB.Z);
                double tolerance = UnitUtils.ConvertToInternalUnits(25, UnitTypeId.Millimeters);
                if (u1 - u0 <= tolerance || z1 - z0 <= tolerance)
                    throw new InvalidOperationException("Hai điểm phải tạo thành lỗ chữ nhật có chiều rộng và cao lớn hơn 25 mm.");

                XYZ p0 = origin + wallAxis.Multiply(u0) + XYZ.BasisZ.Multiply(z0 - origin.Z);
                XYZ p1 = origin + wallAxis.Multiply(u1) + XYZ.BasisZ.Multiply(z1 - origin.Z);
                using (Transaction tx = new Transaction(doc, "KStruct - Tạo lỗ mở tường"))
                {
                    tx.Start();
                    doc.Create.NewOpening(wall, p0, p1);
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận lỗ mở trên tường.");
                }
                TaskDialog.Show("KStruct - Lỗ mở tường", "Đã tạo lỗ mở chữ nhật trên Wall " + wall.Id.Value + ".");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Lỗ mở tường", ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CreateRectangularFloorOpeningCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Floor floor = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Floor>().FirstOrDefault();
                if (floor == null)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new FloorOpeningFilter(), "Chọn sàn ngang để tạo lỗ mở");
                    floor = doc.GetElement(picked) as Floor;
                }
                if (floor == null) return Result.Cancelled;
                BoundingBoxXYZ box = floor.get_BoundingBox(null);
                if (box == null) throw new InvalidOperationException("Không đọc được phạm vi sàn.");
                double topZ = GetWorldCorners(box).Max(p => p.Z);
                XYZ pickedA = ui.Selection.PickPoint("Chọn góc thứ nhất của lỗ mở trong mặt bằng");
                XYZ pickedB = ui.Selection.PickPoint("Chọn góc đối diện của lỗ mở");
                double x0 = Math.Min(pickedA.X, pickedB.X), x1 = Math.Max(pickedA.X, pickedB.X);
                double y0 = Math.Min(pickedA.Y, pickedB.Y), y1 = Math.Max(pickedA.Y, pickedB.Y);
                double tolerance = UnitUtils.ConvertToInternalUnits(25, UnitTypeId.Millimeters);
                if (x1 - x0 <= tolerance || y1 - y0 <= tolerance)
                    throw new InvalidOperationException("Hai điểm phải tạo thành lỗ chữ nhật lớn hơn 25 mm theo hai phương.");

                XYZ p0 = new XYZ(x0, y0, topZ), p1 = new XYZ(x1, y0, topZ);
                XYZ p2 = new XYZ(x1, y1, topZ), p3 = new XYZ(x0, y1, topZ);
                var profile = new CurveArray();
                profile.Append(Line.CreateBound(p0, p1));
                profile.Append(Line.CreateBound(p1, p2));
                profile.Append(Line.CreateBound(p2, p3));
                profile.Append(Line.CreateBound(p3, p0));
                using (Transaction tx = new Transaction(doc, "KStruct - Tạo lỗ mở sàn"))
                {
                    tx.Start();
                    doc.Create.NewOpening(floor, profile, true);
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận lỗ mở trên sàn.");
                }
                TaskDialog.Show("KStruct - Lỗ mở sàn", "Đã tạo lỗ mở chữ nhật trên Floor " + floor.Id.Value + ".");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Lỗ mở sàn", ex.Message);
                return Result.Failed;
            }
        }

        private static List<XYZ> GetWorldCorners(BoundingBoxXYZ box)
        {
            var result = new List<XYZ>();
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        result.Add(box.Transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                            y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            return result;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CreateCircularFloorSleeveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Floor floor = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Floor>().FirstOrDefault();
                if (floor == null)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new FloorOpeningFilter(), "Chọn sàn cần tạo lỗ sleeve tròn");
                    floor = doc.GetElement(picked) as Floor;
                }
                if (floor == null) return Result.Cancelled;

                XYZ centerPick = ui.Selection.PickPoint("Chọn tâm lỗ sleeve trên mặt bằng");
                XYZ radiusPick = ui.Selection.PickPoint("Chọn một điểm trên chu vi để xác định bán kính");
                double radius = Math.Sqrt(Math.Pow(centerPick.X - radiusPick.X, 2) + Math.Pow(centerPick.Y - radiusPick.Y, 2));
                double minRadius = UnitUtils.ConvertToInternalUnits(10, UnitTypeId.Millimeters);
                if (radius < minRadius) throw new InvalidOperationException("Bán kính lỗ phải lớn hơn 10 mm.");
                BoundingBoxXYZ box = floor.get_BoundingBox(null);
                if (box == null) throw new InvalidOperationException("Không đọc được cao độ mặt sàn.");
                double topZ = GetWorldCorners(box).Max(p => p.Z);
                var centers = new List<XYZ> { new XYZ(centerPick.X, centerPick.Y, topZ) };
                while (true)
                {
                    try
                    {
                        XYZ next = ui.Selection.PickPoint("Chọn tâm lỗ sleeve tiếp theo; nhấn Esc để tạo các lỗ đã chọn");
                        centers.Add(new XYZ(next.X, next.Y, topZ));
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; }
                }
                int created = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Tạo lỗ sleeve sàn"))
                {
                    tx.Start();
                    foreach (XYZ center in centers)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                var profile = new CurveArray();
                                profile.Append(Arc.Create(center, radius, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
                                profile.Append(Arc.Create(center, radius, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
                                doc.Create.NewOpening(floor, profile, true);
                                if (sub.Commit() == TransactionStatus.Committed) created++;
                                else throw new InvalidOperationException("Revit không chấp nhận vị trí này.");
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add(ex.Message);
                            }
                        }
                    }
                    if (created == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Sleeve sàn", "Không tạo được lỗ nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận lỗ tròn trên sàn.");
                }
                double diameterMm = UnitUtils.ConvertFromInternalUnits(2 * radius, UnitTypeId.Millimeters);
                TaskDialog.Show("KStruct - Sleeve sàn",
                    "Đã tạo " + created + " lỗ xuyên tròn Ø" + diameterMm.ToString("0.#") + " mm trên Floor " + floor.Id.Value +
                    ". Đây là Opening của Revit, không phải family sleeve." +
                    (skipped.Count == 0 ? "" : "\nBỏ qua " + skipped.Count + " vị trí:\n" + string.Join("\n", skipped.Take(8))));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Sleeve sàn", ex.Message);
                return Result.Failed;
            }
        }

        private static List<XYZ> GetWorldCorners(BoundingBoxXYZ box)
        {
            var result = new List<XYZ>();
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        result.Add(box.Transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                            y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            return result;
        }
    }
}
