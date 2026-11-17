using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CalculateFormworkAreaCommand : IExternalCommand
    {
        private sealed class QuantityRow
        {
            public long ElementId;
            public string Category;
            public string TypeName;
            public double FormworkAreaSquareMeters;
            public double VolumeCubicMeters;
            public int IncludedFaceCount;
            public string Note;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                HashSet<long> categories = new HashSet<long>
                {
                    (long)BuiltInCategory.OST_StructuralColumns,
                    (long)BuiltInCategory.OST_StructuralFraming,
                    (long)BuiltInCategory.OST_Floors,
                    (long)BuiltInCategory.OST_Walls,
                    (long)BuiltInCategory.OST_StructuralFoundation
                };
                List<Element> candidates = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(element => element.Category != null && categories.Contains(element.Category.Id.Value))
                    .ToList();
                if (candidates.Count == 0)
                {
                    TaskDialog.Show("KStruct - Ván khuôn", "Dự án không có cột, dầm, sàn, tường hoặc móng kết cấu phù hợp.");
                    return Result.Cancelled;
                }

                Options options = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true };
                var rows = new List<QuantityRow>();
                foreach (Element element in candidates)
                {
                    try
                    {
                        IList<Solid> solids = GetSolids(element, options);
                        if (solids.Count == 0)
                        {
                            rows.Add(new QuantityRow
                            {
                                ElementId = element.Id.Value,
                                Category = element.Category.Name,
                                TypeName = GetTypeName(doc, element),
                                Note = "Không có solid đọc được trong DetailLevel Fine."
                            });
                            continue;
                        }
                        double areaInternal = 0;
                        double volumeInternal = 0;
                        int faceCount = 0;
                        foreach (Solid solid in solids)
                        {
                            volumeInternal += solid.Volume;
                            foreach (Face face in solid.Faces)
                            {
                                XYZ normal = FaceNormal(face);
                                if (normal == null || !IsFormworkFace(element.Category.Id.Value, element, normal)) continue;
                                areaInternal += face.Area;
                                faceCount++;
                            }
                        }
                        rows.Add(new QuantityRow
                        {
                            ElementId = element.Id.Value,
                            Category = element.Category.Name,
                            TypeName = GetTypeName(doc, element),
                            FormworkAreaSquareMeters = UnitUtils.ConvertFromInternalUnits(areaInternal, UnitTypeId.SquareMeters),
                            VolumeCubicMeters = UnitUtils.ConvertFromInternalUnits(volumeInternal, UnitTypeId.CubicMeters),
                            IncludedFaceCount = faceCount,
                            Note = areaInternal <= 1e-9
                                ? "Không có mặt được tính theo quy tắc category KStruct."
                                : "Diện tích hình học theo quy tắc mặt KStruct; cần rà soát lỗ rỗng/liên kết."
                        });
                    }
                    catch (Exception ex)
                    {
                        rows.Add(new QuantityRow
                        {
                            ElementId = element.Id.Value,
                            Category = element.Category.Name,
                            TypeName = GetTypeName(doc, element),
                            Note = "Không đọc được hình học: " + ex.Message
                        });
                    }
                }
                if (rows.Count == 0)
                {
                    TaskDialog.Show("KStruct - Ván khuôn", "Không đọc được solid hợp lệ từ các cấu kiện kết cấu trong dự án.");
                    return Result.Cancelled;
                }

                string path = ChooseCsvPath();
                if (path == null) return Result.Cancelled;
                WriteCsv(path, rows);
                int calculated = rows.Count(row => row.FormworkAreaSquareMeters > 0);
                double totalArea = rows.Sum(row => row.FormworkAreaSquareMeters);
                if (TaskDialog.Show("KStruct - Cập nhật ván khuôn",
                    "Ghi diện tích hình học vào tham số KStruct_FormworkArea cho " + calculated +
                    " cấu kiện có mặt được tính? Các phần tử không đọc được solid sẽ được giữ nguyên.",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
                    return Result.Cancelled;

                ExternalDefinition formworkDefinition = EnsureFormworkParameter(doc);
                int updated = WriteFormworkParameters(doc, formworkDefinition, rows);
                TaskDialog.Show("KStruct - Ván khuôn",
                    "Đã tính diện tích cho " + calculated.ToString(CultureInfo.InvariantCulture) + " cấu kiện." +
                    "\nĐã cập nhật tham số KStruct_FormworkArea trên " + updated.ToString(CultureInfo.InvariantCulture) + " cấu kiện." +
                    "\nTổng diện tích hình học: " + totalArea.ToString("N3", CultureInfo.CurrentCulture) + " m²" +
                    "\n\nĐã xuất báo cáo: " + path +
                    "\nKiểm tra các mặt bị loại/trùng và điều kiện tiếp giáp trước khi dùng làm khối lượng thi công.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Ván khuôn", ex.Message);
                return Result.Failed;
            }
        }

        private static IList<Solid> GetSolids(Element element, Options options)
        {
            var solids = new List<Solid>();
            GeometryElement geometry = element.get_Geometry(options);
            CollectSolids(geometry, solids, 0);
            return solids;
        }

        private static void CollectSolids(GeometryElement geometry, IList<Solid> solids, int depth)
        {
            if (geometry == null || depth > 8) return;
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid && solid.Faces.Size > 0 && solid.Volume > 1e-9)
                    solids.Add(solid);
                else if (item is GeometryInstance instance)
                {
                    CollectSolids(instance.GetInstanceGeometry(), solids, depth + 1);
                }
            }
        }

        private static XYZ FaceNormal(Face face)
        {
            if (face is PlanarFace planar) return planar.FaceNormal.Normalize();
            BoundingBoxUV bounds = face.GetBoundingBox();
            if (bounds == null) return null;
            UV sample = new UV((bounds.Min.U + bounds.Max.U) / 2.0, (bounds.Min.V + bounds.Max.V) / 2.0);
            try { return face.ComputeNormal(sample).Normalize(); }
            catch { return null; }
        }

        private static bool IsFormworkFace(long categoryId, Element element, XYZ normal)
        {
            double vertical = Math.Abs(normal.DotProduct(XYZ.BasisZ));
            if (categoryId == (long)BuiltInCategory.OST_StructuralColumns)
            {
                XYZ axis = GetMemberAxis(element);
                return axis == null || Math.Abs(normal.DotProduct(axis)) < 0.95;
            }
            if (categoryId == (long)BuiltInCategory.OST_StructuralFraming || categoryId == (long)BuiltInCategory.OST_Floors)
                return normal.DotProduct(XYZ.BasisZ) < 0.95;
            if (categoryId == (long)BuiltInCategory.OST_Walls || categoryId == (long)BuiltInCategory.OST_StructuralFoundation)
                return vertical < 0.95;
            return true;
        }

        private static XYZ GetMemberAxis(Element element)
        {
            LocationCurve curveLocation = element.Location as LocationCurve;
            if (curveLocation != null && curveLocation.Curve != null)
                return (curveLocation.Curve.GetEndPoint(1) - curveLocation.Curve.GetEndPoint(0)).Normalize();
            return XYZ.BasisZ;
        }

        private static string GetTypeName(Document doc, Element element)
        {
            Element type = doc.GetElement(element.GetTypeId());
            return type == null ? element.Name ?? "" : type.Name;
        }

        private static ExternalDefinition EnsureFormworkParameter(Document doc)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KStruct");
            Directory.CreateDirectory(folder);
            string parameterFile = Path.Combine(folder, "KStructSharedParameters.txt");
            if (!File.Exists(parameterFile)) File.WriteAllText(parameterFile, "# KStruct shared parameters\r\n", Encoding.UTF8);

            Autodesk.Revit.ApplicationServices.Application app = doc.Application;
            string originalPath = app.SharedParametersFilename;
            DefinitionFile definitionFile;
            try
            {
                app.SharedParametersFilename = parameterFile;
                definitionFile = app.OpenSharedParameterFile();
            }
            finally
            {
                app.SharedParametersFilename = originalPath;
            }
            if (definitionFile == null) throw new InvalidOperationException("Không mở được file tham số dùng chung KStruct.");
            DefinitionGroup group = definitionFile.Groups.get_Item("KStruct") ?? definitionFile.Groups.Create("KStruct");
            ExternalDefinition definition = group.Definitions.get_Item("KStruct_FormworkArea") as ExternalDefinition;
            if (definition == null)
            {
                var options = new ExternalDefinitionCreationOptions("KStruct_FormworkArea", SpecTypeId.Area)
                {
                    Description = "Diện tích ván khuôn hình học do KStruct tính, lưu theo đơn vị diện tích của Revit."
                };
                definition = group.Definitions.Create(options) as ExternalDefinition;
            }
            if (definition == null) throw new InvalidOperationException("Không tạo được định nghĩa tham số KStruct_FormworkArea.");

            CategorySet categorySet = app.Create.NewCategorySet();
            foreach (BuiltInCategory builtInCategory in new[]
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_StructuralFoundation
            })
            {
                Category category = Category.GetCategory(doc, builtInCategory);
                if (category != null && category.AllowsBoundParameters) categorySet.Insert(category);
            }
            if (categorySet.IsEmpty) throw new InvalidOperationException("Không có Category nào cho phép bind tham số ván khuôn.");

            using (Transaction tx = new Transaction(doc, "KStruct - Bind tham số diện tích ván khuôn"))
            {
                tx.Start();
                InstanceBinding binding = app.Create.NewInstanceBinding(categorySet);
                bool bound = doc.ParameterBindings.ReInsert(definition, binding, GroupTypeId.Data);
                if (!bound) bound = doc.ParameterBindings.Insert(definition, binding, GroupTypeId.Data);
                if (!bound) throw new InvalidOperationException("Không bind được tham số KStruct_FormworkArea vào các Category kết cấu.");
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit không chấp nhận binding tham số ván khuôn.");
            }
            return definition;
        }

        private static int WriteFormworkParameters(Document doc, ExternalDefinition definition, IList<QuantityRow> rows)
        {
            int updated = 0;
            using (Transaction tx = new Transaction(doc, "KStruct - Cập nhật diện tích ván khuôn"))
            {
                tx.Start();
                foreach (QuantityRow row in rows.Where(item => item.IncludedFaceCount > 0))
                {
                    Element element = doc.GetElement(new ElementId(row.ElementId));
                    Parameter parameter = element == null ? null : element.get_Parameter(definition.GUID);
                    if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double) continue;
                    double internalArea = UnitUtils.ConvertToInternalUnits(row.FormworkAreaSquareMeters, UnitTypeId.SquareMeters);
                    if (parameter.Set(internalArea)) updated++;
                }
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit không chấp nhận cập nhật diện tích ván khuôn.");
            }
            return updated;
        }

        private static string ChooseCsvPath()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Lưu báo cáo diện tích ván khuôn KStruct",
                Filter = "CSV UTF-8 (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = "KStruct_FormworkArea_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".csv"
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private static void WriteCsv(string path, IList<QuantityRow> rows)
        {
            var lines = new List<string>
            {
                "Element Id,Category,Type,Formwork Area (m2),Volume (m3),Included Face Count,Note"
            };
            lines.AddRange(rows.Select(row => string.Join(",", new[]
            {
                row.ElementId.ToString(CultureInfo.InvariantCulture), Csv(row.Category), Csv(row.TypeName),
                row.FormworkAreaSquareMeters.ToString("0.###", CultureInfo.InvariantCulture),
                row.VolumeCubicMeters.ToString("0.###", CultureInfo.InvariantCulture),
                row.IncludedFaceCount.ToString(CultureInfo.InvariantCulture), Csv(row.Note)
            })));
            File.WriteAllLines(path, lines, new UTF8Encoding(true));
        }

        private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
