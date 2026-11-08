using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateBeamSectionsCommand : IExternalCommand
    {
        private sealed class ViewChoice
        {
            public ViewFamilyType Type;
            public string Label;
            public ViewChoice(ViewFamilyType type) { Type = type; Label = type.Name + " (" + type.ViewFamily + ")"; }
        }

        private sealed class TemplateChoice
        {
            public View Template;
            public string Label;
            public TemplateChoice(View template) { Template = template; Label = template == null ? "<Không dùng template>" : template.Name; }
        }

        private sealed class Options
        {
            public ViewFamilyType ViewType;
            public View Template;
            public bool CreateCross;
            public bool CreateLongitudinal;
            public double MarginMm;
            public double FarClipMm;
            public string NamePrefix;
        }

        private sealed class BeamFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance fi && IsStructuralMember(fi);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> members = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(IsStructuralMember).Distinct().ToList();
                if (members.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new BeamFilter(), "Chọn dầm/cột kết cấu cần tạo mặt cắt");
                    members = picked.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().Where(IsStructuralMember).Distinct().ToList();
                }
                if (members.Count == 0) return Result.Cancelled;

                List<ViewFamilyType> sectionTypes = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().Where(t => t.ViewFamily == ViewFamily.Section || t.ViewFamily == ViewFamily.Detail)
                    .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (sectionTypes.Count == 0) throw new InvalidOperationException("Project chưa có Section hoặc Detail View Family Type.");
                List<View> templates = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Where(v => v.IsTemplate && (v.ViewType == ViewType.Section || v.ViewType == ViewType.Detail))
                    .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                Options options = ShowOptions(data.Application.MainWindowHandle, sectionTypes, templates);
                if (options == null) return Result.Cancelled;
                HashSet<string> names = new FilteredElementCollector(doc).OfClass(typeof(View))
                    .Cast<View>().Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int created = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Tạo mặt cắt dầm"))
                {
                    transaction.Start();
                    foreach (FamilyInstance member in members)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                int beamCreated = 0;
                                List<string> beamNames = new List<string>();
                                bool isColumn = IsStructuralColumn(member);
                                if (options.CreateCross || isColumn)
                                {
                                    ViewSection view = isColumn
                                        ? CreateColumnSection(doc, member, options, names)
                                        : CreateBeamSection(doc, member, options, false, names);
                                    beamNames.Add(view.Name);
                                    beamCreated++;
                                }
                                if (options.CreateLongitudinal && !isColumn)
                                {
                                    ViewSection view = CreateBeamSection(doc, member, options, true, names);
                                    beamNames.Add(view.Name);
                                    beamCreated++;
                                }
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận mặt cắt.");
                                foreach (string viewName in beamNames) names.Add(viewName);
                                created += beamCreated;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add((IsStructuralColumn(member) ? "Cột " : "Dầm ") + member.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (created == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Mặt cắt dầm", "Không tạo được mặt cắt cho dầm đã chọn.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các view mới.");
                }

                string report = "Đã tạo " + created + " view mặt cắt cho " + members.Count + " cấu kiện.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " cấu kiện:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Mặt cắt dầm", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Mặt cắt dầm", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsStructuralMember(FamilyInstance instance) => IsStructuralFraming(instance) || IsStructuralColumn(instance);
        private static bool IsStructuralFraming(FamilyInstance instance) => instance.Category != null &&
            instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
        private static bool IsStructuralColumn(FamilyInstance instance) => instance.Category != null &&
            instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;

        private static ViewSection CreateColumnSection(Document doc, FamilyInstance column, Options options, HashSet<string> names)
        {
            BoundingBoxXYZ sourceBox = column.get_BoundingBox(null);
            if (sourceBox == null) throw new InvalidOperationException("Không đọc được bounding box cột.");
            XYZ x = column.HandOrientation;
            if (x == null || Math.Abs(x.Z) > 0.01 || x.GetLength() < 1e-6) x = XYZ.BasisX;
            x = new XYZ(x.X, x.Y, 0).Normalize();
            XYZ y = XYZ.BasisZ.CrossProduct(x).Normalize();
            XYZ z = x.CrossProduct(y).Normalize();
            XYZ origin = (sourceBox.Transform ?? Transform.Identity).OfPoint((sourceBox.Min + sourceBox.Max) * 0.5);
            Transform sectionTransform = Transform.Identity;
            sectionTransform.Origin = origin;
            sectionTransform.BasisX = x;
            sectionTransform.BasisY = y;
            sectionTransform.BasisZ = z;
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
            Transform sourceTransform = sourceBox.Transform ?? Transform.Identity;
            for (int ix = 0; ix < 2; ix++)
                for (int iy = 0; iy < 2; iy++)
                    for (int iz = 0; iz < 2; iz++)
                    {
                        XYZ local = new XYZ(ix == 0 ? sourceBox.Min.X : sourceBox.Max.X,
                            iy == 0 ? sourceBox.Min.Y : sourceBox.Max.Y,
                            iz == 0 ? sourceBox.Min.Z : sourceBox.Max.Z);
                        XYZ delta = sourceTransform.OfPoint(local) - origin;
                        double px = delta.DotProduct(x), py = delta.DotProduct(y);
                        minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                        minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                    }
            double margin = UnitUtils.ConvertToInternalUnits(options.MarginMm, UnitTypeId.Millimeters);
            var sectionBox = new BoundingBoxXYZ
            {
                Transform = sectionTransform,
                Min = new XYZ(minX - margin, minY - margin, -V3Depth(sourceBox, z, origin) / 2.0 - margin),
                Max = new XYZ(maxX + margin, maxY + margin, V3Depth(sourceBox, z, origin) / 2.0 + margin)
            };
            ViewSection view = CreateView(doc, options, sectionBox);
            string mark = column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            view.Name = UniqueName(options.NamePrefix + " Tiết diện Cột " +
                (string.IsNullOrWhiteSpace(mark) ? column.Id.Value.ToString() : mark.Trim()), names);
            FinishView(doc, view, options);
            return view;
        }

        private static double V3Depth(BoundingBoxXYZ box, XYZ axis, XYZ origin)
        {
            Transform transform = box.Transform ?? Transform.Identity;
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            for (int ix = 0; ix < 2; ix++)
                for (int iy = 0; iy < 2; iy++)
                    for (int iz = 0; iz < 2; iz++)
                    {
                        XYZ p = transform.OfPoint(new XYZ(ix == 0 ? box.Min.X : box.Max.X,
                            iy == 0 ? box.Min.Y : box.Max.Y, iz == 0 ? box.Min.Z : box.Max.Z));
                        double value = (p - origin).DotProduct(axis);
                        min = Math.Min(min, value); max = Math.Max(max, value);
                    }
            return max - min;
        }

        private static ViewSection CreateBeamSection(Document doc, FamilyInstance beam, Options options, bool longitudinal, HashSet<string> names)
        {
            LocationCurve location = beam.Location as LocationCurve;
            BoundingBoxXYZ sourceBox = beam.get_BoundingBox(null);
            if (location == null || location.Curve == null || sourceBox == null)
                throw new InvalidOperationException("Dầm phải có LocationCurve và bounding box.");

            Curve axis = location.Curve;
            XYZ origin = axis.Evaluate(0.5, true);
            XYZ tangent = axis.ComputeDerivatives(0.5, true).BasisX.Normalize();
            XYZ normal, basisX, basisY;
            if (longitudinal)
            {
                XYZ horizontal = new XYZ(tangent.X, tangent.Y, 0);
                if (horizontal.GetLength() < 1e-6) throw new InvalidOperationException("Không thể tạo mặt cắt dọc cho dầm gần thẳng đứng.");
                basisX = horizontal.Normalize();
                basisY = XYZ.BasisZ;
                normal = basisX.CrossProduct(basisY).Normalize();
            }
            else
            {
                normal = tangent;
                XYZ vertical = XYZ.BasisZ - normal * XYZ.BasisZ.DotProduct(normal);
                if (vertical.GetLength() < 1e-6) vertical = XYZ.BasisY - normal * XYZ.BasisY.DotProduct(normal);
                basisY = vertical.Normalize();
                basisX = basisY.CrossProduct(normal).Normalize();
                basisY = normal.CrossProduct(basisX).Normalize();
            }

            Transform sourceTransform = sourceBox.Transform ?? Transform.Identity;
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        XYZ local = new XYZ(x == 0 ? sourceBox.Min.X : sourceBox.Max.X,
                            y == 0 ? sourceBox.Min.Y : sourceBox.Max.Y,
                            z == 0 ? sourceBox.Min.Z : sourceBox.Max.Z);
                        XYZ point = sourceTransform.OfPoint(local) - origin;
                        double px = point.DotProduct(basisX), py = point.DotProduct(basisY);
                        minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                        minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                    }
            if (!IsFinite(minX) || !IsFinite(maxX) || maxX - minX < doc.Application.ShortCurveTolerance || maxY - minY < doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Không xác định được kích thước tiết diện dầm.");

            double margin = UnitUtils.ConvertToInternalUnits(options.MarginMm, UnitTypeId.Millimeters);
            Transform sectionTransform = Transform.Identity;
            sectionTransform.Origin = origin;
            sectionTransform.BasisX = basisX;
            sectionTransform.BasisY = basisY;
            sectionTransform.BasisZ = normal;
            double halfDepth = Math.Max(0.5, Math.Abs((maxX - minX) + (maxY - minY)) / 2.0);
            var sectionBox = new BoundingBoxXYZ
            {
                Transform = sectionTransform,
                Min = new XYZ(minX - margin, minY - margin, -halfDepth),
                Max = new XYZ(maxX + margin, maxY + margin, halfDepth)
            };
            ViewSection view = CreateView(doc, options, sectionBox);
            string mark = beam.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            string mode = longitudinal ? "Dọc" : "Ngang";
            string baseName = options.NamePrefix + " " + mode + " Dầm " + (string.IsNullOrWhiteSpace(mark) ? beam.Id.Value.ToString() : mark.Trim());
            view.Name = UniqueName(baseName, names);
            FinishView(doc, view, options);
            return view;
        }

        private static ViewSection CreateView(Document doc, Options options, BoundingBoxXYZ sectionBox)
        {
            return options.ViewType.ViewFamily == ViewFamily.Detail
                ? ViewSection.CreateDetail(doc, options.ViewType.Id, sectionBox)
                : ViewSection.CreateSection(doc, options.ViewType.Id, sectionBox);
        }

        private static void FinishView(Document doc, ViewSection view, Options options)
        {
            view.CropBoxActive = true;
            view.CropBoxVisible = false;
            if (options.FarClipMm > 0)
            {
                Parameter farClip = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                if (farClip != null && !farClip.IsReadOnly)
                    farClip.Set(UnitUtils.ConvertToInternalUnits(options.FarClipMm, UnitTypeId.Millimeters));
            }
            if (options.Template != null) view.ViewTemplateId = options.Template.Id;
        }

        private static Options ShowOptions(IntPtr owner, IList<ViewFamilyType> types, IList<View> templates)
        {
            Options result = null;
            var typeChoices = types.Select(t => new ViewChoice(t)).ToList();
            var templateChoices = new List<TemplateChoice> { new TemplateChoice(null) };
            templateChoices.AddRange(templates.Select(t => new TemplateChoice(t)));
            var window = new Window
            {
                Title = "Tạo mặt cắt dầm - KStruct", Width = 450, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = "Loại view:" });
            var typeCombo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = typeChoices, DisplayMemberPath = "Label", SelectedIndex = 0,
                MinWidth = 410, MinHeight = 25, Margin = new Thickness(0, 3, 0, 8)
            };
            root.Children.Add(typeCombo);
            root.Children.Add(new TextBlock { Text = "View template:" });
            var templateCombo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = templateChoices, DisplayMemberPath = "Label", SelectedIndex = 0,
                MinWidth = 410, MinHeight = 25, Margin = new Thickness(0, 3, 0, 8)
            };
            root.Children.Add(templateCombo);
            var crossCheck = new CheckBox { Content = "Tạo mặt cắt ngang tại giữa dầm / tiết diện cột", IsChecked = true, Margin = new Thickness(0, 2, 0, 4) };
            var longCheck = new CheckBox { Content = "Tạo thêm mặt cắt dọc theo dầm", IsChecked = false, Margin = new Thickness(0, 0, 0, 7) };
            root.Children.Add(crossCheck);
            root.Children.Add(longCheck);
            root.Children.Add(new TextBlock { Text = "Khoảng mở rộng khung cắt quanh dầm (mm):" });
            var marginBox = new System.Windows.Controls.TextBox { Text = "150", MinWidth = 410, MinHeight = 24, Margin = new Thickness(0, 3, 0, 8) };
            root.Children.Add(marginBox);
            root.Children.Add(new TextBlock { Text = "Far clip offset (mm, 0 = giữ mặc định):" });
            var clipBox = new System.Windows.Controls.TextBox { Text = "0", MinWidth = 410, MinHeight = 24, Margin = new Thickness(0, 3, 0, 8) };
            root.Children.Add(clipBox);
            root.Children.Add(new TextBlock { Text = "Tiền tố tên view:" });
            var prefixBox = new System.Windows.Controls.TextBox { Text = "KStruct - MC", MinWidth = 410, MinHeight = 24, Margin = new Thickness(0, 3, 0, 10) };
            root.Children.Add(prefixBox);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var create = new Button { Content = "Tạo view", MinWidth = 85, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 72, IsCancel = true };
            create.Click += (s, e) =>
            {
                double margin, clip;
                if (typeCombo.SelectedItem == null || (!crossCheck.IsChecked.GetValueOrDefault() && !longCheck.IsChecked.GetValueOrDefault()) ||
                    !double.TryParse(marginBox.Text, out margin) || margin < 0 || !double.TryParse(clipBox.Text, out clip) || clip < 0 ||
                    string.IsNullOrWhiteSpace(prefixBox.Text))
                {
                    TaskDialog.Show("KStruct - Mặt cắt dầm", "Chọn ít nhất một hướng; kích thước phải hợp lệ và tiền tố tên không được rỗng.");
                    return;
                }
                result = new Options
                {
                    ViewType = ((ViewChoice)typeCombo.SelectedItem).Type,
                    Template = ((TemplateChoice)templateCombo.SelectedItem).Template,
                    CreateCross = crossCheck.IsChecked.GetValueOrDefault(),
                    CreateLongitudinal = longCheck.IsChecked.GetValueOrDefault(),
                    MarginMm = margin,
                    FarClipMm = clip,
                    NamePrefix = prefixBox.Text.Trim()
                };
                window.DialogResult = true;
            };
            buttons.Children.Add(create);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? result : null;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static string UniqueName(string baseName, HashSet<string> names)
        {
            if (!names.Contains(baseName)) return baseName;
            for (int index = 2; ; index++)
            {
                string candidate = baseName + " (" + index + ")";
                if (!names.Contains(candidate)) return candidate;
            }
        }
    }
}
