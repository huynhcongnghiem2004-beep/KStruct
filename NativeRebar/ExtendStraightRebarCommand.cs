using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ExtendStraightRebarCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class ExtendOptions
        {
            public int End;
            public double LengthMm;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Rebar> selected = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (selected.Count == 0)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new RebarFilter(), "Chọn một thanh Rebar thẳng cần kéo dài");
                    selected.Add(doc.GetElement(picked) as Rebar);
                }
                if (selected.Count != 1)
                {
                    TaskDialog.Show("KStruct - Kéo dài Rebar", "Lệnh này xử lý một Rebar mỗi lần. Hãy chỉ chọn một thanh.");
                    return Result.Cancelled;
                }
                Rebar source = selected[0];
                if (source == null || source.IsRebarFreeForm() || source.NumberOfBarPositions != 1)
                {
                    TaskDialog.Show("KStruct - Kéo dài Rebar", "Hiện chỉ hỗ trợ một thanh đơn, không phải free-form hoặc Rebar set.");
                    return Result.Cancelled;
                }
                IList<Curve> curves = source.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeAllMultiplanarCurves, 0);
                if (curves == null || curves.Count != 1 || !(curves[0] is Line axis))
                {
                    TaskDialog.Show("KStruct - Kéo dài Rebar", "Hiện chỉ hỗ trợ thanh có một đoạn thẳng.");
                    return Result.Cancelled;
                }
                ExtendOptions options = AskOptions(data.Application.MainWindowHandle);
                if (options == null) return Result.Cancelled;
                double extension = UnitUtils.ConvertToInternalUnits(options.LengthMm, UnitTypeId.Millimeters);
                XYZ a = axis.GetEndPoint(0), b = axis.GetEndPoint(1);
                XYZ direction = (b - a).Normalize();
                if (options.End == 0) a -= direction * extension;
                else b += direction * extension;

                Element host = doc.GetElement(source.GetHostId());
                RebarBarType barType = doc.GetElement(source.GetTypeId()) as RebarBarType;
                if (host == null || barType == null) throw new InvalidOperationException("Không đọc được host hoặc RebarBarType.");
                RebarHookType startHook = GetHook(doc, source, 0), endHook = GetHook(doc, source, 1);
                RebarHookOrientation startOrientation = source.GetHookOrientation(0), endOrientation = source.GetHookOrientation(1);
                XYZ normal = direction.CrossProduct(XYZ.BasisZ);
                if (normal.GetLength() < 1e-6) normal = direction.CrossProduct(XYZ.BasisY);
                normal = normal.Normalize();

                using (Transaction tx = new Transaction(doc, "KStruct - Kéo dài thanh Rebar"))
                {
                    tx.Start();
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            Rebar replacement = Rebar.CreateFromCurves(doc, RebarStyle.Standard, barType,
                                startHook, endHook, host, normal, new List<Curve> { Line.CreateBound(a, b) },
                                startOrientation, endOrientation, true, true);
                            if (replacement == null) throw new InvalidOperationException("Revit không tạo được thanh kéo dài.");
                            CopyMetadata(source, replacement);
                            doc.Delete(source.Id);
                            if (sub.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Revit không chấp nhận thanh mới.");
                        }
                        catch
                        {
                            if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                            throw;
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                }
                TaskDialog.Show("KStruct - Kéo dài Rebar", "Đã kéo dài " + (options.End == 0 ? "đầu bắt đầu" : "đầu kết thúc") + " thêm " + options.LengthMm.ToString("0.##") + " mm.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Kéo dài Rebar", ex.Message);
                return Result.Failed;
            }
        }

        private static ExtendOptions AskOptions(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Kéo dài thanh thẳng",
                Width = 350,
                Height = 205,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Đầu cần kéo dài:", Margin = new Thickness(0, 0, 0, 4) });
            var endCombo = new System.Windows.Controls.ComboBox { MinWidth = 310, MinHeight = 25, ItemsSource = new[] { "Đầu bắt đầu", "Đầu kết thúc" }, SelectedIndex = 1 };
            root.Children.Add(endCombo);
            root.Children.Add(new TextBlock { Text = "Chiều dài kéo thêm (mm):", Margin = new Thickness(0, 10, 0, 4) });
            var lengthBox = new System.Windows.Controls.TextBox { MinWidth = 310, MinHeight = 23, Text = "100" };
            root.Children.Add(lengthBox);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Áp dụng", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double length;
                if (endCombo.SelectedIndex < 0 || !double.TryParse(lengthBox.Text, out length) || length <= 0)
                {
                    TaskDialog.Show("KStruct - Kéo dài Rebar", "Chiều dài kéo thêm phải là số lớn hơn 0.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new ExtendOptions { End = endCombo.SelectedIndex, LengthMm = double.Parse(lengthBox.Text) };
        }

        private static RebarHookType GetHook(Document doc, Rebar rebar, int end)
        {
            ElementId id = rebar.GetHookTypeId(end);
            return id == null || id == ElementId.InvalidElementId ? null : doc.GetElement(id) as RebarHookType;
        }

        private static void CopyMetadata(Rebar source, Rebar target)
        {
            target.ScheduleMark = source.ScheduleMark;
            Parameter sourcePartition = source.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            Parameter targetPartition = target.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (sourcePartition != null && targetPartition != null && !targetPartition.IsReadOnly && sourcePartition.HasValue)
                targetPartition.Set(sourcePartition.AsString());
        }
    }
}
