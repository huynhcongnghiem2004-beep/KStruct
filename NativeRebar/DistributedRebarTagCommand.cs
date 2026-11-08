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

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class DistributedRebarTagCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class TagSide
        {
            public string Name { get; private set; }
            public double X { get; private set; }
            public double Y { get; private set; }
            public bool VerticalStack { get; private set; }

            public TagSide(string name, double x, double y, bool verticalStack)
            {
                Name = name;
                X = x;
                Y = y;
                VerticalStack = verticalStack;
            }

            public override string ToString() => Name;
        }

        private sealed class TagOptions
        {
            public ElementType Type;
            public TagSide Side;
            public double OffsetMm;
            public double SpacingMm;
        }

        private sealed class Item
        {
            public Rebar Bar;
            public XYZ Center;
            public double X;
            public double Y;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = doc.ActiveView;
            try
            {
                List<Rebar> rebars = ui.Selection.GetElementIds()
                    .Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (rebars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(
                        ObjectType.Element, new RebarFilter(),
                        "Chọn các Rebar cần dàn đều tag");
                    rebars = picked.Select(r => doc.GetElement(r)).OfType<Rebar>().Distinct().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;

                List<ElementType> types = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_RebarTags)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                if (types.Count == 0)
                    throw new InvalidOperationException("Project chưa có Rebar Tag family/type. Hãy nạp tag trước.");

                TagOptions options = ChooseOptions(data.Application.MainWindowHandle, types);
                if (options == null) return Result.Cancelled;

                List<Item> items = rebars.Select(bar => MakeItem(bar, view))
                    .Where(item => item != null)
                    .ToList();
                if (items.Count == 0)
                    throw new InvalidOperationException("Không đọc được vị trí Rebar trong view hiện tại.");

                double scale = Math.Max(1, view.Scale);
                double offset = UnitUtils.ConvertToInternalUnits(options.OffsetMm, UnitTypeId.Millimeters) * scale;
                double spacing = UnitUtils.ConvertToInternalUnits(options.SpacingMm, UnitTypeId.Millimeters) * scale;
                double minX = items.Min(item => item.MinX);
                double maxX = items.Max(item => item.MaxX);
                double minY = items.Min(item => item.MinY);
                double maxY = items.Max(item => item.MaxY);
                double midX = (minX + maxX) * 0.5;
                double midY = (minY + maxY) * 0.5;

                var alreadyTagged = new HashSet<ElementId>();
                foreach (IndependentTag existingTag in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                {
                    try
                    {
                        foreach (ElementId taggedId in existingTag.GetTaggedLocalElementIds())
                            alreadyTagged.Add(taggedId);
                    }
                    catch { }
                }
                int created = 0;
                int skipped = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Dàn đều tag Rebar"))
                {
                    tx.Start();
                    List<Item> pending = items.Where(item => !alreadyTagged.Contains(item.Bar.Id)).ToList();
                    skipped = items.Count - pending.Count;
                    foreach (Item item in pending)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                IndependentTag tag = IndependentTag.Create(
                                    doc, options.Type.Id, view.Id, new Reference(item.Bar),
                                    true, TagOrientation.Horizontal, item.Center);
                                if (tag == null) throw new InvalidOperationException("Revit không tạo được tag.");

                                XYZ head;
                                if (options.Side.VerticalStack)
                                {
                                    double x = options.Side.X > 0 ? maxX + offset : minX - offset;
                                    double y = midY + (pending.Count - 1) * spacing * 0.5 - created * spacing;
                                    head = item.Center + view.RightDirection * (x - item.X) +
                                        view.UpDirection * (y - item.Y);
                                }
                                else
                                {
                                    double y = options.Side.Y > 0 ? maxY + offset : minY - offset;
                                    double x = midX - (pending.Count - 1) * spacing * 0.5 + created * spacing;
                                    head = item.Center + view.RightDirection * (x - item.X) +
                                        view.UpDirection * (y - item.Y);
                                }
                                tag.TagHeadPosition = head;
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không lưu được tag.");
                                created++;
                            }
                            catch
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped++;
                            }
                        }
                    }

                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các tag.");
                }

                TaskDialog.Show("KStruct - Tag thép phân bố",
                    "Đã tạo " + created + " tag; bỏ qua " + skipped + " thanh (đã có tag hoặc Revit không tạo được)." +
                    "\nKhoảng cách đặt trên giấy: " + options.SpacingMm.ToString("0.#", CultureInfo.CurrentCulture) + " mm.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tag thép phân bố", ex.Message);
                return Result.Failed;
            }
        }

        private static Item MakeItem(Rebar bar, View view)
        {
            BoundingBoxXYZ box = bar.get_BoundingBox(view) ?? bar.get_BoundingBox(null);
            if (box == null) return null;
            XYZ center = box.Transform.OfPoint((box.Min + box.Max) * 0.5);
            XYZ[] corners =
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            };
            double[] xs = corners.Select(p => box.Transform.OfPoint(p).DotProduct(view.RightDirection)).ToArray();
            double[] ys = corners.Select(p => box.Transform.OfPoint(p).DotProduct(view.UpDirection)).ToArray();
            return new Item
            {
                Bar = bar,
                Center = center,
                X = center.DotProduct(view.RightDirection),
                Y = center.DotProduct(view.UpDirection),
                MinX = xs.Min(), MaxX = xs.Max(), MinY = ys.Min(), MaxY = ys.Max()
            };
        }

        private static TagOptions ChooseOptions(IntPtr owner, IList<ElementType> types)
        {
            var sides = new List<TagSide>
            {
                new TagSide("Phải", 1, 0, true),
                new TagSide("Trái", -1, 0, true),
                new TagSide("Trên", 0, 1, false),
                new TagSide("Dưới", 0, -1, false)
            };
            var window = new Window
            {
                Title = "Dàn đều tag cốt thép - KStruct",
                Width = 410,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = "Kiểu Rebar Tag:" });
            var typeCombo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = types,
                DisplayMemberPath = "Name",
                SelectedItem = types.FirstOrDefault(),
                MinWidth = 370,
                Margin = new Thickness(0, 3, 0, 9)
            };
            root.Children.Add(typeCombo);

            root.Children.Add(new TextBlock { Text = "Dàn tag về phía:" });
            var sideCombo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = sides,
                SelectedItem = sides[0],
                MinWidth = 370,
                Margin = new Thickness(0, 3, 0, 9)
            };
            root.Children.Add(sideCombo);

            root.Children.Add(new TextBlock { Text = "Độ lệch khỏi cấu kiện (mm trên giấy):" });
            var offsetBox = new System.Windows.Controls.TextBox
            {
                Text = "10",
                MinWidth = 370,
                Margin = new Thickness(0, 3, 0, 9)
            };
            root.Children.Add(offsetBox);

            root.Children.Add(new TextBlock { Text = "Khoảng cách tối thiểu giữa các đầu tag (mm trên giấy):" });
            var spacingBox = new System.Windows.Controls.TextBox
            {
                Text = "8",
                MinWidth = 370,
                Margin = new Thickness(0, 3, 0, 10)
            };
            root.Children.Add(spacingBox);

            TagOptions result = null;
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var apply = new Button
            {
                Content = "Tạo tag",
                MinWidth = 85,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancel = new Button { Content = "Hủy", MinWidth = 75, IsCancel = true };
            apply.Click += (sender, args) =>
            {
                double offsetMm;
                double spacingMm;
                if (typeCombo.SelectedItem == null || sideCombo.SelectedItem == null ||
                    !double.TryParse(offsetBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out offsetMm) ||
                    !double.TryParse(spacingBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out spacingMm) ||
                    offsetMm < 0 || spacingMm <= 0)
                {
                    TaskDialog.Show("KStruct - Tag thép phân bố",
                        "Hãy chọn kiểu tag; độ lệch phải không âm và khoảng cách phải lớn hơn 0.");
                    return;
                }
                result = new TagOptions
                {
                    Type = (ElementType)typeCombo.SelectedItem,
                    Side = (TagSide)sideCombo.SelectedItem,
                    OffsetMm = offsetMm,
                    SpacingMm = spacingMm
                };
                window.DialogResult = true;
            };
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }
    }
}