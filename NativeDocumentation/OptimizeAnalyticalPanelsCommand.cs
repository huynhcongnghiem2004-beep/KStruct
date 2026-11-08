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
    public sealed class OptimizeAnalyticalPanelsCommand : IExternalCommand
    {
        private sealed class Options
        {
            public double MaxDistanceMm;
            public bool SelectedOnly;
        }

        private sealed class Edge2D
        {
            public XYZ Start, End;
        }

        private sealed class CandidateBeam
        {
            public Line Line;
            public double Offset;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                HashSet<ElementId> selectedIds = new HashSet<ElementId>(ui.Selection.GetElementIds());
                List<AnalyticalPanel> allPanels = new FilteredElementCollector(doc).OfClass(typeof(AnalyticalPanel))
                    .WhereElementIsNotElementType().Cast<AnalyticalPanel>()
                    .Where(panel => panel.StructuralRole == AnalyticalStructuralRole.StructuralRoleFloor).ToList();
                var panelIds = new HashSet<ElementId>(allPanels.Select(panel => panel.Id));
                List<AnalyticalPanel> selectedPanels = selectedIds.Select(doc.GetElement).OfType<AnalyticalPanel>()
                    .Where(panel => panel.StructuralRole == AnalyticalStructuralRole.StructuralRoleFloor).ToList();
                foreach (AnalyticalOpening opening in selectedIds.Select(doc.GetElement).OfType<AnalyticalOpening>())
                {
                    AnalyticalPanel parent = doc.GetElement(opening.PanelId) as AnalyticalPanel;
                    if (parent != null && panelIds.Contains(parent.Id) && !selectedPanels.Any(panel => panel.Id == parent.Id))
                        selectedPanels.Add(parent);
                }

                Options options = ChooseOptions(data.Application.MainWindowHandle, selectedPanels.Count > 0);
                if (options == null) return Result.Cancelled;
                if (options.SelectedOnly && selectedPanels.Count == 0)
                {
                    TaskDialog.Show("KStruct - Optimize Analytical Panel", "Hãy chọn Analytical Panel hoặc Analytical Opening trước khi chạy chế độ Selected.");
                    return Result.Cancelled;
                }

                List<AnalyticalPanel> targets = options.SelectedOnly ? selectedPanels : allPanels;
                List<Line> beams = new FilteredElementCollector(doc).OfClass(typeof(AnalyticalMember))
                    .WhereElementIsNotElementType().Cast<AnalyticalMember>()
                    .Where(member => member.StructuralRole == AnalyticalStructuralRole.StructuralRoleBeam ||
                        member.StructuralRole == AnalyticalStructuralRole.StructuralRoleGirder)
                    .Select(member => member.GetCurve() as Line).Where(line => line != null).ToList();
                if (beams.Count == 0)
                    throw new InvalidOperationException("Không tìm thấy Analytical Beam/Girder thẳng để căn biên panel.");

                double maxDistance = UnitUtils.ConvertToInternalUnits(options.MaxDistanceMm, UnitTypeId.Millimeters);
                double tolerance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
                var changes = new Dictionary<ElementId, CurveLoop>();
                int skippedNonPlanar = 0, skippedCurved = 0, skippedOpenings = 0, noNearbyBeam = 0, invalid = 0;
                foreach (AnalyticalPanel panel in targets)
                {
                    try
                    {
                        CurveLoop updated = BuildUpdatedContour(doc, panel, beams, maxDistance, tolerance,
                            out int movedEdges, out string reason);
                        if (updated == null)
                        {
                            if (reason == "curved") skippedCurved++;
                            else if (reason == "nonplanar") skippedNonPlanar++;
                            else if (reason == "opening") skippedOpenings++;
                            else if (reason == "invalid") invalid++;
                            else noNearbyBeam++;
                            continue;
                        }
                        if (movedEdges > 0) changes[panel.Id] = updated;
                        else noNearbyBeam++;
                    }
                    catch { invalid++; }
                }

                if (changes.Count == 0)
                {
                    TaskDialog.Show("KStruct - Optimize Analytical Panel", "Không có contour panel nào đủ điều kiện căn theo dầm song song trong phạm vi đã đặt.");
                    return Result.Cancelled;
                }

                TaskDialogResult confirm = TaskDialog.Show("KStruct - Xác nhận điều chỉnh",
                    "Sẽ thay contour ngoài của " + changes.Count + " Analytical Floor Panel. Các Analytical Opening giữ nguyên; nếu một opening ra ngoài biên mới, panel đó đã bị loại. Việc đổi contour có thể làm thay đổi liên kết analytical ở cạnh. Tiếp tục?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                if (confirm != TaskDialogResult.Yes) return Result.Cancelled;

                int changed = 0, failed = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Optimize Analytical Panels"))
                {
                    tx.Start();
                    foreach (KeyValuePair<ElementId, CurveLoop> item in changes)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                AnalyticalPanel panel = doc.GetElement(item.Key) as AnalyticalPanel;
                                if (panel == null || !AnalyticalSurfaceBase.IsCurveLoopValid(item.Value))
                                    throw new InvalidOperationException("Contour mới không hợp lệ cho Analytical Panel.");
                                panel.SetOuterContour(item.Value);
                                if (sub.Commit() == TransactionStatus.Committed) changed++;
                                else throw new InvalidOperationException("Revit không chấp nhận contour mới.");
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
                        TaskDialog.Show("KStruct - Optimize Analytical Panel", "Revit không chấp nhận thay đổi nào.");
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận cập nhật Analytical Panel.");
                }

                string report = "Đã căn contour ngoài cho " + changed + "/" + changes.Count + " Analytical Floor Panel.";
                if (failed > 0) report += " Revit từ chối " + failed + " panel.";
                if (noNearbyBeam > 0) report += " Không có dầm phù hợp: " + noNearbyBeam + ".";
                if (skippedNonPlanar > 0) report += " Bỏ qua panel không nằm ngang: " + skippedNonPlanar + ".";
                if (skippedCurved > 0) report += " Bỏ qua contour có cạnh cong: " + skippedCurved + ".";
                if (skippedOpenings > 0) report += " Bỏ qua do opening không còn nằm trong biên: " + skippedOpenings + ".";
                if (invalid > 0) report += " Bỏ qua contour không hợp lệ: " + invalid + ".";
                TaskDialog.Show("KStruct - Optimize Analytical Panel", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Optimize Analytical Panel", ex.Message);
                return Result.Failed;
            }
        }

        private static CurveLoop BuildUpdatedContour(Document doc, AnalyticalPanel panel, IList<Line> beams,
            double maxDistance, double tolerance, out int movedEdges, out string reason)
        {
            movedEdges = 0;
            reason = "noBeam";
            CurveLoop contour = panel.GetOuterContour();
            if (contour == null) { reason = "invalid"; return null; }
            List<Curve> curves = contour.ToList();
            if (curves.Count < 3) { reason = "invalid"; return null; }
            if (curves.Any(curve => !(curve is Line))) { reason = "curved"; return null; }

            List<Line> edges = curves.Cast<Line>().ToList();
            List<XYZ> vertices = edges.Select(edge => edge.GetEndPoint(0)).ToList();
            double z = vertices[0].Z;
            if (vertices.Concat(edges.Select(edge => edge.GetEndPoint(1))).Any(point => Math.Abs(point.Z - z) > tolerance))
            {
                reason = "nonplanar";
                return null;
            }

            var adjusted = new List<Edge2D>();
            for (int i = 0; i < edges.Count; i++)
            {
                XYZ a = edges[i].GetEndPoint(0), b = edges[i].GetEndPoint(1);
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= tolerance) { reason = "invalid"; return null; }
                CandidateBeam candidate = FindNearestParallelBeam(a, b, z, beams, maxDistance, tolerance);
                XYZ adjustedStart = candidate == null ? a : ProjectPointToPlanLine(a, candidate.Line, z);
                XYZ adjustedEnd = candidate == null ? b : ProjectPointToPlanLine(b, candidate.Line, z);
                bool moved = adjustedStart.DistanceTo(a) > tolerance || adjustedEnd.DistanceTo(b) > tolerance;
                if (moved) movedEdges++;
                adjusted.Add(new Edge2D { Start = adjustedStart, End = adjustedEnd });
            }
            if (movedEdges == 0) return null;

            var newVertices = new List<XYZ>();
            for (int i = 0; i < adjusted.Count; i++)
            {
                Edge2D previous = adjusted[(i + adjusted.Count - 1) % adjusted.Count];
                Edge2D current = adjusted[i];
                XYZ intersection;
                if (!TryIntersectLines(previous.Start, previous.End, current.Start, current.End, z, tolerance, out intersection))
                {
                    reason = "invalid";
                    return null;
                }
                newVertices.Add(intersection);
            }
            for (int i = 0; i < newVertices.Count; i++)
                if (newVertices[i].DistanceTo(newVertices[(i + 1) % newVertices.Count]) <= tolerance)
                {
                    reason = "invalid";
                    return null;
                }

            var loop = new CurveLoop();
            for (int i = 0; i < newVertices.Count; i++)
            {
                XYZ a = newVertices[i], b = newVertices[(i + 1) % newVertices.Count];
                if (a.DistanceTo(b) <= doc.Application.ShortCurveTolerance) { reason = "invalid"; return null; }
                loop.Append(Line.CreateBound(a, b));
            }
            if (!AnalyticalSurfaceBase.IsCurveLoopValid(loop)) { reason = "invalid"; return null; }

            foreach (ElementId openingId in panel.GetAnalyticalOpeningsIds())
            {
                AnalyticalOpening opening = doc.GetElement(openingId) as AnalyticalOpening;
                if (opening == null) continue;
                List<XYZ> openingPoints = opening.GetOuterContour().SelectMany(curve => curve.Tessellate()).ToList();
                if (openingPoints.Any(point => !PointInside(newVertices, point.X, point.Y) ||
                    DistanceToBoundary(newVertices, point.X, point.Y) <= tolerance))
                {
                    reason = "opening";
                    return null;
                }
            }
            reason = "ok";
            return loop;
        }

        private static CandidateBeam FindNearestParallelBeam(XYZ a, XYZ b, double panelZ,
            IList<Line> beams, double maxDistance, double tolerance)
        {
            XYZ edgeVector = new XYZ(b.X - a.X, b.Y - a.Y, 0);
            double edgeLength = edgeVector.GetLength();
            XYZ edgeDirection = edgeVector.Normalize();
            CandidateBeam best = null;
            foreach (Line beam in beams)
            {
                XYZ beamA = beam.GetEndPoint(0), beamB = beam.GetEndPoint(1);
                XYZ beamVector = new XYZ(beamB.X - beamA.X, beamB.Y - beamA.Y, 0);
                double beamPlanLength = beamVector.GetLength();
                if (beamPlanLength <= tolerance) continue;
                XYZ beamDirection = beamVector.Normalize();
                if (Math.Abs(edgeDirection.DotProduct(beamDirection)) < Math.Cos(Math.PI / 180.0)) continue;
                XYZ beamNormal = new XYZ(-beamDirection.Y, beamDirection.X, 0);
                double offsetA = (a - beamA).DotProduct(beamNormal);
                double offsetB = (b - beamA).DotProduct(beamNormal);
                if (Math.Max(Math.Abs(offsetA), Math.Abs(offsetB)) > maxDistance) continue;
                double offset = (Math.Abs(offsetA) + Math.Abs(offsetB)) / 2.0;
                double meanZ = (beamA.Z + beamB.Z) / 2.0;
                if (Math.Abs(meanZ - panelZ) > maxDistance) continue;
                double edgeStart = a.DotProduct(edgeDirection), edgeEnd = b.DotProduct(edgeDirection);
                double beamStart = beamA.DotProduct(edgeDirection), beamEnd = beamB.DotProduct(edgeDirection);
                double edgeMin = Math.Min(edgeStart, edgeEnd), edgeMax = Math.Max(edgeStart, edgeEnd);
                double beamMin = Math.Min(beamStart, beamEnd), beamMax = Math.Max(beamStart, beamEnd);
                if (beamMin > edgeMin + tolerance || beamMax < edgeMax - tolerance) continue;
                if (best == null || offset < best.Offset)
                    best = new CandidateBeam { Line = beam, Offset = offset };
            }
            return best;
        }

        private static bool TryIntersectLines(XYZ a0, XYZ a1, XYZ b0, XYZ b1, double z, double tolerance, out XYZ point)
        {
            double adx = a1.X - a0.X, ady = a1.Y - a0.Y;
            double bdx = b1.X - b0.X, bdy = b1.Y - b0.Y;
            double determinant = adx * bdy - ady * bdx;
            if (Math.Abs(determinant) <= tolerance * tolerance) { point = null; return false; }
            double t = ((b0.X - a0.X) * bdy - (b0.Y - a0.Y) * bdx) / determinant;
            point = new XYZ(a0.X + t * adx, a0.Y + t * ady, z);
            return true;
        }

        private static XYZ ProjectPointToPlanLine(XYZ point, Line line, double z)
        {
            XYZ a = line.GetEndPoint(0), b = line.GetEndPoint(1);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double denominator = dx * dx + dy * dy;
            if (denominator <= 1e-18) return new XYZ(point.X, point.Y, z);
            double t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / denominator;
            return new XYZ(a.X + t * dx, a.Y + t * dy, z);
        }

        private static bool PointInside(IList<XYZ> polygon, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                XYZ a = polygon[i], b = polygon[j];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static double DistanceToBoundary(IList<XYZ> polygon, double x, double y)
        {
            double minimum = double.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                XYZ a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y, length2 = dx * dx + dy * dy;
                double t = length2 <= 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((x - a.X) * dx + (y - a.Y) * dy) / length2));
                minimum = Math.Min(minimum, Math.Sqrt((x - (a.X + t * dx)) * (x - (a.X + t * dx)) +
                    (y - (a.Y + t * dy)) * (y - (a.Y + t * dy))));
            }
            return minimum;
        }

        private static Options ChooseOptions(IntPtr owner, bool hasSelection)
        {
            var window = new Window
            {
                Title = "Optimize Analytical Panel - KStruct", Width = 420, Height = 195,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            System.Windows.Controls.TextBox distance = AddInput(root, "Khoảng tìm dầm song song (mm):", "200");
            var selectedOnly = new CheckBox
            {
                Content = "Chỉ xử lý Analytical Panel/Opening được chọn",
                IsChecked = hasSelection, Margin = new Thickness(0, 8, 0, 4)
            };
            root.Children.Add(selectedOnly);
            root.Children.Add(new TextBlock
            {
                Text = "Chỉ panel Floor nằm ngang, contour gồm các cạnh thẳng; contour opening được giữ nguyên và kiểm tra nằm trong biên mới.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6)
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Phân tích", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double value;
                if (!double.TryParse(distance.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) || value <= 0)
                {
                    TaskDialog.Show("KStruct - Analytical Panel", "Khoảng tìm dầm phải là số lớn hơn 0.");
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
                MaxDistanceMm = double.Parse(distance.Text, CultureInfo.CurrentCulture),
                SelectedOnly = selectedOnly.IsChecked == true
            };
        }

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { Text = value, MinWidth = 385, MinHeight = 22 };
            root.Children.Add(box);
            return box;
        }
    }
}
