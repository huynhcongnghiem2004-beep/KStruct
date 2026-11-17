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

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class OptimizeAnalyticalMembersCommand : IExternalCommand
    {
        private sealed class ColumnAxis
        {
            public XYZ Center;
            public double MinX, MaxX, MinY, MaxY;
        }

        private sealed class Options
        {
            public double MaxAdjustmentMm;
            public double MinMemberLengthMm;
            public bool SelectedOnly;
        }

        private sealed class Change
        {
            public AnalyticalMember Member;
            public Line NewCurve;
            public bool StartMoved, EndMoved;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                HashSet<ElementId> selectedIds = new HashSet<ElementId>(ui.Selection.GetElementIds());
                List<AnalyticalMember> allMembers = new FilteredElementCollector(doc)
                    .OfClass(typeof(AnalyticalMember)).WhereElementIsNotElementType()
                    .Cast<AnalyticalMember>().ToList();
                List<AnalyticalMember> selectedMembers = allMembers.Where(member => selectedIds.Contains(member.Id)).ToList();
                Options options = ChooseOptions(data.Application.MainWindowHandle, selectedMembers.Count > 0);
                if (options == null) return Result.Cancelled;
                if (options.SelectedOnly && selectedMembers.Count == 0)
                {
                    TaskDialog.Show("KStruct - Optimize Analytical Member", "Hãy chọn ít nhất một Analytical Member trước khi chạy chế độ Selected.");
                    return Result.Cancelled;
                }

                List<AnalyticalMember> targets = options.SelectedOnly ? selectedMembers : allMembers;
                List<AnalyticalMember> columns = allMembers.Where(member =>
                    member.StructuralRole == AnalyticalStructuralRole.StructuralRoleColumn && IsVerticalLine(member.GetCurve())).ToList();
                AnalyticalToPhysicalAssociationManager association =
                    AnalyticalToPhysicalAssociationManager.GetAnalyticalToPhysicalAssociationManager(doc);
                List<ColumnAxis> axes = columns.Select(column => ReadColumnAxis(doc, association, column))
                    .Where(axis => axis != null).ToList();
                if (axes.Count == 0)
                    throw new InvalidOperationException("Không tìm thấy Analytical Column thẳng đứng có liên kết cấu kiện vật lý để xác định vùng cột.");

                double maxAdjustment = UnitUtils.ConvertToInternalUnits(options.MaxAdjustmentMm, UnitTypeId.Millimeters);
                double minLength = UnitUtils.ConvertToInternalUnits(options.MinMemberLengthMm, UnitTypeId.Millimeters);
                double tolerance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
                var changes = new List<Change>();
                int nonLinear = 0, noNearbyColumn = 0, tooShort = 0;
                foreach (AnalyticalMember member in targets.Where(member =>
                    member.StructuralRole == AnalyticalStructuralRole.StructuralRoleBeam ||
                    member.StructuralRole == AnalyticalStructuralRole.StructuralRoleGirder))
                {
                    Line line = member.GetCurve() as Line;
                    if (line == null) { nonLinear++; continue; }
                    XYZ start = line.GetEndPoint(0), end = line.GetEndPoint(1);
                    if (start.DistanceTo(end) <= tolerance) { tooShort++; continue; }
                    XYZ candidateStart = FindNearestColumnNodeProjection(start, end, axes, true, maxAdjustment, tolerance);
                    XYZ candidateEnd = FindNearestColumnNodeProjection(start, end, axes, false, maxAdjustment, tolerance);
                    XYZ newStart = candidateStart ?? start;
                    XYZ newEnd = candidateEnd ?? end;
                    if (newStart.DistanceTo(start) <= tolerance && newEnd.DistanceTo(end) <= tolerance)
                    {
                        noNearbyColumn++;
                        continue;
                    }
                    if (newStart.DistanceTo(newEnd) < minLength)
                    {
                        tooShort++;
                        continue;
                    }
                    Line proposed = Line.CreateBound(newStart, newEnd);
                    if (!AnalyticalMember.IsValidCurve(proposed))
                    {
                        tooShort++;
                        continue;
                    }
                    changes.Add(new Change
                    {
                        Member = member,
                        NewCurve = proposed,
                        StartMoved = newStart.DistanceTo(start) > tolerance,
                        EndMoved = newEnd.DistanceTo(end) > tolerance
                    });
                }

                if (changes.Count == 0)
                {
                    TaskDialog.Show("KStruct - Optimize Analytical Member",
                        "Không có dầm analytical thẳng nào cần điều chỉnh trong phạm vi " + options.MaxAdjustmentMm.ToString("0.##", CultureInfo.CurrentCulture) + " mm.");
                    return Result.Cancelled;
                }

                TaskDialogResult confirm = TaskDialog.Show("KStruct - Xác nhận điều chỉnh",
                    "Sẽ chỉnh " + changes.Count + " Analytical Member tại gần " +
                    changes.Sum(change => (change.StartMoved ? 1 : 0) + (change.EndMoved ? 1 : 0)) +
                    " đầu thanh. API SetCurve có thể ngắt liên kết nút analytical ở đầu được đổi; hãy kiểm tra mô hình và dùng Undo nếu kết quả không phù hợp. Tiếp tục?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                if (confirm != TaskDialogResult.Yes) return Result.Cancelled;

                int changed = 0, failed = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Optimize Analytical Members"))
                {
                    tx.Start();
                    foreach (Change change in changes)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                change.Member.SetCurve(change.NewCurve);
                                if (sub.Commit() == TransactionStatus.Committed) changed++;
                                else throw new InvalidOperationException("Revit không chấp nhận đường analytical mới.");
                            }
                            catch
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                failed++;
                            }
                        }
                    }
                    if (changed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Optimize Analytical Member", "Revit không chấp nhận thay đổi nào.");
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận cập nhật Analytical Member.");
                }

                string report = "Đã chỉnh " + changed + "/" + changes.Count + " Analytical Member theo trục cột analytical liên kết cấu kiện vật lý.";
                if (failed > 0) report += " Không cập nhật được " + failed + " thanh.";
                if (nonLinear > 0) report += " Bỏ qua " + nonLinear + " thanh cong/không thẳng.";
                if (noNearbyColumn > 0) report += " Không có nút cột trong phạm vi: " + noNearbyColumn + ".";
                if (tooShort > 0) report += " Bỏ qua do chiều dài tối thiểu/đường không hợp lệ: " + tooShort + ".";
                TaskDialog.Show("KStruct - Optimize Analytical Member", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Optimize Analytical Member", ex.Message);
                return Result.Failed;
            }
        }

        private static ColumnAxis ReadColumnAxis(Document doc, AnalyticalToPhysicalAssociationManager association, AnalyticalMember column)
        {
            try
            {
                ElementId physicalId = association.GetAssociatedElementId(column.Id);
                Element physical = physicalId == ElementId.InvalidElementId ? null : doc.GetElement(physicalId);
                BoundingBoxXYZ box = physical?.get_BoundingBox(null);
                Line line = column.GetCurve() as Line;
                if (box == null || line == null) return null;
                Transform transform = box.Transform ?? Transform.Identity;
                XYZ[] corners = (from ix in new[] { 0, 1 }
                    from iy in new[] { 0, 1 }
                    from iz in new[] { 0, 1 }
                    select transform.OfPoint(new XYZ(ix == 0 ? box.Min.X : box.Max.X,
                        iy == 0 ? box.Min.Y : box.Max.Y, iz == 0 ? box.Min.Z : box.Max.Z))).ToArray();
                XYZ a = line.GetEndPoint(0), b = line.GetEndPoint(1);
                return new ColumnAxis
                {
                    Center = (a + b) / 2.0,
                    MinX = corners.Min(point => point.X), MaxX = corners.Max(point => point.X),
                    MinY = corners.Min(point => point.Y), MaxY = corners.Max(point => point.Y)
                };
            }
            catch { return null; }
        }

        private static bool IsVerticalLine(Curve curve)
        {
            Line line = curve as Line;
            return line != null && Math.Abs(line.Direction.Normalize().DotProduct(XYZ.BasisZ)) > 0.95;
        }

        private static XYZ FindNearestColumnNodeProjection(XYZ start, XYZ end, IList<ColumnAxis> axes,
            bool isStart, double maxAdjustment, double tolerance)
        {
            XYZ direction = end - start;
            double planLengthSquared = direction.X * direction.X + direction.Y * direction.Y;
            if (planLengthSquared <= tolerance * tolerance) return null;
            double planLength = Math.Sqrt(planLengthSquared);
            ColumnAxis bestAxis = null;
            XYZ bestPoint = null;
            double bestDistance = double.MaxValue;
            foreach (ColumnAxis axis in axes)
            {
                double t = ((axis.Center.X - start.X) * direction.X + (axis.Center.Y - start.Y) * direction.Y) / planLengthSquared;
                XYZ point = start + direction * t;
                if (point.X < axis.MinX - tolerance || point.X > axis.MaxX + tolerance ||
                    point.Y < axis.MinY - tolerance || point.Y > axis.MaxY + tolerance) continue;
                double distance = Math.Abs(isStart ? t : 1.0 - t) * planLength;
                if (distance <= tolerance || distance > maxAdjustment || distance >= bestDistance) continue;
                bestAxis = axis;
                bestPoint = point;
                bestDistance = distance;
            }
            return bestAxis == null ? null : bestPoint;
        }

        private static Options ChooseOptions(IntPtr owner, bool hasSelection)
        {
            var window = new Window
            {
                Title = "Optimize Analytical Member - KStruct", Width = 430, Height = 245,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            System.Windows.Controls.TextBox maxAdjustment = AddInput(root, "Phạm vi tìm nút cột từ đầu dầm (mm):", "300");
            System.Windows.Controls.TextBox minLength = AddInput(root, "Chiều dài tối thiểu sau chỉnh (mm):", "200");
            var selectedOnly = new CheckBox
            {
                Content = "Chỉ xử lý Analytical Member được chọn",
                IsChecked = hasSelection, Margin = new Thickness(0, 8, 0, 4)
            };
            root.Children.Add(selectedOnly);
            root.Children.Add(new TextBlock
            {
                Text = "KStruct dùng Analytical Column được liên kết với cấu kiện vật lý; chỉ xử lý Analytical Member dạng Beam/Girder thẳng.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6)
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Phân tích", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double max, min;
                if (!double.TryParse(maxAdjustment.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out max) ||
                    !double.TryParse(minLength.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out min) || max <= 0 || min <= 0)
                {
                    TaskDialog.Show("KStruct - Analytical Member", "Các giá trị phải là số lớn hơn 0.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new Options
            {
                MaxAdjustmentMm = double.Parse(maxAdjustment.Text, CultureInfo.CurrentCulture),
                MinMemberLengthMm = double.Parse(minLength.Text, CultureInfo.CurrentCulture),
                SelectedOnly = selectedOnly.IsChecked == true
            };
        }

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { Text = value, MinWidth = 390, MinHeight = 22 };
            root.Children.Add(box);
            return box;
        }
    }
}
