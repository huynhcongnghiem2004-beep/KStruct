using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class SelectSlabRebarByLocationCommand : IExternalCommand
    {
        private sealed class Options
        {
            public string Layer;
            public string Direction;
        }

        private sealed class RebarLocation
        {
            public Rebar Bar;
            public string Layer;
            public string Direction;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                HashSet<ElementId> selectedFloorIds = ui.Selection.GetElementIds().Where(id => doc.GetElement(id) is Floor).ToHashSet();
                List<Rebar> selectedBars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().ToList();
                IEnumerable<Rebar> candidateBars = selectedBars.Count > 0
                    ? selectedBars
                    : new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType().Cast<Rebar>();
                List<RebarLocation> locations = candidateBars.Select(bar => Classify(doc, bar))
                    .Where(location => location != null && (selectedFloorIds.Count == 0 || selectedFloorIds.Contains(location.Bar.GetHostId())))
                    .ToList();
                if (locations.Count == 0)
                {
                    TaskDialog.Show("KStruct - Vị trí thép sàn", "Không tìm thấy Rebar được host bởi Floor. Chọn Floor để giới hạn phạm vi hoặc chọn Rebar trực tiếp.");
                    return Result.Cancelled;
                }

                Options options = ShowOptions(data.Application.MainWindowHandle);
                if (options == null) return Result.Cancelled;
                List<ElementId> matches = locations.Where(x =>
                    (options.Layer == "Tất cả" || x.Layer == options.Layer) &&
                    (options.Direction == "Tất cả" || x.Direction == options.Direction))
                    .Select(x => x.Bar.Id).Distinct().ToList();
                ui.Selection.SetElementIds(matches);
                TaskDialog.Show("KStruct - Vị trí thép sàn", "Đã chọn " + matches.Count + " Rebar trong " +
                    (selectedFloorIds.Count > 0 ? "phạm vi Floor đang chọn" : "toàn bộ project") + ".");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Vị trí thép sàn", ex.Message);
                return Result.Failed;
            }
        }

        private static RebarLocation Classify(Document doc, Rebar bar)
        {
            Floor floor = doc.GetElement(bar.GetHostId()) as Floor;
            if (floor == null) return null;
            IList<Curve> curves;
            try { curves = bar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeAllMultiplanarCurves, 0); }
            catch { return null; }
            if (curves == null || curves.Count == 0) return null;

            List<XYZ> points = curves.SelectMany(c => c.Tessellate()).ToList();
            if (points.Count < 2) return null;
            double averageZ = points.Average(p => p.Z);
            List<double> topZ = FaceElevations(doc, floor, true);
            List<double> bottomZ = FaceElevations(doc, floor, false);
            if (topZ.Count == 0 || bottomZ.Count == 0) return null;
            double distanceTop = Math.Abs(averageZ - topZ.Average());
            double distanceBottom = Math.Abs(averageZ - bottomZ.Average());
            string layer = distanceTop <= distanceBottom ? "Trên" : "Dưới";

            double longest = 0;
            XYZ mainDirection = null;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                XYZ vector = points[i + 1] - points[i];
                double horizontalLength = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
                if (horizontalLength > longest)
                {
                    longest = horizontalLength;
                    mainDirection = new XYZ(vector.X / horizontalLength, vector.Y / horizontalLength, 0);
                }
            }
            if (mainDirection == null) return null;
            string direction = Math.Abs(mainDirection.X) >= Math.Abs(mainDirection.Y) ? "X" : "Y";
            return new RebarLocation { Bar = bar, Layer = layer, Direction = direction };
        }

        private static List<double> FaceElevations(Document doc, Floor floor, bool top)
        {
            IList<Reference> faces;
            try { faces = top ? HostObjectUtils.GetTopFaces(floor) : HostObjectUtils.GetBottomFaces(floor); }
            catch { return new List<double>(); }
            var result = new List<double>();
            foreach (Reference reference in faces)
            {
                PlanarFace face = floor.GetGeometryObjectFromReference(reference) as PlanarFace;
                if (face == null) continue;
                XYZ normal = face.FaceNormal.Normalize();
                if (Math.Abs(normal.DotProduct(XYZ.BasisZ)) < 0.95) continue;
                result.Add(face.Origin.Z);
            }
            return result;
        }

        private static Options ShowOptions(IntPtr owner)
        {
            Options result = null;
            var window = new Window
            {
                Title = "Chọn thép sàn theo vị trí - KStruct", Width = 390, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = "Lớp thép:" });
            var layerCombo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = new[] { "Tất cả", "Trên", "Dưới" }, SelectedIndex = 0,
                MinWidth = 350, MinHeight = 25, Margin = new Thickness(0, 3, 0, 8)
            };
            root.Children.Add(layerCombo);
            root.Children.Add(new TextBlock { Text = "Phương thanh theo trục global:" });
            var directionCombo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = new[] { "Tất cả", "X", "Y" }, SelectedIndex = 0,
                MinWidth = 350, MinHeight = 25, Margin = new Thickness(0, 3, 0, 10)
            };
            root.Children.Add(directionCombo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var select = new Button { Content = "Chọn Rebar", MinWidth = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 72, IsCancel = true };
            select.Click += (s, e) =>
            {
                result = new Options { Layer = layerCombo.SelectedItem as string, Direction = directionCombo.SelectedItem as string };
                window.DialogResult = true;
            };
            buttons.Children.Add(select);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? result : null;
        }
    }
}
