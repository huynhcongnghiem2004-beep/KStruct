using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ExportAnalyticalModelCsvCommand : IExternalCommand
    {
        private sealed class ExportRow
        {
            public string ElementId, Kind, Level, Role, AnalyzeAs, Material, Section;
            public string ThicknessMm, RotationDeg, ParentId, HostElementId, WorkPlaneId, LoadCase, LoadData;
            public string NodeIds, CoordinatesMm, Geometry, Status;
            public string ToCsv()
            {
                return string.Join(",", new[] { ElementId, Kind, Level, Role, AnalyzeAs, Material, Section,
                    ThicknessMm, RotationDeg, ParentId, HostElementId, WorkPlaneId, LoadCase, LoadData,
                    NodeIds, CoordinatesMm, Geometry, Status }.Select(Csv));
            }
        }

        private sealed class NodeCatalog
        {
            private const double ToleranceMm = 1.0;
            private readonly List<XYZ> _points = new List<XYZ>();
            private readonly List<string> _statuses = new List<string>();
            private readonly Dictionary<long, int> _referenceNodes = new Dictionary<long, int>();
            private readonly Dictionary<string, List<int>> _buckets = new Dictionary<string, List<int>>();
            private readonly double _tolerance = UnitUtils.ConvertToInternalUnits(ToleranceMm, UnitTypeId.Millimeters);
            public int Count => _points.Count;

            public string AddMember(Document doc, AnalyticalMember member, IList<XYZ> points)
            {
                var ids = new List<string>();
                for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
                {
                    XYZ point = points[pointIndex];
                    if (point == null) continue;
                    int nodeIndex = -1;
                    bool isEndpoint = pointIndex == 0 || pointIndex == points.Count - 1;
                    if (isEndpoint)
                    {
                        try
                        {
                            var selector = new AnalyticalModelSelector(pointIndex == 0
                                ? AnalyticalCurveSelector.StartPoint : AnalyticalCurveSelector.EndPoint);
                            Reference reference = member.GetReference(selector);
                            ReferencePoint referencePoint = reference == null ? null : doc.GetElement(reference.ElementId) as ReferencePoint;
                            if (referencePoint != null)
                            {
                                long referenceId = referencePoint.Id.Value;
                                if (!_referenceNodes.TryGetValue(referenceId, out nodeIndex))
                                {
                                    string connection = "UNKNOWN";
                                    using (AnalyticalNodeData nodeData = AnalyticalNodeData.GetAnalyticalNodeData(referencePoint))
                                        if (nodeData != null) connection = Convert.ToString(nodeData.GetConnectionStatus(), CultureInfo.InvariantCulture);
                                    string nodeStatus = "ANALYTICAL_REFERENCE_" + connection.ToUpperInvariant();
                                    nodeIndex = AddNode(point, nodeStatus, connection == "Connected");
                                    _referenceNodes[referenceId] = nodeIndex;
                                }
                            }
                        }
                        catch { nodeIndex = -1; }
                    }
                    if (nodeIndex < 0) nodeIndex = AddCoordinate(point);
                    string id = NodeId(nodeIndex);
                    if (!ids.Contains(id)) ids.Add(id);
                }
                return string.Join(";", ids);
            }

            public string AddRange(IEnumerable<XYZ> points)
            {
                var ids = new List<string>();
                foreach (XYZ point in points)
                {
                    if (point == null) continue;
                    int index = AddCoordinate(point);
                    string id = NodeId(index);
                    if (!ids.Contains(id)) ids.Add(id);
                }
                return string.Join(";", ids);
            }

            public IEnumerable<ExportRow> CreateRows()
            {
                for (int index = 0; index < _points.Count; index++)
                {
                    string nodeId = "N" + (index + 1).ToString(CultureInfo.InvariantCulture);
                    yield return new ExportRow
                    {
                        Kind = "NODE", NodeIds = nodeId,
                        CoordinatesMm = FormatCoordinates(new[] { _points[index] }),
                        Status = _statuses[index]
                    };
                }
            }

            private int AddCoordinate(XYZ point)
            {
                long bx = (long)Math.Floor(point.X / _tolerance);
                long by = (long)Math.Floor(point.Y / _tolerance);
                long bz = (long)Math.Floor(point.Z / _tolerance);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!_buckets.TryGetValue(Key(bx + dx, by + dy, bz + dz), out List<int> nearby)) continue;
                    foreach (int candidate in nearby)
                        if (_points[candidate].DistanceTo(point) <= _tolerance) return candidate;
                }
                return AddNode(point, "COORDINATE_CLUSTER_1MM", true);
            }

            private int AddNode(XYZ point, string status, bool allowCoordinateMatching)
            {
                int index = _points.Count;
                _points.Add(point);
                _statuses.Add(status);
                if (allowCoordinateMatching)
                {
                    long bx = (long)Math.Floor(point.X / _tolerance);
                    long by = (long)Math.Floor(point.Y / _tolerance);
                    long bz = (long)Math.Floor(point.Z / _tolerance);
                    string key = Key(bx, by, bz);
                    if (!_buckets.TryGetValue(key, out List<int> bucket)) _buckets[key] = bucket = new List<int>();
                    bucket.Add(index);
                }
                return index;
            }

            private static string NodeId(int index) => "N" + (index + 1).ToString(CultureInfo.InvariantCulture);

            private static string Key(long x, long y, long z) =>
                x.ToString(CultureInfo.InvariantCulture) + ":" + y.ToString(CultureInfo.InvariantCulture) + ":" + z.ToString(CultureInfo.InvariantCulture);

            public static string FormatCoordinates(IEnumerable<XYZ> points)
            {
                return string.Join(" | ", points.Select(point =>
                    UnitUtils.ConvertFromInternalUnits(point.X, UnitTypeId.Millimeters).ToString("0.###", CultureInfo.InvariantCulture) + ";" +
                    UnitUtils.ConvertFromInternalUnits(point.Y, UnitTypeId.Millimeters).ToString("0.###", CultureInfo.InvariantCulture) + ";" +
                    UnitUtils.ConvertFromInternalUnits(point.Z, UnitTypeId.Millimeters).ToString("0.###", CultureInfo.InvariantCulture)));
            }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<AnalyticalMember> members = new FilteredElementCollector(doc)
                    .OfClass(typeof(AnalyticalMember)).WhereElementIsNotElementType().Cast<AnalyticalMember>()
                    .OrderBy(member => member.Id.Value).ToList();
                List<AnalyticalPanel> panels = new FilteredElementCollector(doc)
                    .OfClass(typeof(AnalyticalPanel)).WhereElementIsNotElementType().Cast<AnalyticalPanel>()
                    .OrderBy(panel => panel.Id.Value).ToList();
                List<AnalyticalOpening> openings = new FilteredElementCollector(doc)
                    .OfClass(typeof(AnalyticalOpening)).WhereElementIsNotElementType().Cast<AnalyticalOpening>()
                    .OrderBy(opening => opening.Id.Value).ToList();
                List<AreaLoad> areaLoads = new FilteredElementCollector(doc).OfClass(typeof(AreaLoad))
                    .WhereElementIsNotElementType().Cast<AreaLoad>().OrderBy(load => load.Id.Value).ToList();
                List<LineLoad> lineLoads = new FilteredElementCollector(doc).OfClass(typeof(LineLoad))
                    .WhereElementIsNotElementType().Cast<LineLoad>().OrderBy(load => load.Id.Value).ToList();
                List<PointLoad> pointLoads = new FilteredElementCollector(doc).OfClass(typeof(PointLoad))
                    .WhereElementIsNotElementType().Cast<PointLoad>().OrderBy(load => load.Id.Value).ToList();
                List<BoundaryConditions> supports = new FilteredElementCollector(doc).OfClass(typeof(BoundaryConditions))
                    .WhereElementIsNotElementType().Cast<BoundaryConditions>().OrderBy(support => support.Id.Value).ToList();
                List<LoadCombination> combinations = new FilteredElementCollector(doc).OfClass(typeof(LoadCombination))
                    .WhereElementIsNotElementType().Cast<LoadCombination>().OrderBy(combination => combination.Id.Value).ToList();
                List<LoadCase> loadCases = new FilteredElementCollector(doc).OfClass(typeof(LoadCase))
                    .WhereElementIsNotElementType().Cast<LoadCase>().OrderBy(loadCase => loadCase.Number).ToList();
                if (members.Count + panels.Count + openings.Count + areaLoads.Count + lineLoads.Count + pointLoads.Count + supports.Count + combinations.Count + loadCases.Count == 0)
                {
                    TaskDialog.Show("KStruct - Xuất mô hình phân tích", "Project chưa có Analytical Member, Panel hoặc Opening.");
                    return Result.Cancelled;
                }

                var rows = new List<ExportRow>();
                var nodes = new NodeCatalog();
                int nonlinear = 0, invalid = 0;
                foreach (AnalyticalMember member in members)
                {
                    Curve curve = null;
                    List<XYZ> points = new List<XYZ>();
                    string status;
                    try
                    {
                        curve = member.GetCurve();
                        if (curve == null || !curve.IsBound) throw new InvalidOperationException("Member không có curve hữu hạn.");
                        points = curve is Line
                            ? new List<XYZ> { curve.GetEndPoint(0), curve.GetEndPoint(1) }
                            : curve.Tessellate().ToList();
                        status = curve is Line ? "STRAIGHT" : "NONLINEAR_TESSELLATED";
                        if (!(curve is Line)) nonlinear++;
                    }
                    catch (Exception ex)
                    {
                        status = "GEOMETRY_ERROR: " + ex.Message;
                        invalid++;
                    }
                    rows.Add(new ExportRow
                    {
                        ElementId = Id(member.Id), Kind = "MEMBER", Level = LevelName(doc, member.LevelId),
                        Role = member.StructuralRole.ToString(), AnalyzeAs = member.AnalyzeAs.ToString(),
                        Material = ElementName(doc, member.MaterialId),
                        Section = ElementName(doc, member.SectionTypeId),
                        RotationDeg = (member.CrossSectionRotation * 180.0 / Math.PI).ToString("0.###", CultureInfo.InvariantCulture),
                        NodeIds = nodes.AddMember(doc, member, points), CoordinatesMm = NodeCatalog.FormatCoordinates(points),
                        Geometry = curve == null ? "" : curve.GetType().Name, Status = status
                    });
                }

                foreach (AnalyticalPanel panel in panels)
                    rows.Add(CreateSurfaceRow(doc, panel, "PANEL", ElementId.InvalidElementId, nodes, ref invalid));
                foreach (AnalyticalOpening opening in openings)
                    rows.Add(CreateSurfaceRow(doc, opening, "OPENING", opening.PanelId, nodes, ref invalid));
                foreach (AreaLoad load in areaLoads) rows.Add(CreateAreaLoadRow(doc, load, nodes, ref invalid));
                foreach (LineLoad load in lineLoads) rows.Add(CreateLineLoadRow(doc, load, nodes, ref invalid));
                foreach (PointLoad load in pointLoads) rows.Add(CreatePointLoadRow(doc, load, nodes));
                foreach (BoundaryConditions support in supports) rows.Add(CreateSupportRow(doc, support, nodes, ref invalid));
                foreach (LoadCombination combination in combinations) rows.Add(CreateCombinationRow(doc, combination));
                foreach (LoadCase loadCase in loadCases) rows.Add(CreateLoadCaseRow(doc, loadCase));

                int elementRowCount = rows.Count;
                rows.AddRange(nodes.CreateRows());
                var dialog = new SaveFileDialog
                {
                    Title = "Xuất mô hình phân tích KStruct",
                    Filter = "CSV UTF-8 (*.csv)|*.csv",
                    FileName = "KStruct_AnalyticalModel.csv",
                    DefaultExt = ".csv",
                    AddExtension = true,
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true) return Result.Cancelled;
                var csv = new StringBuilder("ElementId,Kind,Level,Role,AnalyzeAs,Material,Section,ThicknessMm,RotationDeg,ParentId,HostElementId,WorkPlaneId,LoadCase,LoadData,NodeIds,CoordinatesMm,Geometry,Status\r\n");
                foreach (ExportRow row in rows) csv.AppendLine(row.ToCsv());
                System.IO.File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
                TaskDialog.Show("KStruct - Xuất mô hình phân tích",
                    "Đã xuất " + elementRowCount + " phần tử và " + nodes.Count + " node (" + members.Count + " member, " + panels.Count +
                    " panel, " + openings.Count + " opening, " + (areaLoads.Count + lineLoads.Count + pointLoads.Count) +
                    " load, " + supports.Count + " boundary condition, " + loadCases.Count + " load case, " + combinations.Count + " combination). Curve phi tuyến được tessellate: " + nonlinear +
                    "; lỗi hình học: " + invalid + ".\nFile CSV này là dữ liệu trao đổi, không phải file ETABS .e2k và không tự ánh xạ section/material sang ETABS.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Xuất mô hình phân tích", ex.Message);
                return Result.Failed;
            }
        }

        private static ExportRow CreateSurfaceRow(Document doc, AnalyticalSurfaceBase surface, string kind,
            ElementId parentId, NodeCatalog nodes, ref int invalid)
        {
            var points = new List<XYZ>();
            string status = "OUTER_CONTOUR";
            try
            {
                CurveLoop contour = surface.GetOuterContour();
                if (contour == null) throw new InvalidOperationException("Không có outer contour.");
                foreach (Curve curve in contour)
                {
                    foreach (XYZ point in curve.Tessellate())
                        if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > 1e-8)
                            points.Add(point);
                }
                if (points.Count > 1 && points[0].DistanceTo(points[points.Count - 1]) <= 1e-8)
                    points.RemoveAt(points.Count - 1);
            }
            catch (Exception ex)
            {
                status = "GEOMETRY_ERROR: " + ex.Message;
                invalid++;
            }
            AnalyticalPanel panel = surface as AnalyticalPanel;
            return new ExportRow
            {
                ElementId = Id(surface.Id), Kind = kind, Level = LevelName(doc, surface.LevelId),
                Role = surface.StructuralRole.ToString(), AnalyzeAs = surface.AnalyzeAs.ToString(),
                Material = ElementName(doc, surface.MaterialId),
                ThicknessMm = panel == null ? "" : UnitUtils.ConvertFromInternalUnits(panel.Thickness, UnitTypeId.Millimeters).ToString("0.###", CultureInfo.InvariantCulture),
                ParentId = parentId == ElementId.InvalidElementId ? "" : Id(parentId),
                NodeIds = nodes.AddRange(points), CoordinatesMm = NodeCatalog.FormatCoordinates(points),
                Geometry = "POLYGON", Status = status
            };
        }

        private static ExportRow CreateAreaLoadRow(Document doc, AreaLoad load, NodeCatalog nodes, ref int invalid)
        {
            var points = new List<XYZ>();
            string status = "AREA_LOAD";
            try
            {
                foreach (CurveLoop loop in load.GetLoops())
                    points.AddRange(ReadLoopPoints(loop));
            }
            catch (Exception ex) { status = "GEOMETRY_ERROR: " + ex.Message; invalid++; }
            string references = string.Join(" | ", Enumerable.Range(0, load.NumRefPoints)
                .Select(index => NodeCatalog.FormatCoordinates(new[] { load.GetRefPoint(index) })));
            string forceUnit = Convert.ToString(UnitTypeId.KilonewtonsPerSquareMeter.TypeId, CultureInfo.InvariantCulture);
            return CreateLoadBase(doc, load, "AREA_LOAD", status, points, nodes, "POLYGON",
                "Area_m2=" + UnitUtils.ConvertFromInternalUnits(load.Area, UnitTypeId.SquareMeters).ToString("0.###", CultureInfo.InvariantCulture) +
                ";F1_" + forceUnit + "=" + FormatVector(load.ForceVector1, UnitTypeId.KilonewtonsPerSquareMeter) +
                ";F2_" + forceUnit + "=" + FormatVector(load.ForceVector2, UnitTypeId.KilonewtonsPerSquareMeter) +
                ";F3_" + forceUnit + "=" + FormatVector(load.ForceVector3, UnitTypeId.KilonewtonsPerSquareMeter) +
                ";ReferencePointsMm=" + references + ";Projected=" + load.IsProjected);
        }

        private static ExportRow CreateLineLoadRow(Document doc, LineLoad load, NodeCatalog nodes, ref int invalid)
        {
            List<XYZ> points = new List<XYZ>();
            string status = load.IsUniform ? "UNIFORM_LINE_LOAD" : "VARYING_LINE_LOAD";
            string geometry = "CURVE";
            try
            {
                Curve curve = load.GetCurve();
                if (curve == null || !curve.IsBound) throw new InvalidOperationException("Line load không có curve hữu hạn.");
                points = curve is Line ? new List<XYZ> { curve.GetEndPoint(0), curve.GetEndPoint(1) } : curve.Tessellate().ToList();
                geometry = curve.GetType().Name;
                if (!(curve is Line)) status += "_NONLINEAR_TESSELLATED";
            }
            catch (Exception ex) { status = "GEOMETRY_ERROR: " + ex.Message; invalid++; }
            string forceUnit = Convert.ToString(UnitTypeId.KilonewtonsPerMeter.TypeId, CultureInfo.InvariantCulture);
            string momentUnit = Convert.ToString(UnitTypeId.KilonewtonMetersPerMeter.TypeId, CultureInfo.InvariantCulture);
            string data = "F1_" + forceUnit + "=" + FormatVector(load.ForceVector1, UnitTypeId.KilonewtonsPerMeter) +
                ";F2_" + forceUnit + "=" + FormatVector(load.ForceVector2, UnitTypeId.KilonewtonsPerMeter) +
                ";M1_" + momentUnit + "=" + FormatVector(load.MomentVector1, UnitTypeId.KilonewtonMetersPerMeter) +
                ";M2_" + momentUnit + "=" + FormatVector(load.MomentVector2, UnitTypeId.KilonewtonMetersPerMeter) +
                ";Projected=" + load.IsProjected;
            return CreateLoadBase(doc, load, "LINE_LOAD", status, points, nodes, geometry, data);
        }

        private static ExportRow CreatePointLoadRow(Document doc, PointLoad load, NodeCatalog nodes)
        {
            string data = "Force_kN=" + FormatVector(load.ForceVector, UnitTypeId.Kilonewtons) +
                ";Moment_kNm=" + FormatVector(load.MomentVector, UnitTypeId.KilonewtonMeters);
            return CreateLoadBase(doc, load, "POINT_LOAD", "POINT_LOAD", new[] { load.Point }, nodes, "POINT", data);
        }

        private static ExportRow CreateLoadBase(Document doc, LoadBase load, string kind, string status,
            IEnumerable<XYZ> points, NodeCatalog nodes, string geometry, string loadData)
        {
            List<XYZ> pointList = points.Where(point => point != null).ToList();
            return new ExportRow
            {
                ElementId = Id(load.Id), Kind = kind, Level = LevelName(doc, load.LevelId),
                HostElementId = Id(load.HostElementId), WorkPlaneId = Id(load.WorkPlaneId),
                LoadCase = load.LoadCaseName ?? ElementName(doc, load.LoadCaseId),
                LoadData = "Type=" + ElementName(doc, load.GetTypeId()) + ";Category=" + (load.LoadCategoryName ?? "") +
                    ";Nature=" + (load.LoadNatureName ?? "") + ";OrientTo=" + load.OrientTo +
                    ";Hosted=" + load.IsHosted + ";Reaction=" + load.IsReaction + ";" + loadData,
                NodeIds = nodes.AddRange(pointList), CoordinatesMm = NodeCatalog.FormatCoordinates(pointList),
                Geometry = geometry, Status = status
            };
        }

        private static ExportRow CreateSupportRow(Document doc, BoundaryConditions support, NodeCatalog nodes, ref int invalid)
        {
            var points = new List<XYZ>();
            var status = "BOUNDARY_" + support.GetBoundaryConditionsType();
            try
            {
                XYZ point = support.Point;
                if (point != null) points.Add(point);
                Curve curve = support.GetCurve();
                if (curve != null && curve.IsBound)
                    points.AddRange(curve is Line ? new[] { curve.GetEndPoint(0), curve.GetEndPoint(1) } : curve.Tessellate());
                foreach (CurveLoop loop in support.GetLoops()) points.AddRange(ReadLoopPoints(loop));
            }
            catch (Exception ex) { status = "GEOMETRY_ERROR: " + ex.Message; invalid++; }
            string axes = "";
            try
            {
                Transform transform = support.GetDegreesOfFreedomCoordinateSystem();
                if (transform != null)
                    axes = ";AxisX=" + FormatVector(transform.BasisX, null) + ";AxisY=" + FormatVector(transform.BasisY, null) + ";AxisZ=" + FormatVector(transform.BasisZ, null);
            }
            catch { axes = ";LocalAxes=UNAVAILABLE"; }
            return new ExportRow
            {
                ElementId = Id(support.Id), Kind = "BOUNDARY_CONDITION", HostElementId = Id(support.HostElementId),
                ParentId = Id(support.AssociatedLoadId), LoadData = "GeometryType=" + support.GetBoundaryConditionsType() + axes,
                NodeIds = nodes.AddRange(points), CoordinatesMm = NodeCatalog.FormatCoordinates(points),
                Geometry = points.Count <= 1 ? "POINT" : "BOUNDARY", Status = status
            };
        }

        private static ExportRow CreateCombinationRow(Document doc, LoadCombination combination)
        {
            var components = new List<string>();
            foreach (LoadComponent component in combination.GetComponents())
            {
                try
                {
                    string name = ElementName(doc, component.LoadCaseOrCombinationId);
                    components.Add(name + "*" + component.Factor.ToString("0.######", CultureInfo.InvariantCulture));
                }
                finally { component.Dispose(); }
            }
            return new ExportRow
            {
                ElementId = Id(combination.Id), Kind = "LOAD_COMBINATION", Level = LevelName(doc, combination.LevelId),
                LoadData = "Type=" + combination.Type + ";State=" + combination.State + ";Components=" + string.Join("|", components),
                Status = combination.IsThirdPartyGenerated ? "THIRD_PARTY_GENERATED" : "OK"
            };
        }

        private static ExportRow CreateLoadCaseRow(Document doc, LoadCase loadCase)
        {
            return new ExportRow
            {
                ElementId = Id(loadCase.Id), Kind = "LOAD_CASE", LoadCase = loadCase.Name,
                LoadData = "Number=" + loadCase.Number.ToString(CultureInfo.InvariantCulture) +
                    ";Category=" + ElementName(doc, loadCase.SubcategoryId) +
                    ";Nature=" + ElementName(doc, loadCase.NatureId),
                Status = "OK"
            };
        }

        private static List<XYZ> ReadLoopPoints(CurveLoop loop)
        {
            var points = new List<XYZ>();
            foreach (Curve curve in loop)
                foreach (XYZ point in curve.Tessellate())
                    if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > 1e-8)
                        points.Add(point);
            if (points.Count > 1 && points[0].DistanceTo(points[points.Count - 1]) <= 1e-8)
                points.RemoveAt(points.Count - 1);
            return points;
        }

        private static string FormatVector(XYZ vector, ForgeTypeId unit)
        {
            if (vector == null) return "";
            Func<double, string> format = value => (unit == null ? value : UnitUtils.ConvertFromInternalUnits(value, unit))
                .ToString("0.######", CultureInfo.InvariantCulture);
            return format(vector.X) + ";" + format(vector.Y) + ";" + format(vector.Z);
        }

        private static string LevelName(Document doc, ElementId id)
        {
            Level level = id == ElementId.InvalidElementId ? null : doc.GetElement(id) as Level;
            return level?.Name ?? "";
        }

        private static string ElementName(Document doc, ElementId id)
        {
            Element element = id == ElementId.InvalidElementId ? null : doc.GetElement(id);
            return element?.Name ?? "";
        }

        private static string Id(ElementId id) => id == ElementId.InvalidElementId ? "" : id.Value.ToString(CultureInfo.InvariantCulture);
        private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
