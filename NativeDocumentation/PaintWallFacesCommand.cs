using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal sealed class WallFaceFilter : ISelectionFilter
    {
        private readonly Document _doc;
        public WallFaceFilter(Document doc) { _doc = doc; }
        public bool AllowElement(Element element) => element is Wall;
        public bool AllowReference(Reference reference, XYZ position)
        {
            try
            {
                return _doc.GetElement(reference.ElementId) is Wall &&
                    _doc.GetElement(reference.ElementId)?.GetGeometryObjectFromReference(reference) is PlanarFace;
            }
            catch { return false; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class PaintWallFacesCommand : IExternalCommand
    {
        private sealed class MaterialChoice
        {
            public Material Material { get; }
            public string Label { get; }
            public MaterialChoice(Material material)
            {
                Material = material;
                Label = material.Name;
            }
            public override string ToString() => Label;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                IList<Reference> faces = ui.Selection.PickObjects(ObjectType.Face, new WallFaceFilter(doc),
                    "Chọn một hoặc nhiều mặt phẳng của tường để tô vật liệu");
                if (faces == null || faces.Count == 0) return Result.Cancelled;
                List<Material> materials = new FilteredElementCollector(doc).OfClass(typeof(Material))
                    .Cast<Material>().OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (materials.Count == 0) throw new InvalidOperationException("Project chưa có Material.");
                Material material = ChooseMaterial(data.Application.MainWindowHandle, materials);
                if (material == null) return Result.Cancelled;

                int painted = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Tô vật liệu mặt tường"))
                {
                    tx.Start();
                    foreach (Reference reference in faces)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                Element wall = doc.GetElement(reference.ElementId);
                                Face face = wall?.GetGeometryObjectFromReference(reference) as Face;
                                if (!(wall is Wall) || !(face is PlanarFace))
                                    throw new InvalidOperationException("Mặt đã chọn không còn là mặt phẳng của tường.");
                                doc.Paint(wall.Id, face, material.Id);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không hoàn tất thao tác paint.");
                                painted++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Element " + reference.ElementId.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (painted == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Tô mặt tường", "Không tô được mặt nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi paint.");
                }
                string report = "Đã tô " + painted + "/" + faces.Count + " mặt tường bằng Material '" + material.Name + "'.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " mặt:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Tô mặt tường", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tô mặt tường", ex.Message);
                return Result.Failed;
            }
        }

        private static Material ChooseMaterial(IntPtr owner, IList<Material> materials)
        {
            var choices = materials.Select(m => new MaterialChoice(m)).ToList();
            var window = new Window
            {
                Title = "Tô mặt tường - KStruct",
                Width = 420,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Chọn Material để tô lên các mặt đã chọn. Cấu tạo WallType không bị thay đổi.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var combo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = choices,
                SelectedIndex = 0,
                MinWidth = 380,
                DisplayMemberPath = "Label"
            };
            root.Children.Add(combo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new System.Windows.Controls.Button { Content = "Tô mặt", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) => { if (combo.SelectedItem != null) window.DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? (combo.SelectedItem as MaterialChoice)?.Material : null;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class ClearWallFacePaintCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                IList<Reference> faces = ui.Selection.PickObjects(ObjectType.Face, new WallFaceFilter(doc),
                    "Chọn các mặt tường cần xóa paint");
                int cleared = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Xóa paint mặt tường"))
                {
                    tx.Start();
                    foreach (Reference reference in faces)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                Element wall = doc.GetElement(reference.ElementId);
                                Face face = wall?.GetGeometryObjectFromReference(reference) as Face;
                                if (!(wall is Wall) || !(face is PlanarFace))
                                    throw new InvalidOperationException("Mặt đã chọn không còn là mặt phẳng của tường.");
                                if (doc.IsPainted(wall.Id, face))
                                {
                                    doc.RemovePaint(wall.Id, face);
                                    cleared++;
                                }
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không hoàn tất thao tác xóa paint.");
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Element " + reference.ElementId.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (cleared == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Xóa paint", skipped.Count == 0
                            ? "Các mặt đã chọn chưa được tô paint."
                            : "Không xóa được paint.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi paint.");
                }
                string report = "Đã xóa paint trên " + cleared + " mặt tường.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " mặt:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Xóa paint", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Xóa paint", ex.Message);
                return Result.Failed;
            }
        }
    }
}
