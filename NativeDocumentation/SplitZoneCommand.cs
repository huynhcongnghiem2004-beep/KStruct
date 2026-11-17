using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class SplitZoneCommand : IExternalCommand
    {
        internal const string ApplicationKey = "KStruct.SplitZone";

        private sealed class ZoneOptions
        {
            public string Name;
            public double TopOffsetMm;
            public double BottomOffsetMm;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                XYZ first = ui.Selection.PickPoint("Chọn góc thứ nhất vùng Split Zone trên mặt bằng");
                XYZ second = ui.Selection.PickPoint("Chọn góc đối diện vùng Split Zone");
                if (Math.Abs(first.X - second.X) <= doc.Application.ShortCurveTolerance ||
                    Math.Abs(first.Y - second.Y) <= doc.Application.ShortCurveTolerance)
                    throw new InvalidOperationException("Hai góc phải tạo thành vùng có chiều dài và chiều rộng lớn hơn 0.");
                ZoneOptions options = ShowOptions(data.Application.MainWindowHandle);
                if (options == null) return Result.Cancelled;

                double topOffset = UnitUtils.ConvertToInternalUnits(options.TopOffsetMm, UnitTypeId.Millimeters);
                double bottomOffset = UnitUtils.ConvertToInternalUnits(options.BottomOffsetMm, UnitTypeId.Millimeters);
                double minX = Math.Min(first.X, second.X), maxX = Math.Max(first.X, second.X);
                double minY = Math.Min(first.Y, second.Y), maxY = Math.Max(first.Y, second.Y);
                double baseZ = (first.Z + second.Z) / 2.0;
                double minZ = baseZ - bottomOffset, maxZ = baseZ + topOffset;
                if (maxZ - minZ <= doc.Application.ShortCurveTolerance)
                    throw new InvalidOperationException("Tổng chiều cao zone phải lớn hơn 0.");

                BoundingBoxXYZ zoneBounds = new BoundingBoxXYZ
                {
                    Min = new XYZ(minX, minY, minZ),
                    Max = new XYZ(maxX, maxY, maxZ)
                };
                List<Element> intersecting = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(element => element.Category != null && element.Category.CategoryType == CategoryType.Model &&
                        !element.ViewSpecific && !(element is RevitLinkInstance) &&
                        !(element is DirectShape shape && string.Equals(shape.ApplicationId, ApplicationKey, StringComparison.Ordinal)))
                    .Where(element => Intersects(zoneBounds, element.get_BoundingBox(null)))
                    .ToList();
                int reassigned = intersecting.Count(element =>
                {
                    Parameter parameter = element.LookupParameter("KStruct_Zone");
                    return parameter != null && parameter.HasValue && parameter.StorageType == StorageType.String &&
                        !string.Equals(parameter.AsString(), options.Name, StringComparison.OrdinalIgnoreCase);
                });
                if (reassigned > 0 && TaskDialog.Show("KStruct - Split Zone",
                    reassigned + " cấu kiện giao với vùng này đang mang zone khác. Gán lại chúng vào '" + options.Name + "'?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
                    return Result.Cancelled;

                ExternalDefinition definition = EnsureZoneDefinition(doc);
                CurveLoop profile = CreateRectangle(minX, minY, maxX, maxY, minZ);
                Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { profile }, XYZ.BasisZ, maxZ - minZ);
                string zoneId = options.Name.Trim();
                int assigned = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Tạo/cập nhật Split Zone"))
                {
                    tx.Start();
                    CategorySet categorySet = GetBoundCategories(doc, definition);
                    foreach (Element element in intersecting)
                        if (element.Category.AllowsBoundParameters) categorySet.Insert(element.Category);
                    if (categorySet.Size > 0)
                    {
                        InstanceBinding binding = doc.Application.Create.NewInstanceBinding(categorySet);
                        bool bound = doc.ParameterBindings.ReInsert(definition, binding, GroupTypeId.Data);
                        if (!bound) bound = doc.ParameterBindings.Insert(definition, binding, GroupTypeId.Data);
                        if (!bound) throw new InvalidOperationException("Không bind được tham số KStruct_Zone.");
                        foreach (Element element in intersecting)
                        {
                            Parameter parameter = element.get_Parameter(definition.GUID);
                            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String) continue;
                            if (parameter.Set(zoneId)) assigned++;
                        }
                    }

                    List<ElementId> oldZoneIds = new FilteredElementCollector(doc)
                        .OfClass(typeof(DirectShape)).Cast<DirectShape>()
                        .Where(shape => string.Equals(shape.ApplicationId, ApplicationKey, StringComparison.Ordinal) &&
                            string.Equals(shape.ApplicationDataId, zoneId, StringComparison.OrdinalIgnoreCase))
                        .Select(shape => shape.Id).ToList();
                    if (oldZoneIds.Count > 0) doc.Delete(oldZoneIds);
                    DirectShape zone = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    zone.ApplicationId = ApplicationKey;
                    zone.ApplicationDataId = zoneId;
                    zone.SetShape(new GeometryObject[] { solid });
                    Parameter comments = zone.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (comments != null && !comments.IsReadOnly) comments.Set("KStruct Split Zone: " + zoneId);
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận zone hoặc tham số KStruct_Zone.");
                }

                TaskDialog.Show("KStruct - Split Zone",
                    "Đã tạo/cập nhật zone '" + zoneId + "'." +
                    "\nCấu kiện giao vùng: " + intersecting.Count.ToString(CultureInfo.InvariantCulture) +
                    "\nĐã gán KStruct_Zone: " + assigned.ToString(CultureInfo.InvariantCulture) +
                    "\nKhối zone là DirectShape; cấu kiện trong Revit Link chưa được sao chép vào dự án.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Split Zone", ex.Message);
                return Result.Failed;
            }
        }

        private static bool Intersects(BoundingBoxXYZ a, BoundingBoxXYZ source)
        {
            if (source == null) return false;
            BoundingBoxXYZ b = ToWorldBounds(source);
            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
                a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
                a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
        }

        private static BoundingBoxXYZ ToWorldBounds(BoundingBoxXYZ box)
        {
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

        internal static CategorySet GetBoundCategories(Document doc, ExternalDefinition definition)
        {
            CategorySet categories = doc.Application.Create.NewCategorySet();
            DefinitionBindingMapIterator iterator = doc.ParameterBindings.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                ExternalDefinition existing = iterator.Key as ExternalDefinition;
                ElementBinding binding = iterator.Current as ElementBinding;
                if (existing == null || binding == null || existing.GUID != definition.GUID) continue;
                foreach (Category category in binding.Categories) categories.Insert(category);
                break;
            }
            return categories;
        }

        internal static ExternalDefinition EnsureZoneDefinition(Document doc)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KStruct");
            Directory.CreateDirectory(folder);
            string parameterFile = Path.Combine(folder, "KStructSharedParameters.txt");
            if (!File.Exists(parameterFile)) File.WriteAllText(parameterFile, "# KStruct shared parameters\r\n", Encoding.UTF8);
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;
            string originalPath = app.SharedParametersFilename;
            DefinitionFile file;
            try
            {
                app.SharedParametersFilename = parameterFile;
                file = app.OpenSharedParameterFile();
            }
            finally { app.SharedParametersFilename = originalPath; }
            if (file == null) throw new InvalidOperationException("Không mở được file tham số dùng chung KStruct.");
            DefinitionGroup group = file.Groups.get_Item("KStruct") ?? file.Groups.Create("KStruct");
            ExternalDefinition definition = group.Definitions.get_Item("KStruct_Zone") as ExternalDefinition;
            if (definition == null)
            {
                var options = new ExternalDefinitionCreationOptions("KStruct_Zone", SpecTypeId.String.Text)
                {
                    Description = "Tên Split Zone do KStruct gán cho cấu kiện giao vùng."
                };
                definition = group.Definitions.Create(options) as ExternalDefinition;
            }
            if (definition == null) throw new InvalidOperationException("Không tạo được định nghĩa tham số KStruct_Zone.");
            return definition;
        }

        private static CurveLoop CreateRectangle(double minX, double minY, double maxX, double maxY, double z)
        {
            XYZ p0 = new XYZ(minX, minY, z), p1 = new XYZ(maxX, minY, z);
            XYZ p2 = new XYZ(maxX, maxY, z), p3 = new XYZ(minX, maxY, z);
            return CurveLoop.Create(new Curve[]
            {
                Line.CreateBound(p0, p1), Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3), Line.CreateBound(p3, p0)
            });
        }

        private static ZoneOptions ShowOptions(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Split Zone - KStruct",
                Width = 390,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = "Tên zone:" });
            var name = new System.Windows.Controls.TextBox { Text = "Zone 01", MinWidth = 350, Margin = new Thickness(0, 3, 0, 8) };
            root.Children.Add(name);
            var top = AddNumber(root, "Chiều cao từ mặt phẳng chọn lên (mm):", "3000");
            var bottom = AddNumber(root, "Chiều sâu từ mặt phẳng chọn xuống (mm):", "0");
            root.Children.Add(new TextBlock
            {
                Text = "KStruct tạo một khối hộp zone, gán KStruct_Zone cho cấu kiện có bounding box giao vùng và cập nhật khối cũ cùng tên.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            ZoneOptions result = null;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Tạo/cập nhật", MinWidth = 100, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 75, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double topMm, bottomMm;
                string zoneName = (name.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(zoneName) || !TryNumber(top.Text, out topMm) ||
                    !TryNumber(bottom.Text, out bottomMm) || topMm < 0 || bottomMm < 0 || topMm + bottomMm <= 0)
                {
                    TaskDialog.Show("KStruct - Split Zone", "Nhập tên zone và chiều cao/dưới sâu không âm; tổng chiều cao phải lớn hơn 0.");
                    return;
                }
                result = new ZoneOptions { Name = zoneName, TopOffsetMm = topMm, BottomOffsetMm = bottomMm };
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
