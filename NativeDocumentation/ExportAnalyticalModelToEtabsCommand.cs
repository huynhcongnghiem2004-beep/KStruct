using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace SDK_HD.KStruct.NativeDocumentation
{
    /// <summary>
    /// Creates an ETABS model from Revit analytical members, panels and openings through CSI's COM API.
    /// This intentionally has no compile-time or runtime dependency on any BimSpeed assembly.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ExportAnalyticalModelToEtabsCommand : IExternalCommand
    {
        private const int EtabsUnitsKnM = 6;
        private const int ShellSlab = 0;
        private const int ShellTypeThin = 1;
        private const int EtabsConcrete = 2;
        private const int EtabsSteel = 1;
        private const int EtabsNoDesign = 3;

        private sealed class PointSupportData
        {
            public bool[] Restraints;
            public double[] SpringStiffness;
        }

        private sealed class EtabsJoint
        {
            public string Name;
            public XYZ Point;
        }

        private sealed class FrameSegment
        {
            public string EtabsName;
            public XYZ Start;
            public XYZ End;
        }

        private sealed class MaterialMap
        {
            public string EtabsName;
            public int EtabsType;
            public double YoungModulus;
            public double Poisson;
            public double ThermalExpansion;
            public double WeightDensity;
            public bool IsMapped;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            var dialog = new SaveFileDialog
            {
                Title = "Xuất mô hình phân tích sang ETABS",
                Filter = "ETABS model (*.edb)|*.edb",
                FileName = SafeFileName(doc.Title) + ".edb",
                DefaultExt = ".edb",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true) return Result.Cancelled;

            object etabsObject = null;
            bool keepEtabsOpen = false;
            try
            {
                List<AnalyticalMember> members = new FilteredElementCollector(doc)
                    .OfClass(typeof(AnalyticalMember)).WhereElementIsNotElementType().Cast<AnalyticalMember>().ToList();
                List<AnalyticalPanel> panels = new FilteredElementCollector(doc)
                    .OfClass(typeof(AnalyticalPanel)).WhereElementIsNotElementType().Cast<AnalyticalPanel>().ToList();
                List<AnalyticalOpening> openings = new FilteredElementCollector(doc)
                    .OfClass(typeof(AnalyticalOpening)).WhereElementIsNotElementType().Cast<AnalyticalOpening>().ToList();
                List<LoadCase> loadCases = new FilteredElementCollector(doc)
                    .OfClass(typeof(LoadCase)).WhereElementIsNotElementType().Cast<LoadCase>().ToList();
                List<AreaLoad> areaLoads = new FilteredElementCollector(doc)
                    .OfClass(typeof(AreaLoad)).WhereElementIsNotElementType().Cast<AreaLoad>().ToList();
                List<LineLoad> lineLoads = new FilteredElementCollector(doc)
                    .OfClass(typeof(LineLoad)).WhereElementIsNotElementType().Cast<LineLoad>().ToList();
                List<PointLoad> pointLoads = new FilteredElementCollector(doc)
                    .OfClass(typeof(PointLoad)).WhereElementIsNotElementType().Cast<PointLoad>().ToList();
                List<BoundaryConditions> supports = new FilteredElementCollector(doc)
                    .OfClass(typeof(BoundaryConditions)).WhereElementIsNotElementType().Cast<BoundaryConditions>().ToList();
                List<LoadCombination> combinations = new FilteredElementCollector(doc)
                    .OfClass(typeof(LoadCombination)).WhereElementIsNotElementType().Cast<LoadCombination>().ToList();
                if (members.Count + panels.Count == 0)
                {
                    TaskDialog.Show("KStruct - Xuất ETABS", "Project chưa có Analytical Member hoặc Analytical Panel.");
                    return Result.Cancelled;
                }

                Type etabsType = Type.GetTypeFromProgID("CSI.ETABS.API.ETABSObject", false);
                if (etabsType == null)
                    throw new InvalidOperationException("Không tìm thấy CSI ETABS OAPI trên máy. Cài ETABS và đăng ký CSI API trước khi chạy lệnh này.");
                etabsObject = Activator.CreateInstance(etabsType);
                dynamic etabs = etabsObject;
                Check((int)etabs.ApplicationStart(), "Khởi động ETABS");
                dynamic sap = etabs.SapModel;
                Check((int)sap.InitializeNewModel(EtabsUnitsKnM), "Khởi tạo mô hình ETABS");
                Check((int)sap.File.NewBlank(), "Tạo mô hình ETABS rỗng");
                Check((int)sap.SetPresentUnits(EtabsUnitsKnM), "Đặt đơn vị kN-m");

                var materialMaps = new Dictionary<long, MaterialMap>();
                int createdMembers = 0, createdPanels = 0, createdOpenings = 0;
                int skippedMembers = 0, skippedPanels = 0, skippedOpenings = 0;
                int tessellatedMembers = 0;
                var failures = new List<string>();
                var areaNamesByHostId = new Dictionary<long, string>();
                var frameNamesByHostId = new Dictionary<long, List<FrameSegment>>();
                var loadPatternByCaseId = new Dictionary<long, string>();
                var loadPatternByCaseName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                AnalyticalToPhysicalAssociationManager associationManager =
                    AnalyticalToPhysicalAssociationManager.GetAnalyticalToPhysicalAssociationManager(doc);
                int createdAreaLoads = 0, skippedAreaLoads = 0, createdLineLoads = 0, skippedLineLoads = 0;
                int createdPointLoads = 0, skippedPointLoads = 0, createdSupports = 0, skippedSupports = 0, createdSpringSupports = 0;
                int createdCombinations = 0, skippedCombinations = 0;
                foreach (LoadCase loadCase in loadCases)
                {
                    try
                    {
                        string patternName = "KS_LC_" + loadCase.Id.Value.ToString(CultureInfo.InvariantCulture) + "_" + SafeFileName(loadCase.Name);
                        Check((int)sap.LoadPatterns.Add(patternName, EtabsLoadPatternType(doc, loadCase), 0.0, true),
                            "Tạo load pattern " + patternName);
                        loadPatternByCaseId[loadCase.Id.Value] = patternName;
                        loadPatternByCaseName[loadCase.Name] = patternName;
                    }
                    catch (Exception ex) { failures.Add("Load case " + loadCase.Name + ": " + ex.Message); }
                }

                foreach (AnalyticalMember member in members)
                {
                    try
                    {
                        Curve curve = member.GetCurve();
                        if (curve == null || !curve.IsBound) throw new InvalidOperationException("Không có đường phân tích hữu hạn.");
                        List<XYZ> points = curve is Line
                            ? new List<XYZ> { curve.GetEndPoint(0), curve.GetEndPoint(1) }
                            : curve.Tessellate().ToList();
                        if (points.Count < 2) throw new InvalidOperationException("Không đủ điểm hình học.");
                        if (!(curve is Line)) tessellatedMembers++;

                        Element section = member.SectionTypeId == ElementId.InvalidElementId ? null : doc.GetElement(member.SectionTypeId);
                        double width, depth;
                        if (!TryGetSection(section, out width, out depth))
                            throw new InvalidOperationException("Không đọc được chiều rộng/chiều cao tiết diện từ Section Type.");
                        MaterialMap material = GetMaterialMap(doc, member.MaterialId, materialMaps);
                        if (!material.IsMapped) throw new InvalidOperationException("Material không có Structural Asset với modulus hợp lệ.");
                        DefineMaterial(sap, material);

                        string propertyName = "KS_F_" + member.Id.Value.ToString(CultureInfo.InvariantCulture);
                        Check((int)sap.PropFrame.SetRectangle(propertyName, material.EtabsName, width, depth,
                            -1, "KStruct Revit analytical member " + member.Id.Value, ""), "Tạo tiết diện " + propertyName);
                        double rotation = member.CrossSectionRotation * 180.0 / Math.PI;
                        for (int i = 1; i < points.Count; i++)
                        {
                            XYZ a = points[i - 1], b = points[i];
                            if (a.DistanceTo(b) <= doc.Application.ShortCurveTolerance) continue;
                            string objectName = "";
                            Check((int)sap.FrameObj.AddByCoord(Meters(a.X), Meters(a.Y), Meters(a.Z),
                                Meters(b.X), Meters(b.Y), Meters(b.Z), ref objectName, propertyName,
                                "KS_RVT_" + member.Id.Value.ToString(CultureInfo.InvariantCulture) + "_" + i, "Global"),
                                "Tạo frame Revit " + member.Id.Value);
                            MapFrameObject(associationManager, member.Id, objectName, a, b, frameNamesByHostId);
                            if (Math.Abs(rotation) > 1e-7)
                                Check((int)sap.FrameObj.SetLocalAxes(objectName, rotation, 0), "Đặt góc tiết diện " + objectName);
                            createdMembers++;
                        }
                    }
                    catch (Exception ex)
                    {
                        skippedMembers++;
                        failures.Add("Member " + member.Id.Value + ": " + ex.Message);
                    }
                }

                foreach (AnalyticalPanel panel in panels)
                {
                    try
                    {
                        List<XYZ> points = GetContour(panel.GetOuterContour());
                        if (points.Count < 3) throw new InvalidOperationException("Biên panel không tạo được đa giác.");
                        if (panel.Thickness <= doc.Application.ShortCurveTolerance) throw new InvalidOperationException("Chiều dày panel không hợp lệ.");
                        MaterialMap material = GetMaterialMap(doc, panel.MaterialId, materialMaps);
                        if (!material.IsMapped) throw new InvalidOperationException("Material không có Structural Asset với modulus hợp lệ.");
                        DefineMaterial(sap, material);
                        string propertyName = "KS_A_" + panel.Id.Value.ToString(CultureInfo.InvariantCulture);
                        Check((int)sap.PropArea.SetSlab(propertyName, ShellSlab, ShellTypeThin,
                            material.EtabsName, Meters(panel.Thickness), -1,
                            "KStruct Revit analytical panel " + panel.Id.Value, ""), "Tạo area property " + propertyName);
                        string areaName = AddArea(sap, points, propertyName, "KS_RVT_PANEL_" + panel.Id.Value.ToString(CultureInfo.InvariantCulture));
                        areaNamesByHostId[panel.Id.Value] = areaName;
                        MapPhysicalAssociations(associationManager, panel.Id, areaName, areaNamesByHostId);
                        createdPanels++;
                    }
                    catch (Exception ex)
                    {
                        skippedPanels++;
                        failures.Add("Panel " + panel.Id.Value + ": " + ex.Message);
                    }
                }

                foreach (AnalyticalOpening opening in openings)
                {
                    try
                    {
                        List<XYZ> points = GetContour(opening.GetOuterContour());
                        if (points.Count < 3) throw new InvalidOperationException("Biên opening không tạo được đa giác.");
                        string name = AddArea(sap, points, "None", "KS_RVT_OPENING_" + opening.Id.Value.ToString(CultureInfo.InvariantCulture));
                        Check((int)sap.AreaObj.SetOpening(name, true, 0), "Đánh dấu opening " + opening.Id.Value);
                        createdOpenings++;
                    }
                    catch (Exception ex)
                    {
                        skippedOpenings++;
                        failures.Add("Opening " + opening.Id.Value + ": " + ex.Message);
                    }
                }

                foreach (AreaLoad load in areaLoads)
                {
                    try
                    {
                        string areaName;
                        if (!areaNamesByHostId.TryGetValue(load.HostElementId.Value, out areaName))
                            throw new InvalidOperationException("Không tìm được area ETABS tương ứng với HostElementId " + load.HostElementId.Value + ".");
                        string patternName;
                        if (!TryGetLoadPattern(load, loadPatternByCaseId, loadPatternByCaseName, out patternName))
                            throw new InvalidOperationException("LoadCase chưa được ánh xạ sang ETABS.");
                        if (load.OrientTo != LoadOrientTo.Project)
                            throw new InvalidOperationException("Chỉ xuất AreaLoad theo Project/global; WorkPlane và HostLocal cần chuyển hệ trục.");
                        XYZ v1 = load.ForceVector1, v2 = load.ForceVector2, v3 = load.ForceVector3;
                        if (!VectorsNearlyEqual(v1, v2) || !VectorsNearlyEqual(v1, v3))
                            throw new InvalidOperationException("AreaLoad không đều theo các reference point; ETABS uniform load không giữ được phân bố này.");
                        SetAreaVectorLoad(sap, areaName, patternName, v1);
                        createdAreaLoads++;
                    }
                    catch (Exception ex) { skippedAreaLoads++; failures.Add("AreaLoad " + load.Id.Value + ": " + ex.Message); }
                }

                foreach (LineLoad load in lineLoads)
                {
                    try
                    {
                        if (load.OrientTo != LoadOrientTo.Project)
                            throw new InvalidOperationException("Chỉ xuất LineLoad theo Project/global; WorkPlane và HostLocal cần chuyển hệ trục.");
                        string patternName;
                        if (!TryGetLoadPattern(load, loadPatternByCaseId, loadPatternByCaseName, out patternName))
                            throw new InvalidOperationException("LoadCase chưa được ánh xạ sang ETABS.");
                        List<FrameSegment> hostFrames;
                        if (!frameNamesByHostId.TryGetValue(load.HostElementId.Value, out hostFrames))
                            throw new InvalidOperationException("Host không có frame ETABS tương ứng.");
                        Curve loadCurve = load.GetCurve();
                        if (!(loadCurve is Line)) throw new InvalidOperationException("Chỉ ánh xạ LineLoad nằm trên đường thẳng.");
                        FrameSegment frame;
                        double dist1, dist2;
                        bool reverse;
                        if (!FindFrameForLineLoad(hostFrames, loadCurve.GetEndPoint(0), loadCurve.GetEndPoint(1),
                            out frame, out dist1, out dist2, out reverse))
                            throw new InvalidOperationException("LineLoad phải nằm trọn trên một ETABS frame thẳng.");
                        XYZ force1 = reverse ? load.ForceVector2 : load.ForceVector1;
                        XYZ force2 = reverse ? load.ForceVector1 : load.ForceVector2;
                        XYZ moment1 = reverse ? load.MomentVector2 : load.MomentVector1;
                        XYZ moment2 = reverse ? load.MomentVector1 : load.MomentVector2;
                        bool forceAssigned = SetFrameDistributedVector(sap, frame.EtabsName, patternName, force1, force2,
                            dist1, dist2, false, true);
                        bool momentAssigned = SetFrameDistributedVector(sap, frame.EtabsName, patternName, moment1, moment2,
                            dist1, dist2, true, !forceAssigned);
                        if (!forceAssigned && !momentAssigned) throw new InvalidOperationException("Vector lực và moment đều bằng 0.");
                        createdLineLoads++;
                    }
                    catch (Exception ex) { skippedLineLoads++; failures.Add("LineLoad " + load.Id.Value + ": " + ex.Message); }
                }

                foreach (PointLoad load in pointLoads)
                {
                    try
                    {
                        if (load.OrientTo != LoadOrientTo.Project)
                            throw new InvalidOperationException("Chỉ xuất PointLoad theo Project/global; WorkPlane và HostLocal cần chuyển hệ trục.");
                        string patternName;
                        if (!TryGetLoadPattern(load, loadPatternByCaseId, loadPatternByCaseName, out patternName))
                            throw new InvalidOperationException("LoadCase chưa được ánh xạ sang ETABS.");
                        List<FrameSegment> hostFrames;
                        if (!frameNamesByHostId.TryGetValue(load.HostElementId.Value, out hostFrames))
                            throw new InvalidOperationException("PointLoad host không có frame ETABS tương ứng; chỉ hỗ trợ điểm nằm trên member.");
                        FrameSegment frame;
                        double distance;
                        if (!FindFrameForPointLoad(hostFrames, load.Point, out frame, out distance))
                            throw new InvalidOperationException("Không xác định được duy nhất một frame chứa PointLoad.");
                        bool forceAssigned = SetFramePointVector(sap, frame.EtabsName, patternName, load.ForceVector, distance, false, true);
                        bool momentAssigned = SetFramePointVector(sap, frame.EtabsName, patternName, load.MomentVector, distance, true, !forceAssigned);
                        if (!forceAssigned && !momentAssigned) throw new InvalidOperationException("Vector lực và moment đều bằng 0.");
                        createdPointLoads++;
                    }
                    catch (Exception ex) { skippedPointLoads++; failures.Add("PointLoad " + load.Id.Value + ": " + ex.Message); }
                }
                List<EtabsJoint> etabsJoints = supports.Count == 0 ? new List<EtabsJoint>() : GetEtabsJoints(sap);
                foreach (BoundaryConditions support in supports)
                {
                    try
                    {
                        if (support.GetBoundaryConditionsType() != BoundaryConditionsType.Point)
                            throw new InvalidOperationException("Chỉ chuyển BoundaryCondition dạng Point; Line/Area chưa được ánh xạ.");
                        if (support.GetOrientTo() != BoundaryConditionsOrientTo.Project)
                            throw new InvalidOperationException("BoundaryCondition HostLocal chưa được chuyển hệ trục.");
                        if (support.Point == null) throw new InvalidOperationException("Không có tọa độ gối.");
                        PointSupportData supportData = GetPointSupportData(support);
                        bool hasRestraint = supportData.Restraints.Any(value => value);
                        bool hasSpring = supportData.SpringStiffness.Any(value => value > 0);
                        if (!hasRestraint && !hasSpring)
                            throw new InvalidOperationException("Gối không có bậc tự do Fixed hoặc Spring để chuyển.");
                        string jointName = FindEtabsJoint(etabsJoints, support.Point);
                        if (jointName == null) throw new InvalidOperationException("Không tìm thấy một ETABS joint duy nhất tại tọa độ gối.");
                        if (hasRestraint)
                            Check((int)sap.PointObj.SetRestraint(jointName, ref supportData.Restraints, 0),
                                "Gán restraint cho ETABS joint " + jointName);
                        if (hasSpring)
                        {
                            Check((int)sap.PointObj.SetSpring(jointName, ref supportData.SpringStiffness, 0, false, true),
                                "Gán spring cho ETABS joint " + jointName);
                            createdSpringSupports++;
                        }
                        createdSupports++;
                    }
                    catch (Exception ex) { skippedSupports++; failures.Add("BoundaryCondition " + support.Id.Value + ": " + ex.Message); }
                }
                var combinationNames = combinations.ToDictionary(c => c.Id.Value,
                    c => "KS_COMB_" + c.Id.Value.ToString(CultureInfo.InvariantCulture) + "_" + SafeFileName(c.Name));
                var createdCombinationIds = new HashSet<long>();
                foreach (LoadCombination combination in combinations)
                {
                    try
                    {
                        string comboName = combinationNames[combination.Id.Value];
                        int comboType = combination.Type == LoadCombinationType.Envelope ? 1 : 0;
                        Check((int)sap.RespCombo.Add(comboName, comboType), "Tạo load combination " + comboName);
                        createdCombinationIds.Add(combination.Id.Value);
                    }
                    catch (Exception ex) { skippedCombinations++; failures.Add("Combination " + combination.Name + ": " + ex.Message); }
                }
                foreach (LoadCombination combination in combinations.Where(c => createdCombinationIds.Contains(c.Id.Value)))
                {
                    try
                    {
                        string comboName = combinationNames[combination.Id.Value];
                        foreach (LoadComponent component in combination.GetComponents())
                        {
                            try
                            {
                                string componentName;
                                int nameType;
                                if (combinationNames.TryGetValue(component.LoadCaseOrCombinationId.Value, out componentName) &&
                                    createdCombinationIds.Contains(component.LoadCaseOrCombinationId.Value)) nameType = 1;
                                else if (loadPatternByCaseId.TryGetValue(component.LoadCaseOrCombinationId.Value, out componentName)) nameType = 0;
                                else throw new InvalidOperationException("Không ánh xạ được thành phần " + component.LoadCaseOrCombinationId.Value + ".");
                                Check((int)sap.RespCombo.SetCaseList(comboName, ref nameType, componentName, component.Factor),
                                    "Gán thành phần " + componentName + " vào " + comboName);
                            }
                            finally { component.Dispose(); }
                        }
                        createdCombinations++;
                    }
                    catch (Exception ex) { skippedCombinations++; failures.Add("Combination " + combination.Name + ": " + ex.Message); }
                }
                if (createdMembers + createdPanels == 0)
                    throw new InvalidOperationException("Không xuất được member hoặc panel nào; chưa tạo file ETABS rỗng.");
                Check((int)sap.File.Save(dialog.FileName), "Lưu file ETABS");
                keepEtabsOpen = true;
                string report = "Đã lưu mô hình ETABS: " + dialog.FileName + "\n" +
                    "Đơn vị: kN-m. Member/frame: " + createdMembers + " (bỏ qua " + skippedMembers + "); panel: " + createdPanels +
                    " (bỏ qua " + skippedPanels + "); opening: " + createdOpenings + " (bỏ qua " + skippedOpenings + "); AreaLoad: " + createdAreaLoads + " (bỏ qua " + skippedAreaLoads + "); LineLoad: " + createdLineLoads + " (bỏ qua " + skippedLineLoads + "); PointLoad: " + createdPointLoads + " (bỏ qua " + skippedPointLoads + "); gối point: " + createdSupports + " (bỏ qua " + skippedSupports + ", có spring " + createdSpringSupports + "); tổ hợp tải: " + createdCombinations + " (bỏ qua " + skippedCombinations + ").\n\n" +
                    "Lệnh hiện chuyển hình học, tiết diện chữ nhật đọc được từ Revit, vật liệu từ Structural Asset và opening. " +
                    "Member cong được chia đoạn thẳng: " + tessellatedMembers + ". Load case và AreaLoad theo phương Project được chuyển; self-weight multiplier để 0 tránh nhân đôi. LineLoad/PointLoad không theo Project, tải đường không nằm trọn trên một frame, và tải điểm không nằm trên frame đều bị bỏ qua và ghi vào báo cáo. Gối point Fixed/Released theo Project được chuyển vào joint ETABS; line/area và HostLocal bị bỏ qua. Translational springs được chuyển sang kN/m; rotational springs được quy đổi từ kN-m/degree sang kN-m/radian. Hãy kiểm tra mô hình trong ETABS trước khi phân tích.";
                if (failures.Count > 0) report += "\n\nLỗi đầu tiên:\n" + string.Join("\n", failures.Take(8));
                TaskDialog.Show("KStruct - Xuất ETABS", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Xuất ETABS", ex.Message);
                return Result.Failed;
            }
            finally
            {
                if (etabsObject != null && !keepEtabsOpen)
                {
                    try { ((dynamic)etabsObject).ApplicationExit(false); } catch { }
                }
                if (etabsObject != null && Marshal.IsComObject(etabsObject))
                    try { Marshal.ReleaseComObject(etabsObject); } catch { }
            }
        }

        private static List<EtabsJoint> GetEtabsJoints(dynamic sap)
        {
            int count = 0;
            string[] names = null;
            Check((int)sap.PointObj.GetNameList(ref count, ref names), "Đọc danh sách ETABS joint");
            var joints = new List<EtabsJoint>();
            if (names == null) return joints;
            foreach (string name in names.Take(count))
            {
                double x = 0, y = 0, z = 0;
                Check((int)sap.PointObj.GetCoordCartesian(name, ref x, ref y, ref z, "Global"),
                    "Đọc tọa độ ETABS joint " + name);
                joints.Add(new EtabsJoint
                {
                    Name = name,
                    Point = new XYZ(UnitUtils.ConvertToInternalUnits(x, UnitTypeId.Meters),
                        UnitUtils.ConvertToInternalUnits(y, UnitTypeId.Meters),
                        UnitUtils.ConvertToInternalUnits(z, UnitTypeId.Meters))
                });
            }
            return joints;
        }

        private static PointSupportData GetPointSupportData(BoundaryConditions support)
        {
            BuiltInParameter[] fixedParameters =
            {
                BuiltInParameter.BOUNDARY_X_TRANSLATION_FIXED,
                BuiltInParameter.BOUNDARY_Y_TRANSLATION_FIXED,
                BuiltInParameter.BOUNDARY_Z_TRANSLATION_FIXED,
                BuiltInParameter.BOUNDARY_X_ROTATION_FIXED,
                BuiltInParameter.BOUNDARY_Y_ROTATION_FIXED,
                BuiltInParameter.BOUNDARY_Z_ROTATION_FIXED
            };
            BuiltInParameter[] springParameters =
            {
                BuiltInParameter.BOUNDARY_X_TRANSLATION_SPRING,
                BuiltInParameter.BOUNDARY_Y_TRANSLATION_SPRING,
                BuiltInParameter.BOUNDARY_Z_TRANSLATION_SPRING,
                BuiltInParameter.BOUNDARY_X_ROTATION_SPRING,
                BuiltInParameter.BOUNDARY_Y_ROTATION_SPRING,
                BuiltInParameter.BOUNDARY_Z_ROTATION_SPRING
            };
            BuiltInParameter[] stiffnessParameters =
            {
                BuiltInParameter.BOUNDARY_RESTRAINT_X,
                BuiltInParameter.BOUNDARY_RESTRAINT_Y,
                BuiltInParameter.BOUNDARY_RESTRAINT_Z,
                BuiltInParameter.BOUNDARY_RESTRAINT_ROT_X,
                BuiltInParameter.BOUNDARY_RESTRAINT_ROT_Y,
                BuiltInParameter.BOUNDARY_RESTRAINT_ROT_Z
            };
            var result = new PointSupportData { Restraints = new bool[6], SpringStiffness = new double[6] };
            for (int i = 0; i < 6; i++)
            {
                Parameter fixedParameter = support.get_Parameter(fixedParameters[i]);
                Parameter springParameter = support.get_Parameter(springParameters[i]);
                if (fixedParameter == null || springParameter == null || !fixedParameter.HasValue || !springParameter.HasValue)
                    throw new InvalidOperationException("Thiếu tham số Fixed/Spring bậc tự do " + i + ".");
                bool isFixed = fixedParameter.AsInteger() != 0;
                bool isSpring = springParameter.AsInteger() != 0;
                if (isFixed && isSpring)
                    throw new InvalidOperationException("Revit đánh dấu đồng thời Fixed và Spring tại bậc tự do " + i + ".");
                result.Restraints[i] = isFixed;
                if (!isSpring) continue;
                Parameter stiffness = support.get_Parameter(stiffnessParameters[i]);
                if (stiffness == null || !stiffness.HasValue)
                    throw new InvalidOperationException("Thiếu modulus spring tại bậc tự do " + i + ".");
                double value = stiffness.AsDouble();
                if (i < 3)
                    result.SpringStiffness[i] = UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.KilonewtonsPerMeter);
                else
                    result.SpringStiffness[i] = UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.KilonewtonMetersPerDegree) * (180.0 / Math.PI);
                if (result.SpringStiffness[i] <= 0)
                    throw new InvalidOperationException("Spring stiffness phải lớn hơn 0 tại bậc tự do " + i + ".");
            }
            return result;
        }
        private static string FindEtabsJoint(IList<EtabsJoint> joints, XYZ point)
        {
            double tolerance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
            List<Tuple<EtabsJoint, double>> matches = joints
                .Select(joint => Tuple.Create(joint, joint.Point.DistanceTo(point)))
                .Where(item => item.Item2 <= tolerance)
                .OrderBy(item => item.Item2).ToList();
            if (matches.Count == 0) return null;
            if (matches.Count > 1 && matches[1].Item2 - matches[0].Item2 <= tolerance * 0.1) return null;
            return matches[0].Item1.Name;
        }
        private static void MapFrameObject(AnalyticalToPhysicalAssociationManager manager, ElementId analyticalId,
            string etabsName, XYZ start, XYZ end, IDictionary<long, List<FrameSegment>> namesByHostId)
        {
            var segment = new FrameSegment { EtabsName = etabsName, Start = start, End = end };
            AddFrameMap(namesByHostId, analyticalId.Value, segment);
            if (manager == null || !manager.HasAssociation(analyticalId)) return;
            foreach (ElementId physicalId in manager.GetAssociatedElementIds(analyticalId))
                AddFrameMap(namesByHostId, physicalId.Value, segment);
        }

        private static void AddFrameMap(IDictionary<long, List<FrameSegment>> map, long hostId, FrameSegment segment)
        {
            List<FrameSegment> list;
            if (!map.TryGetValue(hostId, out list)) map[hostId] = list = new List<FrameSegment>();
            if (!list.Any(existing => existing.EtabsName == segment.EtabsName)) list.Add(segment);
        }

        private static bool FindFrameForLineLoad(IList<FrameSegment> frames, XYZ loadStart, XYZ loadEnd,
            out FrameSegment match, out double distance1, out double distance2, out bool reverse)
        {
            match = null; distance1 = distance2 = 0; reverse = false;
            var candidates = new List<Tuple<FrameSegment, double, double>>();
            foreach (FrameSegment frame in frames)
            {
                double t1, t2;
                if (!TrySegmentParameter(frame, loadStart, out t1) || !TrySegmentParameter(frame, loadEnd, out t2)) continue;
                if (Math.Abs(t2 - t1) <= 1e-9) continue;
                candidates.Add(Tuple.Create(frame, t1, t2));
            }
            if (candidates.Count != 1) return false;
            match = candidates[0].Item1;
            reverse = candidates[0].Item2 > candidates[0].Item3;
            distance1 = Math.Min(candidates[0].Item2, candidates[0].Item3);
            distance2 = Math.Max(candidates[0].Item2, candidates[0].Item3);
            return true;
        }

        private static bool FindFrameForPointLoad(IList<FrameSegment> frames, XYZ point,
            out FrameSegment match, out double distance)
        {
            match = null; distance = 0;
            var candidates = new List<Tuple<FrameSegment, double>>();
            foreach (FrameSegment frame in frames)
            {
                double t;
                if (TrySegmentParameter(frame, point, out t)) candidates.Add(Tuple.Create(frame, t));
            }
            if (candidates.Count != 1) return false;
            match = candidates[0].Item1; distance = candidates[0].Item2;
            return true;
        }

        private static bool TrySegmentParameter(FrameSegment frame, XYZ point, out double parameter)
        {
            XYZ axis = frame.End - frame.Start;
            double lengthSquared = axis.DotProduct(axis);
            if (lengthSquared <= 1e-16) { parameter = 0; return false; }
            parameter = (point - frame.Start).DotProduct(axis) / lengthSquared;
            double tolerance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
            if (parameter < -1e-8 || parameter > 1.0 + 1e-8) return false;
            XYZ projected = frame.Start + axis * Math.Max(0.0, Math.Min(1.0, parameter));
            return projected.DistanceTo(point) <= tolerance;
        }

        private static bool SetFrameDistributedVector(dynamic sap, string frameName, string patternName,
            XYZ start, XYZ end, double distance1, double distance2, bool isMoment, bool replace)
        {
            double[] first = { start.X, start.Y, start.Z };
            double[] second = { end.X, end.Y, end.Z };
            int[] directions = { 4, 5, 6 };
            ForgeTypeId unit = isMoment ? UnitTypeId.KilonewtonMetersPerMeter : UnitTypeId.KilonewtonsPerMeter;
            bool assigned = false;
            int myType = isMoment ? 2 : 1;
            for (int i = 0; i < first.Length; i++)
            {
                double value1 = UnitUtils.ConvertFromInternalUnits(first[i], unit);
                double value2 = UnitUtils.ConvertFromInternalUnits(second[i], unit);
                if (Math.Abs(value1) <= 1e-12 && Math.Abs(value2) <= 1e-12) continue;
                Check((int)sap.FrameObj.SetLoadDistributed(frameName, patternName, myType, directions[i],
                    distance1, distance2, value1, value2, "Global", true, replace, 0),
                    "Gán tải phân bố " + patternName + " vào " + frameName);
                replace = false;
                assigned = true;
            }
            return assigned;
        }

        private static bool SetFramePointVector(dynamic sap, string frameName, string patternName,
            XYZ vector, double distance, bool isMoment, bool replace)
        {
            double[] values = { vector.X, vector.Y, vector.Z };
            int[] directions = { 4, 5, 6 };
            ForgeTypeId unit = isMoment ? UnitTypeId.KilonewtonMeters : UnitTypeId.Kilonewtons;
            bool assigned = false;
            int myType = isMoment ? 2 : 1;
            for (int i = 0; i < values.Length; i++)
            {
                double value = UnitUtils.ConvertFromInternalUnits(values[i], unit);
                if (Math.Abs(value) <= 1e-12) continue;
                Check((int)sap.FrameObj.SetLoadPoint(frameName, patternName, myType, directions[i],
                    distance, value, "Global", true, replace, 0),
                    "Gán tải điểm " + patternName + " vào " + frameName);
                replace = false;
                assigned = true;
            }
            return assigned;
        }
        private static void MapPhysicalAssociations(AnalyticalToPhysicalAssociationManager manager, ElementId analyticalId,
            string etabsName, IDictionary<long, string> namesByHostId)
        {
            namesByHostId[analyticalId.Value] = etabsName;
            if (manager == null || !manager.HasAssociation(analyticalId)) return;
            foreach (ElementId physicalId in manager.GetAssociatedElementIds(analyticalId))
                namesByHostId[physicalId.Value] = etabsName;
        }

        private static int EtabsLoadPatternType(Document doc, LoadCase loadCase)
        {
            Element category = loadCase.SubcategoryId == ElementId.InvalidElementId ? null : doc.GetElement(loadCase.SubcategoryId);
            string name = (category?.Name ?? "") + " " + loadCase.Name;
            name = name.ToLowerInvariant();
            if (name.Contains("dead") || name.Contains("permanent")) return 1;
            if (name.Contains("live") || name.Contains("imposed")) return 3;
            if (name.Contains("wind")) return 6;
            if (name.Contains("snow")) return 7;
            if (name.Contains("roof")) return 11;
            if (name.Contains("seismic") || name.Contains("earthquake") || name.Contains("quake")) return 5;
            if (name.Contains("temperature") || name.Contains("thermal")) return 10;
            return 8;
        }

        private static bool TryGetLoadPattern(LoadBase load, IDictionary<long, string> byId,
            IDictionary<string, string> byName, out string patternName)
        {
            if (load.LoadCaseId != ElementId.InvalidElementId && byId.TryGetValue(load.LoadCaseId.Value, out patternName)) return true;
            if (!string.IsNullOrWhiteSpace(load.LoadCaseName) && byName.TryGetValue(load.LoadCaseName, out patternName)) return true;
            patternName = null;
            return false;
        }

        private static void SetAreaVectorLoad(dynamic sap, string areaName, string patternName, XYZ vector)
        {
            double[] values = { vector.X, vector.Y, vector.Z };
            int[] directions = { 4, 5, 6 };
            bool replace = true;
            bool assigned = false;
            for (int i = 0; i < values.Length; i++)
            {
                double value = UnitUtils.ConvertFromInternalUnits(values[i], UnitTypeId.KilonewtonsPerSquareMeter);
                if (Math.Abs(value) <= 1e-12) continue;
                Check((int)sap.AreaObj.SetLoadUniform(areaName, patternName, value, directions[i],
                    replace, "Global", 0), "Gán AreaLoad " + patternName + " vào " + areaName);
                replace = false;
                assigned = true;
            }
            if (!assigned) throw new InvalidOperationException("Vector tải AreaLoad bằng 0.");
        }

        private static bool VectorsNearlyEqual(XYZ a, XYZ b) => a != null && b != null && a.DistanceTo(b) <= 1e-8;
        private static MaterialMap GetMaterialMap(Document doc, ElementId materialId, IDictionary<long, MaterialMap> cache)
        {
            long key = materialId == ElementId.InvalidElementId ? -1 : materialId.Value;
            MaterialMap cached;
            if (cache.TryGetValue(key, out cached)) return cached;
            var result = new MaterialMap { IsMapped = false };
            Material material = key < 0 ? null : doc.GetElement(materialId) as Material;
            PropertySetElement propertySet = material == null || material.StructuralAssetId == ElementId.InvalidElementId
                ? null : doc.GetElement(material.StructuralAssetId) as PropertySetElement;
            StructuralAsset asset = propertySet?.GetStructuralAsset();
            if (material != null && asset != null)
            {
                double e = UnitUtils.ConvertFromInternalUnits(asset.YoungModulus.X, UnitTypeId.KilonewtonsPerSquareMeter);
                double nu = asset.PoissonRatio.X;
                double alpha = asset.ThermalExpansionCoefficient.X;
                if (e > 0 && nu > -0.99 && nu < 0.49)
                {
                    result = new MaterialMap
                    {
                        EtabsName = "KS_M_" + material.Id.Value.ToString(CultureInfo.InvariantCulture),
                        EtabsType = asset.StructuralAssetClass == StructuralAssetClass.Concrete ? EtabsConcrete :
                            asset.StructuralAssetClass == StructuralAssetClass.Metal ? EtabsSteel : EtabsNoDesign,
                        YoungModulus = e, Poisson = nu, ThermalExpansion = alpha,
                        WeightDensity = Math.Max(0.0, UnitUtils.ConvertFromInternalUnits(asset.Density, UnitTypeId.KilogramsPerCubicMeter) * 9.80665 / 1000.0),
                        IsMapped = true
                    };
                }
            }
            cache[key] = result;
            return result;
        }

        private static void DefineMaterial(dynamic sap, MaterialMap material)
        {
            if (!material.IsMapped) throw new InvalidOperationException("Không ánh xạ được vật liệu.");
            Check((int)sap.PropMaterial.SetMaterial(material.EtabsName, material.EtabsType, -1,
                "Mapped from Revit Structural Asset", ""), "Tạo vật liệu " + material.EtabsName);
            Check((int)sap.PropMaterial.SetMPIsotropic(material.EtabsName, material.YoungModulus,
                material.Poisson, material.ThermalExpansion, 0.0), "Gán cơ tính " + material.EtabsName);
            Check((int)sap.PropMaterial.SetWeightAndMass(material.EtabsName, 1, material.WeightDensity, 0.0),
                "Gán trọng lượng riêng " + material.EtabsName);
        }

        private static bool TryGetSection(Element section, out double width, out double depth)
        {
            width = depth = 0;
            if (section == null) return false;
            Parameter w = section.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
            Parameter h = section.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);
            if (w == null || h == null || !w.HasValue || !h.HasValue) return false;
            width = Meters(w.AsDouble());
            depth = Meters(h.AsDouble());
            return width > 1e-4 && depth > 1e-4;
        }

        private static string AddArea(dynamic sap, IList<XYZ> points, string property, string userName)
        {
            double[] x = points.Select(p => Meters(p.X)).ToArray();
            double[] y = points.Select(p => Meters(p.Y)).ToArray();
            double[] z = points.Select(p => Meters(p.Z)).ToArray();
            string name = "";
            Check((int)sap.AreaObj.AddByCoord(points.Count, ref x, ref y, ref z, ref name,
                property, userName, "Global"), "Tạo area " + userName);
            return name;
        }

        private static List<XYZ> GetContour(CurveLoop contour)
        {
            if (contour == null) throw new InvalidOperationException("Không có outer contour.");
            var points = new List<XYZ>();
            foreach (Curve curve in contour)
                foreach (XYZ point in curve.Tessellate())
                    if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > 1e-8)
                        points.Add(point);
            if (points.Count > 1 && points[0].DistanceTo(points[points.Count - 1]) <= 1e-8)
                points.RemoveAt(points.Count - 1);
            return points;
        }

        private static double Meters(double internalFeet) => UnitUtils.ConvertFromInternalUnits(internalFeet, UnitTypeId.Meters);

        private static string SafeFileName(string title)
        {
            string name = string.IsNullOrWhiteSpace(title) ? "KStruct_AnalyticalModel" : title;
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static void Check(int result, string operation)
        {
            if (result != 0) throw new InvalidOperationException(operation + " thất bại (CSI ETABS API code " + result + ").");
        }
    }
}












