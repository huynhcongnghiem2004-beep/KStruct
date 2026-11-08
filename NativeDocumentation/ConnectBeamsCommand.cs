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
using Autodesk.Revit.UI.Selection;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ConnectBeamsCommand : IExternalCommand
    {
        private sealed class BeamFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class BeamData
        {
            public FamilyInstance Beam;
            public Line Axis;
            public XYZ Direction;
            public XYZ Start, End;
        }

        private sealed class Endpoint
        {
            public BeamData Data;
            public int EndIndex;
            public XYZ Point;
            public XYZ Outward;
            public bool Used;
            public string Key => Data.Beam.Id.Value + ":" + EndIndex;
        }

        private sealed class Candidate
        {
            public Endpoint A, B;
            public double Distance;
            public XYZ Target;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> selected = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(IsBeam).Distinct().ToList();
                if (selected.Count < 2)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new BeamFilter(),
                        "Chọn ít nhất hai dầm thẳng cần nối đầu-cuối");
                    selected = picked.Select(reference => doc.GetElement(reference)).OfType<FamilyInstance>()
                        .Where(IsBeam).Distinct().ToList();
                }
                if (selected.Count < 2) throw new InvalidOperationException("Cần ít nhất hai dầm Structural Framing.");
                double toleranceMm = ChooseTolerance(data.Application.MainWindowHandle);
                if (double.IsNaN(toleranceMm)) return Result.Cancelled;
                double tolerance = UnitUtils.ConvertToInternalUnits(toleranceMm, UnitTypeId.Millimeters);

                List<BeamData> beams = selected.Select(ReadBeam).ToList();
                List<Endpoint> endpoints = beams.SelectMany(beam => new[]
                {
                    new Endpoint { Data = beam, EndIndex = 0, Point = beam.Start, Outward = beam.Direction.Negate() },
                    new Endpoint { Data = beam, EndIndex = 1, Point = beam.End, Outward = beam.Direction }
                }).ToList();
                List<Candidate> candidates = FindCandidates(endpoints, tolerance);
                var assignments = new Dictionary<string, XYZ>(StringComparer.Ordinal);
                var connections = new List<Candidate>();
                foreach (Candidate candidate in candidates)
                {
                    if (candidate.A.Used || candidate.B.Used) continue;
                    candidate.A.Used = candidate.B.Used = true;
                    assignments[candidate.A.Key] = candidate.Target;
                    assignments[candidate.B.Key] = candidate.Target;
                    connections.Add(candidate);
                }
                if (connections.Count == 0)
                    throw new InvalidOperationException("Không tìm thấy hai đầu dầm thẳng, gần đồng trục trong phạm vi " +
                        toleranceMm.ToString("0.#") + " mm. Dầm giao nhau cần dùng lệnh Join Geometry.");

                using (Transaction tx = new Transaction(doc, "KStruct - Nối đầu dầm"))
                {
                    tx.Start();
                    foreach (BeamData beam in beams)
                    {
                        XYZ start = assignments.TryGetValue(beam.Beam.Id.Value + ":0", out XYZ newStart) ? newStart : beam.Start;
                        XYZ end = assignments.TryGetValue(beam.Beam.Id.Value + ":1", out XYZ newEnd) ? newEnd : beam.End;
                        if (start.DistanceTo(end) <= doc.Application.ShortCurveTolerance * 2)
                            throw new InvalidOperationException("Kết nối làm dầm " + beam.Beam.Id.Value + " ngắn hơn giới hạn Revit.");
                        if (start.DistanceTo(beam.Start) > doc.Application.ShortCurveTolerance ||
                            end.DistanceTo(beam.End) > doc.Application.ShortCurveTolerance)
                            (beam.Beam.Location as LocationCurve).Curve = Line.CreateBound(start, end);
                    }
                    foreach (Candidate connection in connections)
                    {
                        StructuralFramingUtils.AllowJoinAtEnd(connection.A.Data.Beam, connection.A.EndIndex);
                        StructuralFramingUtils.AllowJoinAtEnd(connection.B.Data.Beam, connection.B.EndIndex);
                    }
                    doc.Regenerate();
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi đầu dầm.");
                }

                TaskDialog.Show("KStruct - Nối dầm",
                    "Đã nối " + connections.Count + " cặp đầu dầm. KStruct chỉ điều chỉnh các đầu gần đồng trục trong phạm vi " +
                    toleranceMm.ToString("0.#") + " mm; các dầm giao nhau dùng lệnh Join Geometry.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Nối dầm", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsBeam(FamilyInstance beam) => beam?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;

        private static BeamData ReadBeam(FamilyInstance beam)
        {
            Line axis = (beam.Location as LocationCurve)?.Curve as Line;
            if (axis == null) throw new InvalidOperationException("Chỉ hỗ trợ dầm Structural Framing có trục thẳng. Dầm " + beam.Id.Value + ".");
            return new BeamData { Beam = beam, Axis = axis, Direction = axis.Direction.Normalize(), Start = axis.GetEndPoint(0), End = axis.GetEndPoint(1) };
        }

        private static List<Candidate> FindCandidates(IList<Endpoint> endpoints, double tolerance)
        {
            var result = new List<Candidate>();
            double angleTolerance = 1e-5;
            for (int i = 0; i < endpoints.Count; i++)
            for (int j = i + 1; j < endpoints.Count; j++)
            {
                Endpoint a = endpoints[i], b = endpoints[j];
                if (a.Data.Beam.Id == b.Data.Beam.Id) continue;
                if (Math.Abs(Math.Abs(a.Data.Direction.DotProduct(b.Data.Direction)) - 1.0) > angleTolerance) continue;
                XYZ delta = b.Point - a.Point;
                double distance = delta.GetLength();
                if (distance > tolerance) continue;
                if (distance <= 1e-9)
                {
                    if (a.Outward.DotProduct(b.Outward) > -0.5) continue;
                    result.Add(new Candidate { A = a, B = b, Distance = 0, Target = a.Point });
                }
                else
                {
                    XYZ lateralA = delta - a.Data.Direction * delta.DotProduct(a.Data.Direction);
                    XYZ lateralB = delta - b.Data.Direction * delta.DotProduct(b.Data.Direction);
                    if (lateralA.GetLength() > tolerance || lateralB.GetLength() > tolerance) continue;
                    if (a.Outward.DotProduct(delta.Normalize()) < 0.5 || b.Outward.DotProduct(delta.Negate().Normalize()) < 0.5) continue;
                    result.Add(new Candidate { A = a, B = b, Distance = distance, Target = (a.Point + b.Point) * 0.5 });
                }
            }
            return result.OrderBy(candidate => candidate.Distance).ToList();
        }

        private static double ChooseTolerance(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Nối dầm đầu-cuối - KStruct", Width = 420, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = "Khoảng cách tối đa để ghép hai đầu dầm gần đồng trục (mm):" });
            var toleranceBox = new WpfTextBox { Text = "300", MinWidth = 380, Margin = new Thickness(0, 5, 0, 10) };
            root.Children.Add(toleranceBox);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Nối dầm", MinWidth = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            double result = double.NaN;
            ok.Click += (sender, args) =>
            {
                double value;
                if (!double.TryParse(toleranceBox.Text, out value) || value < 0 || value > 2000)
                {
                    MessageBox.Show(window, "Khoảng cách phải từ 0 đến 2000 mm.", "KStruct - Nối dầm", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = value;
                window.DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons); window.Content = root;
            return window.ShowDialog() == true ? result : double.NaN;
        }
    }
}
