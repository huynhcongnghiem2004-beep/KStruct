using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WpfGrid = System.Windows.Controls.Grid;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateLintelsCommand : IExternalCommand
    {
        private static readonly Guid SchemaId = new Guid("6F9A7B21-64C2-4E03-9B75-70D9C27388B4");

        private sealed class OpeningFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => IsOpening(element);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class SymbolChoice
        {
            public FamilySymbol Symbol { get; }
            public string Display => Symbol.FamilyName + " : " + Symbol.Name;
            public SymbolChoice(FamilySymbol symbol) { Symbol = symbol; }
            public override string ToString() => Display;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> openings = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>().Where(IsOpening).Distinct().ToList();
                if (openings.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new OpeningFilter(),
                        "Chọn cửa/cửa sổ để tạo lintel phía trên");
                    openings = picked.Select(reference => doc.GetElement(reference)).OfType<FamilyInstance>().Distinct().ToList();
                }
                if (openings.Count == 0) return Result.Cancelled;

                List<FamilySymbol> symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_StructuralFraming).Cast<FamilySymbol>()
                    .OrderBy(symbol => symbol.FamilyName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(symbol => symbol.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (symbols.Count == 0) throw new InvalidOperationException("Project chưa có Family Type thuộc Structural Framing.");
                Tuple<FamilySymbol, double> choice = ChooseOptions(data.Application.MainWindowHandle, symbols);
                if (choice == null) return Result.Cancelled;

                Schema schema = GetSchema();
                int created = 0, updated = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Tạo lintel trên cửa/cửa sổ"))
                {
                    transaction.Start();
                    if (!choice.Item1.IsActive) choice.Item1.Activate();
                    doc.Regenerate();
                    foreach (FamilyInstance opening in openings)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                Wall hostWall = opening.Host as Wall;
                                if (hostWall == null) throw new InvalidOperationException("Cửa/cửa sổ không host bởi Wall.");
                                Line wallAxis = (hostWall.Location as LocationCurve)?.Curve as Line;
                                if (wallAxis == null || Math.Abs(wallAxis.Direction.Z) > 1e-5)
                                    throw new InvalidOperationException("Chỉ hỗ trợ tường thẳng nằm ngang trong mặt bằng.");
                                BoundingBoxXYZ box = opening.get_BoundingBox(null);
                                if (box == null) throw new InvalidOperationException("Không đọc được bounding box của cửa/cửa sổ.");

                                XYZ axis = new XYZ(wallAxis.Direction.X, wallAxis.Direction.Y, 0).Normalize();
                                XYZ perpendicular = XYZ.BasisZ.CrossProduct(axis).Normalize();
                                List<XYZ> corners = GetCorners(box);
                                double minAlong = corners.Min(point => point.DotProduct(axis));
                                double maxAlong = corners.Max(point => point.DotProduct(axis));
                                if (maxAlong - minAlong < doc.Application.ShortCurveTolerance * 2)
                                    throw new InvalidOperationException("Bề rộng cửa/cửa sổ quá nhỏ để tạo lintel.");
                                XYZ center = (box.Min + box.Max) * 0.5;
                                double perpendicularOffset = center.DotProduct(perpendicular);
                                double centerZ = box.Max.Z + UnitUtils.ConvertToInternalUnits(choice.Item2, UnitTypeId.Millimeters);
                                XYZ start = axis * minAlong + perpendicular * perpendicularOffset + XYZ.BasisZ * centerZ;
                                XYZ end = axis * maxAlong + perpendicular * perpendicularOffset + XYZ.BasisZ * centerZ;
                                Level level = ResolveLevel(doc, opening, hostWall, centerZ);
                                if (level == null) throw new InvalidOperationException("Không tìm được Level để đặt lintel.");

                                List<ElementId> previous = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
                                    .WhereElementIsNotElementType().Where(element => HasSourceTag(element, schema, opening.UniqueId))
                                    .Select(element => element.Id).ToList();
                                FamilyInstance beam = doc.Create.NewFamilyInstance(Line.CreateBound(start, end),
                                    choice.Item1, level, StructuralType.Beam);
                                if (beam == null) throw new InvalidOperationException("Revit không tạo được Structural Framing lintel.");
                                SetSourceTag(beam, schema, opening.UniqueId);
                                if (previous.Count > 0) doc.Delete(previous);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận lintel.");
                                if (previous.Count > 0) updated++; else created++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Cửa/cửa sổ " + opening.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (created + updated == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Tạo lintel", "Không tạo được lintel nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi lintel.");
                }

                string report = "Đã tạo mới " + created + " lintel; cập nhật " + updated + " lintel KStruct đã có.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " cửa/cửa sổ:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Tạo lintel", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tạo lintel", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsOpening(Element element)
        {
            long? category = element?.Category?.Id.Value;
            return element is FamilyInstance && (category == (long)BuiltInCategory.OST_Doors ||
                category == (long)BuiltInCategory.OST_Windows);
        }

        private static List<XYZ> GetCorners(BoundingBoxXYZ box)
        {
            Transform transform = box.Transform ?? Transform.Identity;
            return new[]
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            }.Select(transform.OfPoint).ToList();
        }

        private static Level ResolveLevel(Document doc, FamilyInstance opening, Wall wall, double z)
        {
            ElementId[] candidates = { opening.LevelId, wall.LevelId };
            foreach (ElementId id in candidates)
            {
                Level level = id != ElementId.InvalidElementId ? doc.GetElement(id) as Level : null;
                if (level != null) return level;
            }
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(level => Math.Abs(level.Elevation - z)).FirstOrDefault();
        }

        private static Schema GetSchema()
        {
            Schema schema = Schema.Lookup(SchemaId);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaId);
            builder.SetSchemaName("KStructGeneratedLintel");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("SourceOpeningUniqueId", typeof(string));
            return builder.Finish();
        }

        private static void SetSourceTag(Element element, Schema schema, string sourceUniqueId)
        {
            var entity = new Entity(schema);
            entity.Set(schema.GetField("SourceOpeningUniqueId"), sourceUniqueId ?? string.Empty);
            element.SetEntity(entity);
        }

        private static bool HasSourceTag(Element element, Schema schema, string sourceUniqueId)
        {
            Entity entity = element.GetEntity(schema);
            return entity.IsValid() && string.Equals(entity.Get<string>(schema.GetField("SourceOpeningUniqueId")),
                sourceUniqueId, StringComparison.Ordinal);
        }

        private static Tuple<FamilySymbol, double> ChooseOptions(IntPtr owner, IList<FamilySymbol> symbols)
        {
            var window = new Window
            {
                Title = "Tạo lintel - KStruct",
                Width = 460,
                Height = 225,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new WpfGrid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new TextBlock { Text = "Loại Structural Framing:", Margin = new Thickness(0, 0, 0, 5) });
            var combo = new WpfComboBox
            {
                ItemsSource = symbols.Select(symbol => new SymbolChoice(symbol)).ToList(),
                MinHeight = 28,
                Margin = new Thickness(0, 0, 0, 10)
            };
            combo.SelectedIndex = 0;
            WpfGrid.SetRow(combo, 1);
            root.Children.Add(combo);
            var offsetRow = new WpfGrid { Margin = new Thickness(0, 0, 0, 10) };
            offsetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            offsetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            offsetRow.Children.Add(new TextBlock
            {
                Text = "Độ nâng đường tâm so với đỉnh cửa/cửa sổ (mm):",
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            var offset = new WpfTextBox { Text = "0", MinHeight = 25, VerticalContentAlignment = VerticalAlignment.Center };
            WpfGrid.SetColumn(offset, 1);
            offsetRow.Children.Add(offset);
            WpfGrid.SetRow(offsetRow, 2);
            root.Children.Add(offsetRow);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Tạo / cập nhật", MinWidth = 110, IsDefault = true, Margin = new Thickness(0, 0, 7, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            Tuple<FamilySymbol, double> result = null;
            ok.Click += (sender, args) =>
            {
                SymbolChoice selected = combo.SelectedItem as SymbolChoice;
                double mm;
                if (selected == null || !double.TryParse(offset.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.CurrentCulture, out mm))
                {
                    MessageBox.Show(window, "Chọn loại dầm và nhập độ nâng hợp lệ.", "KStruct - Tạo lintel",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = Tuple.Create(selected.Symbol, mm);
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            WpfGrid.SetRow(buttons, 3);
            root.Children.Add(buttons);
            window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }
    }
}
