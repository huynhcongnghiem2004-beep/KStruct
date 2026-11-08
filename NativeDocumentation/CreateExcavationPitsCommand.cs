using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateExcavationPitsCommand : IExternalCommand
    {
        private const string ApplicationKey = "KStruct.ExcavationPit";

        private sealed class ExcavationOptions
        {
            public double ClearanceMm;
            public double TopOffsetMm;
            public double BottomDepthMm;
            public double SideSlopeHorizontalPerVertical;
        }

        private sealed class FoundationFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element.Category != null &&
                element.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> foundations = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .Where(item => item != null && item.Category != null &&
                        item.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation).Distinct().ToList();
                if (foundations.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FoundationFilter(),
                        "Chọn một hoặc nhiều móng để tạo hố đào KStruct");
                    foundations = picked.Select(reference => doc.GetElement(reference))
                        .Where(item => item != null).Distinct().ToList();
                }
                if (foundations.Count == 0) return Result.Cancelled;
                ExcavationOptions options = ShowOptions(data.Application.MainWindowHandle);
                if (options == null) return Result.Cancelled;

                var profiles = foundations.Select(foundation => new
                {
                    Foundation = foundation,
                    Box = GetWorldBounds(foundation.get_BoundingBox(null))
                }).Where(item => item.Box != null).ToList();
                if (profiles.Count == 0)
                    throw new InvalidOperationException("Không đọc được bounding box của các móng đã chọn.");

                int created = 0;
                double totalVolume = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Tạo hố đào theo móng"))
                {
                    tx.Start();
                    foreach (var item in profiles)
                    {
                        double clearance = UnitUtils.ConvertToInternalUnits(options.ClearanceMm, UnitTypeId.Millimeters);
                        double topOffset = UnitUtils.ConvertToInternalUnits(options.TopOffsetMm, UnitTypeId.Millimeters);
                        double bottomDepth = UnitUtils.ConvertToInternalUnits(options.BottomDepthMm, UnitTypeId.Millimeters);
                        double minX = item.Box.Min.X - clearance;
                        double minY = item.Box.Min.Y - clearance;
                        double maxX = item.Box.Max.X + clearance;
                        double maxY = item.Box.Max.Y + clearance;
                        double bottomZ = item.Box.Min.Z - bottomDepth;
                        double topZ = item.Box.Max.Z + topOffset;
                        double height = topZ - bottomZ;
                        if (height <= doc.Application.ShortCurveTolerance)
                            throw new InvalidOperationException("Chiều cao hố đào không hợp lệ tại móng Id " + item.Foundation.Id.Value + ".");

                        double slopeRun = bottomDepth * options.SideSlopeHorizontalPerVertical;
                        Solid excavationSolid;
                        if (slopeRun > doc.Application.ShortCurveTolerance && bottomDepth > doc.Application.ShortCurveTolerance)
                        {
                            double gradeMinX = minX - slopeRun, gradeMaxX = maxX + slopeRun;
                            double gradeMinY = minY - slopeRun, gradeMaxY = maxY + slopeRun;
                            var profileLoops = new List<CurveLoop>
                            {
                                CreateRectangle(minX, minY, maxX, maxY, bottomZ),
                                CreateRectangle(gradeMinX, gradeMinY, gradeMaxX, gradeMaxY, item.Box.Min.Z),
                                CreateRectangle(gradeMinX, gradeMinY, gradeMaxX, gradeMaxY, topZ)
                            };
                            excavationSolid = GeometryCreationUtilities.CreateLoftGeometry(profileLoops, null);
                        }
                        else
                        {
                            CurveLoop profile = CreateRectangle(minX, minY, maxX, maxY, bottomZ);
                            excavationSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                                new List<CurveLoop> { profile }, XYZ.BasisZ, height);
                        }
                        if (excavationSolid == null || excavationSolid.Volume <= 1e-9)
                            throw new InvalidOperationException("Revit không tạo được khối hố đào tại móng Id " + item.Foundation.Id.Value + ".");

                        string sourceId = item.Foundation.UniqueId ?? item.Foundation.Id.Value.ToString(CultureInfo.InvariantCulture);
                        List<ElementId> oldShapes = new FilteredElementCollector(doc)
                            .OfClass(typeof(DirectShape)).Cast<DirectShape>()
                            .Where(shape => string.Equals(shape.ApplicationId, ApplicationKey, StringComparison.Ordinal) &&
                                string.Equals(shape.ApplicationDataId, sourceId, StringComparison.Ordinal))
                            .Select(shape => shape.Id).ToList();
                        if (oldShapes.Count > 0) doc.Delete(oldShapes);

                        DirectShape pit = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                        pit.ApplicationId = ApplicationKey;
                        pit.ApplicationDataId = sourceId;
                        pit.SetShape(new GeometryObject[] { excavationSolid });
                        Parameter comments = pit.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (comments != null && !comments.IsReadOnly)
                            comments.Set("KStruct hố đào | Móng Id " + item.Foundation.Id.Value.ToString(CultureInfo.InvariantCulture));
                        created++;
                        totalVolume += excavationSolid.Volume;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các khối hố đào.");
                }

                TaskDialog.Show("KStruct - Đào đất",
                    "Đã tạo/cập nhật " + created.ToString(CultureInfo.InvariantCulture) + " khối hố đào." +
                    "\nThể tích hình học tổng: " + UnitUtils.ConvertFromInternalUnits(totalVolume, UnitTypeId.CubicMeters).ToString("N3", CultureInfo.CurrentCulture) + " m³" +
                    "\nKhối hố đào là DirectShape KStruct để phối hợp và thống kê; cần kiểm tra cao độ, taluy và giao cắt với địa hình trước thi công.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Đào đất", ex.Message);
                return Result.Failed;
            }
        }

        private static BoundingBoxXYZ GetWorldBounds(BoundingBoxXYZ box)
        {
            if (box == null) return null;
            Transform transform = box.Transform ?? Transform.Identity;
            XYZ[] points = new XYZ[]
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            }.Select(transform.OfPoint).ToArray();
            return new BoundingBoxXYZ
            {
                Min = new XYZ(points.Min(point => point.X), points.Min(point => point.Y), points.Min(point => point.Z)),
                Max = new XYZ(points.Max(point => point.X), points.Max(point => point.Y), points.Max(point => point.Z))
            };
        }

        private static CurveLoop CreateRectangle(double minX, double minY, double maxX, double maxY, double z)
        {
            XYZ p0 = new XYZ(minX, minY, z);
            XYZ p1 = new XYZ(maxX, minY, z);
            XYZ p2 = new XYZ(maxX, maxY, z);
            XYZ p3 = new XYZ(minX, maxY, z);
            return CurveLoop.Create(new Curve[]
            {
                Line.CreateBound(p0, p1), Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3), Line.CreateBound(p3, p0)
            });
        }

        private static ExcavationOptions ShowOptions(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Thông số hố đào KStruct",
                Width = 390,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new StackPanel { Margin = new Thickness(14) };
            var clearance = AddNumber(root, "Khoảng hở thi công quanh móng (mm):", "500");
            var top = AddNumber(root, "Chiều cao khối trên đỉnh móng (mm):", "1500");
            var bottom = AddNumber(root, "Chiều sâu đào dưới đáy móng (mm):", "500");
            var slope = AddNumber(root, "Taluy ngang / đứng (0 = thành đứng):", "0.5");
            root.Children.Add(new TextBlock
            {
                Text = "KStruct tạo khối phối hợp quanh bounding box móng; taluy mở rộng từ đáy đào lên đáy móng. Công cụ không cắt địa hình Revit. Chạy lại sẽ cập nhật khối KStruct của cùng móng.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            ExcavationOptions result = null;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Tạo/cập nhật", MinWidth = 100, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 75, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double clearanceMm, topMm, bottomMm, slopeValue;
                if (!TryNumber(clearance.Text, out clearanceMm) || !TryNumber(top.Text, out topMm) ||
                    !TryNumber(bottom.Text, out bottomMm) || !TryNumber(slope.Text, out slopeValue) ||
                    clearanceMm < 0 || topMm < 0 || bottomMm < 0 || slopeValue < 0 || slopeValue > 5 ||
                    topMm + bottomMm <= 0)
                {
                    TaskDialog.Show("KStruct - Đào đất", "Nhập số không âm, taluy tối đa 5 ngang/1 đứng; tổng chiều cao trên đỉnh và dưới đáy phải lớn hơn 0.");
                    return;
                }
                result = new ExcavationOptions { ClearanceMm = clearanceMm, TopOffsetMm = topMm, BottomDepthMm = bottomMm,
                    SideSlopeHorizontalPerVertical = slopeValue };
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }

        private static System.Windows.Controls.TextBox AddNumber(System.Windows.Controls.Panel root, string label, string initial)
        {
            root.Children.Add(new TextBlock { Text = label });
            var box = new System.Windows.Controls.TextBox { Text = initial, MinWidth = 350, Margin = new Thickness(0, 3, 0, 8) };
            root.Children.Add(box);
            return box;
        }

        private static bool TryNumber(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
