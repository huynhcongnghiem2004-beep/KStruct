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

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class AnalyticalPanelFromGeometry
    {
        internal sealed class RoleChoice
        {
            public AnalyticalStructuralRole Role { get; set; }
            public string Name { get; set; }
            public override string ToString() => Name;
        }

        private sealed class LoopInfo
        {
            public CurveLoop Loop;
            public List<XYZ> Points;
            public List<UV> Projected;
            public double Area;
            public int Parent = -1;
            public int Depth;
        }

        private sealed class Vertex
        {
            public XYZ Point;
            public List<int> Edges = new List<int>();
        }

        private sealed class Edge
        {
            public int A, B;
            public Curve Curve;
            public bool Used;
        }

        internal sealed class FaceFilter : ISelectionFilter
        {
            private readonly Document _doc;
            public FaceFilter(Document doc) { _doc = doc; }
            public bool AllowElement(Element element) => element != null;
            public bool AllowReference(Reference reference, XYZ position) =>
                _doc.GetElement(reference)?.GetGeometryObjectFromReference(reference) is PlanarFace;
        }

        internal sealed class MemberFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is AnalyticalMember;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        internal sealed class NodeFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                ReferencePoint point = element as ReferencePoint;
                if (point == null) return false;
                try
                {
                    using (AnalyticalNodeData nodeData = AnalyticalNodeData.GetAnalyticalNodeData(point))
                        return nodeData != null && nodeData.IsValidObject;
                }
                catch { return false; }
            }
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        internal static RoleChoice ChooseRole(IntPtr owner)
        {
            var choices = new[]
            {
                new RoleChoice { Role = AnalyticalStructuralRole.StructuralRoleFloor, Name = "Floor" },
                new RoleChoice { Role = AnalyticalStructuralRole.StructuralRolePanel, Name = "Panel" },
                new RoleChoice { Role = AnalyticalStructuralRole.StructuralRoleWall, Name = "Wall" }
            };
            var window = new Window
            {
                Title = "Tạo Analytical Panel - KStruct", Width = 410, Height = 190,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Structural Role của Analytical Panel mới:" });
            var box = new System.Windows.Controls.ComboBox { ItemsSource = choices, SelectedIndex = 0, MinWidth = 360, Margin = new Thickness(0, 5, 0, 8) };
            root.Children.Add(box);
            root.Children.Add(new TextBlock
            {
                Text = "Role chỉ phân loại mô hình tính; không đổi mặt phẳng hoặc hướng của biên đã chọn.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Ok", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var close = new Button { Content = "Close", Width = 72, IsCancel = true };
            ok.Click += (sender, args) => { if (box.SelectedItem != null) window.DialogResult = true; };
            buttons.Children.Add(ok); buttons.Children.Add(close); root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? box.SelectedItem as RoleChoice : null;
        }

        internal static int CreateFromFaceReferences(Document doc, IList<Reference> references, AnalyticalStructuralRole role,
            out int skipped, out List<string> errors)
        {
            skipped = 0;
            errors = new List<string>();
            int created = 0;
            using (Transaction tx = new Transaction(doc, "KStruct - Analytical Panel từ Face"))
            {
                tx.Start();
                foreach (Reference reference in references)
                {
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            Element host = doc.GetElement(reference);
                            PlanarFace face = host?.GetGeometryObjectFromReference(reference) as PlanarFace;
                            if (face == null) throw new InvalidOperationException("Chỉ hỗ trợ face phẳng.");
                            int count = CreateFromLoopSet(doc, face.GetEdgesAsCurveLoops(), role);
                            if (count == 0) throw new InvalidOperationException("Face không tạo được Analytical Panel hợp lệ.");
                            if (sub.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Revit không chấp nhận Analytical Panel.");
                            created += count;
                        }
                        catch (Exception ex)
                        {
                            if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                            skipped++;
                            if (errors.Count < 8) errors.Add("Face " + reference.ElementId.Value + ": " + ex.Message);
                        }
                    }
                }
                if (created == 0) { tx.RollBack(); return 0; }
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit không chấp nhận các Analytical Panel từ Face.");
            }
            return created;
        }

        internal static int CreateFromMemberCurves(Document doc, IList<AnalyticalMember> members,
            AnalyticalStructuralRole role, out int skipped, out List<string> errors)
        {
            skipped = 0;
            errors = new List<string>();
            List<CurveLoop> loops;
            try { loops = BuildClosedLoopsFromMembers(doc, members); }
            catch (Exception ex) { errors.Add(ex.Message); skipped = members.Count; return 0; }
            return CreateFromLoopSets(doc, new[] { loops }, role, out skipped, errors, "Analytical Member");
        }

        internal static int CreateFromOrderedNodes(Document doc, IList<ReferencePoint> points,
            AnalyticalStructuralRole role, out int skipped, out List<string> errors)
        {
            skipped = 0;
            errors = new List<string>();
            try
            {
                if (points == null || points.Count < 4) throw new InvalidOperationException("Chọn ít nhất bốn Analytical Node theo thứ tự quanh chu vi.");
                CurveLoop loop = new CurveLoop();
                for (int i = 0; i < points.Count; i++)
                {
                    XYZ a = points[i].Position, b = points[(i + 1) % points.Count].Position;
                    if (a.DistanceTo(b) <= doc.Application.ShortCurveTolerance)
                        throw new InvalidOperationException("Danh sách có hai node trùng hoặc quá gần nhau.");
                    loop.Append(Line.CreateBound(a, b));
                }
                return CreateFromLoopSets(doc, new[] { new List<CurveLoop> { loop } }, role, out skipped, errors, "Analytical Node");
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                skipped = 1;
                return 0;
            }
        }

        private static int CreateFromLoopSets(Document doc, IEnumerable<IList<CurveLoop>> sets,
            AnalyticalStructuralRole role, out int skipped, List<string> errors, string source)
        {
            skipped = 0;
            int created = 0;
            using (Transaction tx = new Transaction(doc, "KStruct - Analytical Panel từ " + source))
            {
                tx.Start();
                foreach (IList<CurveLoop> set in sets)
                {
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            int count = CreateFromLoopSet(doc, set, role);
                            if (count == 0) throw new InvalidOperationException("Đường bao không tạo được Analytical Panel.");
                            if (sub.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Revit không chấp nhận Analytical Panel.");
                            created += count;
                        }
                        catch (Exception ex)
                        {
                            if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                            skipped++;
                            if (errors.Count < 8) errors.Add(source + ": " + ex.Message);
                        }
                    }
                }
                if (created == 0) { tx.RollBack(); return 0; }
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit không chấp nhận các Analytical Panel.");
            }
            return created;
        }

        private static int CreateFromLoopSet(Document doc, IList<CurveLoop> loops, AnalyticalStructuralRole role)
        {
            if (loops == null || loops.Count == 0) throw new InvalidOperationException("Không đọc được loop biên.");
            List<LoopInfo> info = BuildLoopInfo(doc, loops);
            OrganizeNesting(info);
            int created = 0;
            for (int i = 0; i < info.Count; i++)
            {
                LoopInfo outer = info[i];
                if (outer.Depth % 2 != 0) continue;
                AnalyticalPanel panel = AnalyticalPanel.Create(doc, outer.Loop);
                panel.StructuralRole = role;
                foreach (LoopInfo hole in info.Where(loop => loop.Parent == i && loop.Depth == outer.Depth + 1))
                {
                    if (!AnalyticalOpening.IsCurveLoopValidForAnalyticalOpening(hole.Loop, doc, panel.Id))
                        throw new InvalidOperationException("Loop lỗ mở không hợp lệ cho Analytical Panel.");
                    AnalyticalOpening.Create(doc, hole.Loop, panel.Id);
                }
                created++;
            }
            return created;
        }

        private static List<LoopInfo> BuildLoopInfo(Document doc, IList<CurveLoop> loops)
        {
            List<LoopInfo> result = loops.Select(loop =>
            {
                if (loop == null || !AnalyticalSurfaceBase.IsCurveLoopValid(loop))
                    throw new InvalidOperationException("Loop biên không kín hoặc không hợp lệ.");
                List<XYZ> points = loop.SelectMany(curve => curve.Tessellate()).ToList();
                if (points.Count < 3) throw new InvalidOperationException("Loop biên có ít hơn ba điểm.");
                return new LoopInfo { Loop = loop, Points = points };
            }).ToList();
            XYZ origin = result[0].Points[0];
            XYZ normal = FindPlaneNormal(result[0].Points, doc.Application.ShortCurveTolerance);
            XYZ basisX = (result[0].Points.First(point => point.DistanceTo(origin) > doc.Application.ShortCurveTolerance) - origin).Normalize();
            XYZ basisY = normal.CrossProduct(basisX).Normalize();
            double tolerance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
            foreach (LoopInfo loop in result)
            {
                if (loop.Points.Any(point => Math.Abs((point - origin).DotProduct(normal)) > tolerance))
                    throw new InvalidOperationException("Các loop không đồng phẳng.");
                loop.Projected = loop.Points.Select(point =>
                {
                    XYZ relative = point - origin;
                    return new UV(relative.DotProduct(basisX), relative.DotProduct(basisY));
                }).ToList();
                loop.Area = Math.Abs(SignedArea(loop.Projected));
            }
            return result.OrderByDescending(loop => loop.Area).ToList();
        }

        private static XYZ FindPlaneNormal(IList<XYZ> points, double tolerance)
        {
            XYZ origin = points[0];
            for (int i = 1; i < points.Count; i++)
            for (int j = i + 1; j < points.Count; j++)
            {
                XYZ cross = (points[i] - origin).CrossProduct(points[j] - origin);
                if (cross.GetLength() > tolerance * tolerance) return cross.Normalize();
            }
            throw new InvalidOperationException("Không xác định được mặt phẳng của loop.");
        }

        private static void OrganizeNesting(IList<LoopInfo> loops)
        {
            for (int i = 0; i < loops.Count; i++)
            {
                LoopInfo child = loops[i];
                int parent = -1;
                double parentArea = double.MaxValue;
                UV probe = child.Projected[0];
                for (int j = 0; j < i; j++)
                {
                    LoopInfo candidate = loops[j];
                    if (candidate.Area > child.Area && candidate.Area < parentArea && PointInside(candidate.Projected, probe))
                    {
                        parent = j;
                        parentArea = candidate.Area;
                    }
                }
                child.Parent = parent;
                child.Depth = parent < 0 ? 0 : loops[parent].Depth + 1;
            }
        }

        private static bool PointInside(IList<UV> polygon, UV point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                UV a = polygon[i], b = polygon[j];
                if ((a.V > point.V) != (b.V > point.V) && point.U < (b.U - a.U) * (point.V - a.V) / (b.V - a.V) + a.U)
                    inside = !inside;
            }
            return inside;
        }

        private static double SignedArea(IList<UV> points)
        {
            double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                UV a = points[i], b = points[(i + 1) % points.Count];
                area += a.U * b.V - b.U * a.V;
            }
            return area / 2.0;
        }

        private static List<CurveLoop> BuildClosedLoopsFromMembers(Document doc, IList<AnalyticalMember> members)
        {
            if (members == null || members.Count < 3) throw new InvalidOperationException("Chọn ít nhất ba Analytical Member làm cạnh biên.");
            List<Curve> curves = members.Select(member => member.GetCurve()).ToList();
            if (curves.Any(curve => curve == null || !curve.IsBound))
                throw new InvalidOperationException("Mỗi Analytical Member phải có một đường cong giới hạn hữu hạn.");
            List<XYZ> endpoints = curves.SelectMany(curve => new[] { curve.GetEndPoint(0), curve.GetEndPoint(1) }).ToList();
            XYZ origin = endpoints[0];
            XYZ normal = FindPlaneNormal(endpoints, doc.Application.ShortCurveTolerance);
            double tolerance = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Millimeters);
            if (curves.SelectMany(curve => curve.Tessellate()).Any(point => Math.Abs((point - origin).DotProduct(normal)) > tolerance))
                throw new InvalidOperationException("Các Analytical Member không đồng phẳng.");

            var vertices = new List<Vertex>();
            int AddVertex(XYZ point)
            {
                int existing = vertices.FindIndex(vertex => vertex.Point.DistanceTo(point) <= tolerance);
                if (existing >= 0) return existing;
                vertices.Add(new Vertex { Point = point });
                return vertices.Count - 1;
            }

            var edges = new List<Edge>();
            foreach (Curve curve in curves)
            {
                int a = AddVertex(curve.GetEndPoint(0)), b = AddVertex(curve.GetEndPoint(1));
                if (a == b) throw new InvalidOperationException("Có Analytical Member quá ngắn hoặc hai đầu trùng nhau.");
                int edge = edges.Count;
                edges.Add(new Edge { A = a, B = b, Curve = curve });
                vertices[a].Edges.Add(edge);
                vertices[b].Edges.Add(edge);
            }
            if (vertices.Any(vertex => vertex.Edges.Count != 2))
                throw new InvalidOperationException("Các member phải tạo thành loop kín chỉ bằng các đầu mút; bỏ qua cạnh hở hoặc nút phân nhánh.");

            var loops = new List<CurveLoop>();
            for (int startEdge = 0; startEdge < edges.Count; startEdge++)
            {
                if (edges[startEdge].Used) continue;
                var loop = new CurveLoop();
                int firstVertex = edges[startEdge].A;
                int currentVertex = firstVertex;
                int edgeIndex = startEdge;
                int guard = 0;
                do
                {
                    if (++guard > edges.Count + 1) throw new InvalidOperationException("Không thể sắp xếp cạnh thành loop kín.");
                    Edge edge = edges[edgeIndex];
                    if (edge.Used) throw new InvalidOperationException("Loop lặp lại cùng một cạnh.");
                    int nextVertex = edge.A == currentVertex ? edge.B : edge.A;
                    XYZ a = vertices[currentVertex].Point, b = vertices[nextVertex].Point;
                    if (a.DistanceTo(b) <= doc.Application.ShortCurveTolerance)
                        throw new InvalidOperationException("Cạnh loop ngắn hơn ShortCurveTolerance của Revit.");
                    loop.Append(edge.A == currentVertex ? edge.Curve : edge.Curve.CreateReversed());
                    edge.Used = true;
                    currentVertex = nextVertex;
                    if (currentVertex == firstVertex) break;
                    edgeIndex = vertices[currentVertex].Edges.First(candidate => !edges[candidate].Used);
                } while (true);
                loops.Add(loop);
            }
            return loops;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CreateAnalyticalPanelsFromFacesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            try
            {
                var role = AnalyticalPanelFromGeometry.ChooseRole(data.Application.MainWindowHandle);
                if (role == null) return Result.Cancelled;
                IList<Reference> faces = ui.Selection.PickObjects(ObjectType.Face,
                    new AnalyticalPanelFromGeometry.FaceFilter(ui.Document), "Chọn các planar face để tạo Analytical Panel rồi nhấn Finish");
                int count = AnalyticalPanelFromGeometry.CreateFromFaceReferences(ui.Document, faces, role.Role,
                    out int skipped, out List<string> errors);
                return ShowResult(count, skipped, errors, ref message, "Face");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.ToString(); TaskDialog.Show("KStruct - Analytical Panel", ex.Message); return Result.Failed; }
        }

        internal static Result ShowResult(int count, int skipped, IList<string> errors, ref string message, string source)
        {
            string report = count == 0 ? "Không tạo được Analytical Panel từ " + source + "." : "Đã tạo " + count + " Analytical Panel từ " + source + ".";
            if (skipped > 0) report += " Bỏ qua " + skipped + ":\n" + string.Join("\n", errors.Take(8));
            TaskDialog.Show("KStruct - Analytical Panel", report);
            return count > 0 ? Result.Succeeded : Result.Cancelled;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CreateAnalyticalPanelsFromMembersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            try
            {
                var role = AnalyticalPanelFromGeometry.ChooseRole(data.Application.MainWindowHandle);
                if (role == null) return Result.Cancelled;
                List<AnalyticalMember> members = ui.Selection.GetElementIds().Select(ui.Document.GetElement).OfType<AnalyticalMember>().ToList();
                if (members.Count == 0)
                    members = ui.Selection.PickObjects(ObjectType.Element, new AnalyticalPanelFromGeometry.MemberFilter(),
                        "Chọn các Analytical Member tạo loop kín rồi nhấn Finish")
                        .Select(reference => ui.Document.GetElement(reference)).OfType<AnalyticalMember>().ToList();
                int count = AnalyticalPanelFromGeometry.CreateFromMemberCurves(ui.Document, members, role.Role,
                    out int skipped, out List<string> errors);
                return CreateAnalyticalPanelsFromFacesCommand.ShowResult(count, skipped, errors, ref message, "Analytical Member");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.ToString(); TaskDialog.Show("KStruct - Analytical Panel", ex.Message); return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class CreateAnalyticalPanelsFromNodesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            try
            {
                var role = AnalyticalPanelFromGeometry.ChooseRole(data.Application.MainWindowHandle);
                if (role == null) return Result.Cancelled;
                List<ReferencePoint> points = ui.Selection.PickObjects(ObjectType.Element,
                    new AnalyticalPanelFromGeometry.NodeFilter(), "Chọn ít nhất bốn Analytical Node theo thứ tự quanh chu vi rồi nhấn Finish")
                    .Select(reference => ui.Document.GetElement(reference)).OfType<ReferencePoint>().ToList();
                int count = AnalyticalPanelFromGeometry.CreateFromOrderedNodes(ui.Document, points, role.Role,
                    out int skipped, out List<string> errors);
                return CreateAnalyticalPanelsFromFacesCommand.ShowResult(count, skipped, errors, ref message, "Analytical Node");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.ToString(); TaskDialog.Show("KStruct - Analytical Panel", ex.Message); return Result.Failed; }
        }
    }
}
