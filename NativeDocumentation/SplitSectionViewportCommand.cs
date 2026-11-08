using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class SplitSectionViewportCommand : IExternalCommand
    {
        private sealed class ViewportFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Viewport;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class Options
        {
            public int Segments;
            public double OverlapMm;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                ViewSheet sheet = doc.ActiveView as ViewSheet;
                if (sheet == null) throw new InvalidOperationException("Mở sheet cần chia viewport rồi chạy lệnh.");
                List<Viewport> selected = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Viewport>().Distinct().ToList();
                if (selected.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new ViewportFilter(),
                        "Chọn một hoặc nhiều viewport section cần chia trên sheet");
                    selected = picked.Select(reference => doc.GetElement(reference)).OfType<Viewport>().Distinct().ToList();
                }
                selected = selected.Where(viewport => viewport.SheetId == sheet.Id).ToList();
                if (selected.Count == 0) throw new InvalidOperationException("Không có viewport nào được chọn trên sheet hiện tại.");
                foreach (Viewport viewport in selected)
                    if (!(doc.GetElement(viewport.ViewId) is ViewSection))
                        throw new InvalidOperationException("Lệnh chỉ hỗ trợ ViewSection/section elevation; viewport không phải section: " + viewport.Id.Value + ".");

                Options options = ChooseOptions(data.Application.MainWindowHandle);
                if (options == null) return Result.Cancelled;
                int split = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Chia section viewport"))
                {
                    tx.Start();
                    foreach (Viewport viewport in selected)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                SplitOne(doc, sheet, viewport, options);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận các section view đã chia.");
                                split++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Viewport " + viewport.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (split == 0)
                    {
                        tx.RollBack();
                        throw new InvalidOperationException("Không chia được viewport nào. " + string.Join("\n", skipped.Take(6)));
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không lưu được các viewport mới.");
                }
                string report = "Đã chia " + split + "/" + selected.Count + " viewport thành " + options.Segments + " section view mỗi viewport.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + ":\n" + string.Join("\n", skipped.Take(6));
                TaskDialog.Show("KStruct - Chia section view", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Chia section view", ex.Message);
                return Result.Failed;
            }
        }

        private static void SplitOne(Document doc, ViewSheet sheet, Viewport originalViewport, Options options)
        {
            ViewSection originalView = doc.GetElement(originalViewport.ViewId) as ViewSection;
            if (originalView == null || !originalView.CropBoxActive)
                throw new InvalidOperationException("Section view phải có crop box đang bật.");
            BoundingBoxXYZ sourceCrop = originalView.CropBox;
            double minX = sourceCrop.Min.X, maxX = sourceCrop.Max.X;
            double width = maxX - minX;
            if (width <= doc.Application.ShortCurveTolerance * options.Segments)
                throw new InvalidOperationException("Crop width quá nhỏ để chia thành " + options.Segments + " phần.");

            Outline originalOutline = originalViewport.GetBoxOutline();
            double viewportWidth = originalOutline.MaximumPoint.X - originalOutline.MinimumPoint.X;
            if (viewportWidth <= 1e-6) throw new InvalidOperationException("Không đọc được kích thước viewport trên sheet.");
            XYZ oldCenter = originalViewport.GetBoxCenter();
            double overlap = UnitUtils.ConvertToInternalUnits(options.OverlapMm, UnitTypeId.Millimeters);
            double paperOverlap = overlap / Math.Max(1, originalView.Scale);
            double layoutStep = viewportWidth / options.Segments + paperOverlap;
            var viewNames = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View))
                .Cast<View>().Select(view => view.Name), StringComparer.OrdinalIgnoreCase);

            var views = new List<ViewSection> { originalView };
            for (int i = 1; i < options.Segments; i++)
            {
                ElementId duplicateId = originalView.Duplicate(ViewDuplicateOption.Duplicate);
                ViewSection duplicate = doc.GetElement(duplicateId) as ViewSection;
                if (duplicate == null) throw new InvalidOperationException("Revit không sao chép được section view.");
                duplicate.Name = UniqueViewName(originalView.Name + " - Phần " + (i + 1), viewNames);
                viewNames.Add(duplicate.Name);
                views.Add(duplicate);
            }
            originalView.Name = UniqueViewName(originalView.Name + " - Phần 1", viewNames);
            viewNames.Add(originalView.Name);

            for (int i = 0; i < options.Segments; i++)
            {
                double stepStart = minX + width * i / options.Segments;
                double stepEnd = minX + width * (i + 1) / options.Segments;
                double segmentMin = i == 0 ? stepStart : stepStart - overlap / 2.0;
                double segmentMax = i == options.Segments - 1 ? stepEnd : stepEnd + overlap / 2.0;
                BoundingBoxXYZ crop = views[i].CropBox;
                crop.Min = new XYZ(Math.Max(minX, segmentMin), sourceCrop.Min.Y, sourceCrop.Min.Z);
                crop.Max = new XYZ(Math.Min(maxX, segmentMax), sourceCrop.Max.Y, sourceCrop.Max.Z);
                views[i].CropBox = crop;
                views[i].CropBoxActive = true;
                views[i].CropBoxVisible = false;
            }

            doc.Regenerate();
            for (int i = 0; i < options.Segments; i++)
            {
                XYZ center = new XYZ(oldCenter.X + (i - (options.Segments - 1) / 2.0) * layoutStep, oldCenter.Y, oldCenter.Z);
                if (i == 0)
                {
                    originalViewport.SetBoxCenter(center);
                    continue;
                }
                if (!Viewport.CanAddViewToSheet(doc, sheet.Id, views[i].Id))
                    throw new InvalidOperationException("Không thể đặt section view Phần " + (i + 1) + " lên sheet hiện tại.");
                Viewport viewport = Viewport.Create(doc, sheet.Id, views[i].Id, center);
                ElementId viewportTypeId = originalViewport.GetTypeId();
                if (viewportTypeId != ElementId.InvalidElementId && viewport.GetTypeId() != viewportTypeId)
                    viewport.ChangeTypeId(viewportTypeId);
                viewport.SetBoxCenter(center);
            }
        }

        private static string UniqueViewName(string baseName, ISet<string> existing)
        {
            if (!existing.Contains(baseName)) return baseName;
            for (int index = 2; ; index++)
            {
                string candidate = baseName + " (" + index + ")";
                if (!existing.Contains(candidate)) return candidate;
            }
        }

        private static Options ChooseOptions(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Chia section viewport - KStruct", Width = 430, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = "Số phần theo chiều ngang:" });
            var count = new System.Windows.Controls.ComboBox { ItemsSource = new[] { 2, 3, 4, 5, 6 }, SelectedIndex = 1, MinWidth = 390, Margin = new Thickness(0, 4, 0, 10) };
            root.Children.Add(count);
            root.Children.Add(new TextBlock { Text = "Độ chồng giữa các crop (mm):" });
            var overlap = new WpfTextBox { Text = "50", MinWidth = 390, Margin = new Thickness(0, 4, 0, 10) };
            root.Children.Add(overlap);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Chia viewport", MinWidth = 105, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            Options result = null;
            ok.Click += (sender, args) =>
            {
                int segments = count.SelectedItem is int value ? value : 0;
                double overlapMm;
                if (segments < 2 || !double.TryParse(overlap.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out overlapMm) || overlapMm < 0 || overlapMm > 1000)
                {
                    MessageBox.Show(window, "Số phần phải từ 2 đến 6; độ chồng từ 0 đến 1000 mm.", "KStruct - Chia section view",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = new Options { Segments = segments, OverlapMm = overlapMm };
                window.DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons); window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }
    }
}
