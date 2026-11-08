using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateLeanConcreteCommand : IExternalCommand
    {
        private static readonly Guid SchemaId = new Guid("A1D688B8-04F7-4F60-A29D-4316BAF4FE12");

        private sealed class FoundationFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance && element.Category != null &&
                element.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class FloorTypeChoice
        {
            public FloorType Type { get; }
            public string Label { get; }
            public FloorTypeChoice(FloorType type)
            {
                Type = type;
                double thickness = type.GetCompoundStructure()?.GetWidth() ?? 0;
                Label = type.Name + (thickness > 0 ? " | dày " + UnitUtils.ConvertFromInternalUnits(thickness, UnitTypeId.Millimeters).ToString("0.#") + " mm" : "");
            }
            public override string ToString() => Label;
        }

        private sealed class BottomProfile
        {
            public double Elevation;
            public IList<CurveLoop> Loops;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> foundations = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(element => element.Category != null &&
                        element.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation).Distinct().ToList();
                if (foundations.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FoundationFilter(),
                        "Chọn móng có mặt đáy ngang để tạo bê tông lót");
                    foundations = picked.Select(reference => doc.GetElement(reference)).OfType<FamilyInstance>().Distinct().ToList();
                }
                if (foundations.Count == 0) return Result.Cancelled;

                List<FloorType> floorTypes = new FilteredElementCollector(doc).OfClass(typeof(FloorType))
                    .Cast<FloorType>().Where(type => !type.IsFoundationSlab)
                    .OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (floorTypes.Count == 0) throw new InvalidOperationException("Project chưa có Floor Type để tạo bê tông lót.");
                FloorType selectedType = ChooseFloorType(data.Application.MainWindowHandle, floorTypes);
                if (selectedType == null) return Result.Cancelled;

                Schema schema = GetSchema();
                int created = 0, updated = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Tạo bê tông lót móng"))
                {
                    transaction.Start();
                    foreach (FamilyInstance foundation in foundations)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                BottomProfile profile = FindBottomProfile(foundation);
                                Level level = ResolveLevel(doc, foundation, profile.Elevation);
                                if (level == null) throw new InvalidOperationException("Không tìm được Level liên kết với móng.");
                                List<ElementId> oldFloors = FindTaggedFloors(doc, schema, foundation.UniqueId);
                                Floor lining = Floor.Create(doc, profile.Loops, selectedType.Id, level.Id, false, null, 0);
                                if (lining == null) throw new InvalidOperationException("Revit không tạo được Floor bê tông lót.");
                                Parameter offset = lining.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                if (offset != null && !offset.IsReadOnly) offset.Set(profile.Elevation - level.Elevation);
                                SetSourceTag(lining, schema, foundation.UniqueId);
                                if (oldFloors.Count > 0) doc.Delete(oldFloors);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận bê tông lót.");
                                if (oldFloors.Count > 0) updated++; else created++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Móng " + foundation.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (created + updated == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Bê tông lót", "Không tạo được lớp bê tông lót nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các lớp bê tông lót.");
                }

                string report = "Đã tạo mới " + created + " lớp bê tông lót; cập nhật " + updated +
                    " lớp KStruct đã có. Floor Type: " + selectedType.Name +
                    ". Biên sàn theo mặt đáy ngang của móng; chiều dày lấy từ Floor Type.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " móng:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Bê tông lót", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Bê tông lót", ex.Message);
                return Result.Failed;
            }
        }

        private static BottomProfile FindBottomProfile(FamilyInstance foundation)
        {
            var solids = new List<Solid>();
            Options options = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false, ComputeReferences = false };
            CollectSolids(foundation.get_Geometry(options), solids);
            var faces = solids.Where(solid => solid != null && solid.Faces.Size > 0)
                .SelectMany(solid => solid.Faces.Cast<Face>()).OfType<PlanarFace>()
                .Where(face => face.FaceNormal.Z < -0.999 && face.Area > 1e-6).ToList();
            if (faces.Count == 0) throw new InvalidOperationException("Không tìm thấy mặt đáy ngang. Chỉ hỗ trợ móng có đáy phẳng nằm ngang.");
            double lowest = faces.Min(face => face.Origin.Z);
            double tolerance = UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters);
            PlanarFace bottom = faces.Where(face => Math.Abs(face.Origin.Z - lowest) <= tolerance)
                .OrderByDescending(face => face.Area).First();
            IList<CurveLoop> loops = bottom.GetEdgesAsCurveLoops();
            if (loops == null || loops.Count == 0 || !BoundaryValidation.IsValidHorizontalBoundary(loops))
                throw new InvalidOperationException("Biên mặt đáy không tạo được profile Floor ngang hợp lệ.");
            return new BottomProfile { Elevation = bottom.Origin.Z, Loops = loops };
        }

        private static void CollectSolids(GeometryElement geometry, IList<Solid> solids)
        {
            if (geometry == null) return;
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid && solid.Faces.Size > 0) solids.Add(solid);
                else if (item is GeometryInstance instance) CollectSolids(instance.GetInstanceGeometry(), solids);
            }
        }

        private static Level ResolveLevel(Document doc, FamilyInstance foundation, double elevation)
        {
            Level level = foundation.LevelId != ElementId.InvalidElementId ? doc.GetElement(foundation.LevelId) as Level : null;
            if (level != null) return level;
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(candidate => Math.Abs(candidate.Elevation - elevation)).FirstOrDefault();
        }

        private static Schema GetSchema()
        {
            Schema schema = Schema.Lookup(SchemaId);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaId);
            builder.SetSchemaName("KStructGeneratedLeanConcrete");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("SourceFoundationUniqueId", typeof(string));
            return builder.Finish();
        }

        private static void SetSourceTag(Element element, Schema schema, string sourceUniqueId)
        {
            var entity = new Entity(schema);
            entity.Set(schema.GetField("SourceFoundationUniqueId"), sourceUniqueId ?? string.Empty);
            element.SetEntity(entity);
        }

        private static List<ElementId> FindTaggedFloors(Document doc, Schema schema, string sourceUniqueId) =>
            new FilteredElementCollector(doc).OfClass(typeof(Floor)).WhereElementIsNotElementType()
                .Where(floor => HasSourceTag(floor, schema, sourceUniqueId)).Select(floor => floor.Id).ToList();

        private static bool HasSourceTag(Element floor, Schema schema, string sourceUniqueId)
        {
            Entity entity = floor.GetEntity(schema);
            return entity.IsValid() && string.Equals(entity.Get<string>(schema.GetField("SourceFoundationUniqueId")),
                sourceUniqueId, StringComparison.Ordinal);
        }

        private static FloorType ChooseFloorType(IntPtr owner, IList<FloorType> types)
        {
            var window = new Window
            {
                Title = "Floor Type bê tông lót - KStruct",
                Width = 430,
                Height = 175,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new System.Windows.Controls.Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new TextBlock
            {
                Text = "Chọn Floor Type không kết cấu. Sàn bê tông lót sẽ theo đúng biên mặt đáy móng và cao độ đáy.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var combo = new WpfComboBox
            {
                ItemsSource = types.Select(type => new FloorTypeChoice(type)).ToList(),
                MinHeight = 27,
                SelectedIndex = 0
            };
            System.Windows.Controls.Grid.SetRow(combo, 1);
            root.Children.Add(combo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Tạo / cập nhật", MinWidth = 110, IsDefault = true, Margin = new Thickness(0, 0, 7, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            ok.Click += (sender, args) => window.DialogResult = true;
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            System.Windows.Controls.Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);
            window.Content = root;
            return window.ShowDialog() == true ? (combo.SelectedItem as FloorTypeChoice)?.Type : null;
        }
    }
}
