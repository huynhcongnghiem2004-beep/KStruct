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
    public sealed class DimensionPileCentersCommand : IExternalCommand
    {
        private sealed class FoundationFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance instance &&
                instance.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
        private sealed class Center { public Reference Reference; public XYZ Point; }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = ui.ActiveView;
            try
            {
                if (!(view is ViewPlan) || view.IsTemplate)
                    throw new InvalidOperationException("Mở mặt bằng móng trước khi tạo kích thước tim cọc.");
                List<FamilyInstance> piles = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>().Where(IsFoundation).Distinct().ToList();
                if (piles.Count < 2)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FoundationFilter(),
                        "Chọn ít nhất hai family cọc/móng có Center Reference");
                    piles = picked.Select(reference => doc.GetElement(reference)).OfType<FamilyInstance>()
                        .Where(IsFoundation).Distinct().ToList();
                }
                if (piles.Count < 2) return Result.Cancelled;
                XYZ axis = ChooseAxis(data.Application.MainWindowHandle);
                if (axis == null) return Result.Cancelled;
                XYZ viewNormal = view.ViewDirection.Normalize();
                XYZ normal = viewNormal.CrossProduct(axis).Normalize();
                XYZ pickedPoint = ui.Selection.PickPoint("Chọn vị trí đặt chuỗi kích thước tim cọc");
                double offset = pickedPoint.DotProduct(normal);
                double elevation = pickedPoint.DotProduct(viewNormal);
                var candidates = new[] { FamilyInstanceReferenceType.CenterLeftRight, FamilyInstanceReferenceType.CenterFrontBack };
                string lastFailure = null;
                int dimensionCount = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Dimension pile centers"))
                {
                    tx.Start();
                    foreach (FamilyInstanceReferenceType referenceType in candidates)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<Center> centers = new List<Center>();
                                foreach (FamilyInstance pile in piles)
                                {
                                    LocationPoint location = pile.Location as LocationPoint;
                                    if (location == null) throw new InvalidOperationException("Móng/cọc " + pile.Id.Value + " không có LocationPoint.");
                                    Reference reference = pile.GetReferences(referenceType).FirstOrDefault();
                                    if (reference == null) throw new InvalidOperationException("Family " + pile.Symbol.FamilyName + " thiếu reference tâm " + referenceType + ".");
                                    centers.Add(new Center { Reference = reference, Point = location.Point });
                                }
                                centers = centers.OrderBy(center => center.Point.DotProduct(axis)).ToList();
                                double tolerance = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Millimeters);
                                List<Center> distinct = centers.Where((center, index) => index == 0 ||
                                    Math.Abs(center.Point.DotProduct(axis) - centers[index - 1].Point.DotProduct(axis)) > tolerance).ToList();
                                if (distinct.Count < 2) throw new InvalidOperationException("Các tim cọc trùng nhau trên phương đã chọn.");
                                ReferenceArray references = new ReferenceArray();
                                foreach (Center center in distinct) references.Append(center.Reference);
                                double min = distinct.First().Point.DotProduct(axis), max = distinct.Last().Point.DotProduct(axis);
                                XYZ start = axis.Multiply(min) + normal.Multiply(offset) + viewNormal.Multiply(elevation);
                                XYZ end = axis.Multiply(max) + normal.Multiply(offset) + viewNormal.Multiply(elevation);
                                if (doc.Create.NewDimension(view, Line.CreateBound(start, end), references) == null)
                                    throw new InvalidOperationException("Revit không tạo được dimension cho reference tâm.");
                                if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận dimension.");
                                dimensionCount = distinct.Count;
                                break;
                            }
                            catch (Exception ex)
                            {
                                lastFailure = ex.Message;
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                            }
                        }
                    }
                    if (dimensionCount > 0)
                    {
                        if (tx.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Revit không chấp nhận kích thước tim cọc.");
                        TaskDialog.Show("KStruct - Dim cọc", "Đã tạo chuỗi kích thước tim cho " + dimensionCount + " cọc/móng.");
                        return Result.Succeeded;
                    }
                    tx.RollBack();
                }
                throw new InvalidOperationException("Không tạo được dimension tim. Family cần reference Center Left/Right hoặc Center Front/Back tương thích với view. " + lastFailure);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Dim cọc", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsFoundation(FamilyInstance instance) => instance?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation;

        private static XYZ ChooseAxis(IntPtr owner)
        {
            var dialog = new System.Windows.Window
            {
                Title = "Dim tim cọc", Width = 330, Height = 150, ResizeMode = System.Windows.ResizeMode.NoResize,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                WindowStyle = System.Windows.WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12) };
            root.Children.Add(new System.Windows.Controls.TextBlock { Text = "Chọn phương chuỗi kích thước:" });
            var axis = new System.Windows.Controls.ComboBox { ItemsSource = new[] { "X toàn cục", "Y toàn cục" }, SelectedIndex = 0, Margin = new System.Windows.Thickness(0, 6, 0, 8) };
            root.Children.Add(axis);
            var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var ok = new System.Windows.Controls.Button { Content = "Tiếp tục", Width = 82, IsDefault = true, Margin = new System.Windows.Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) => dialog.DialogResult = true;
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
            dialog.Content = root;
            new System.Windows.Interop.WindowInteropHelper(dialog).Owner = owner;
            if (dialog.ShowDialog() != true) return null;
            return axis.SelectedIndex == 0 ? XYZ.BasisX : XYZ.BasisY;
        }
    }
}
