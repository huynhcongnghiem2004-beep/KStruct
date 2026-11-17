using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class TagSlabRebarSetsCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class TagTypeChoice
        {
            public ElementType Type { get; }
            public string Label => Type.Name;
            public TagTypeChoice(ElementType type) { Type = type; }
        }

        private sealed class TagOptions
        {
            public ElementType Type;
            public double OffsetMm;
        }

        private sealed class Extent
        {
            public Rebar Bar;
            public XYZ Center;
            public XYZ LineStart;
            public XYZ LineEnd;
            public XYZ TagHead;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            ViewPlan view = doc.ActiveView as ViewPlan;
            try
            {
                if (view == null) throw new InvalidOperationException("Lệnh này chỉ chạy trong mặt bằng.");
                List<Rebar> bars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                HashSet<ElementId> selectedFloors = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Floor>()
                    .Select(floor => floor.Id).ToHashSet();
                if (bars.Count == 0 && selectedFloors.Count > 0)
                    bars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                        .Cast<Rebar>().Where(bar => selectedFloors.Contains(bar.GetHostId())).ToList();
                if (bars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(),
                        "Chọn các bộ Rebar sàn cần gắn tag theo phạm vi rải");
                    bars = picked.Select(reference => doc.GetElement(reference)).OfType<Rebar>().Distinct().ToList();
                }
                bars = bars.Where(bar => doc.GetElement(bar.GetHostId()) is Floor && bar.NumberOfBarPositions > 1).ToList();
                if (bars.Count == 0) throw new InvalidOperationException("Không tìm thấy Rebar set sàn có từ 2 vị trí trở lên.");

                List<ElementType> tagTypes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RebarTags)
                    .WhereElementIsElementType().Cast<ElementType>().OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (tagTypes.Count == 0) throw new InvalidOperationException("Project chưa có Rebar Tag family/type.");
                TagOptions options = ChooseOptions(data.Application.MainWindowHandle, tagTypes);
                if (options == null) return Result.Cancelled;

                var existingTagged = new HashSet<ElementId>();
                foreach (IndependentTag tag in new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                    try { foreach (ElementId id in tag.GetTaggedLocalElementIds()) existingTagged.Add(id); } catch { }

                List<Extent> extents = bars.Where(bar => !existingTagged.Contains(bar.Id)).Select(bar => MakeExtent(bar, view, options.OffsetMm))
                    .Where(extent => extent != null).ToList();
                if (extents.Count == 0) throw new InvalidOperationException("Các bộ thép đã có tag hoặc không có phạm vi hình học trong view.");

                int created = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Tag bộ thép sàn"))
                {
                    tx.Start();
                    foreach (Extent extent in extents)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                doc.Create.NewDetailCurve(view, Line.CreateBound(extent.LineStart, extent.LineEnd));
                                IndependentTag tag = IndependentTag.Create(doc, options.Type.Id, view.Id,
                                    new Reference(extent.Bar), false, TagOrientation.Horizontal, extent.TagHead);
                                if (tag == null) throw new InvalidOperationException("Revit không tạo được tag.");
                                if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Không lưu được tag và đường phạm vi rải.");
                                created++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add(extent.Bar.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (created == 0)
                    {
                        tx.RollBack();
                        throw new InvalidOperationException("Revit không tạo được tag/đường phạm vi cho bộ thép nào.");
                    }
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận các tag.");
                }

                string report = "Đã tạo " + created + " tag bộ thép và đường phạm vi rải.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " bộ: " + string.Join(", ", skipped.Take(6));
                TaskDialog.Show("KStruct - Tag thép sàn", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tag thép sàn", ex.Message);
                return Result.Failed;
            }
        }

        private static Extent MakeExtent(Rebar bar, ViewPlan view, double offsetMm)
        {
            BoundingBoxXYZ box = bar.get_BoundingBox(view) ?? bar.get_BoundingBox(null);
            if (box == null) return null;
            XYZ[] corners =
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            };
            List<XYZ> points = corners.Select(box.Transform.OfPoint).ToList();
            XYZ right = view.RightDirection.Normalize(), up = view.UpDirection.Normalize();
            double minX = points.Min(point => point.DotProduct(right)), maxX = points.Max(point => point.DotProduct(right));
            double minY = points.Min(point => point.DotProduct(up)), maxY = points.Max(point => point.DotProduct(up));
            if (maxX - minX <= 1e-6 || maxY - minY <= 1e-6) return null;
            XYZ origin = view.Origin;
            XYZ center = origin + right * ((minX + maxX) / 2) + up * ((minY + maxY) / 2);
            // The distribution axis is normally perpendicular to the bar length, so it is the shorter plan extent.
            XYZ start, end;
            if (maxX - minX <= maxY - minY)
            {
                start = origin + right * minX + up * ((minY + maxY) / 2);
                end = origin + right * maxX + up * ((minY + maxY) / 2);
            }
            else
            {
                start = origin + right * ((minX + maxX) / 2) + up * minY;
                end = origin + right * ((minX + maxX) / 2) + up * maxY;
            }
            double paperOffset = UnitUtils.ConvertToInternalUnits(offsetMm, UnitTypeId.Millimeters) * Math.Max(1, view.Scale);
            return new Extent { Bar = bar, Center = center, LineStart = start, LineEnd = end, TagHead = center + up * paperOffset };
        }

        private static TagOptions ChooseOptions(IntPtr owner, IList<ElementType> types)
        {
            var window = new Window
            {
                Title = "Tag bộ thép sàn - KStruct", Width = 420, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = "Kiểu tag Rebar:" });
            var combo = new WpfComboBox { ItemsSource = types.Select(type => new TagTypeChoice(type)).ToList(), DisplayMemberPath = "Label", SelectedIndex = 0, MinWidth = 380, Margin = new Thickness(0, 4, 0, 10) };
            root.Children.Add(combo);
            root.Children.Add(new TextBlock { Text = "Khoảng cách tag khỏi bộ thép (mm trên giấy):" });
            var offset = new WpfTextBox { Text = "10", MinWidth = 380, Margin = new Thickness(0, 4, 0, 10) };
            root.Children.Add(offset);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Tạo tag", MinWidth = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            TagOptions result = null;
            ok.Click += (sender, args) =>
            {
                double millimeters;
                TagTypeChoice choice = combo.SelectedItem as TagTypeChoice;
                if (choice == null || !double.TryParse(offset.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out millimeters) || millimeters < 0)
                {
                    MessageBox.Show(window, "Chọn tag và nhập khoảng cách hợp lệ.", "KStruct - Tag thép sàn", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = new TagOptions { Type = choice.Type, OffsetMm = millimeters };
                window.DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons); window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }
    }
}
