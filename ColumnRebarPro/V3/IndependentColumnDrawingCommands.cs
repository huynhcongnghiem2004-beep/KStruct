using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace ColumnRebarPro.V3
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateColumnDrawingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            IndependentColumnDrawingService.ExecuteV3(data, ref message, false);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class RefreshColumnDrawingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            IndependentColumnDrawingService.ExecuteV3(data, ref message, true);
    }

    internal static class IndependentColumnDrawingService
    {
        private static readonly Guid SchemaId = new Guid("B411042C-5B2A-4C1B-AF4C-762628B7CC1E");
        private sealed class ColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance instance &&
                instance.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        // Use the same full KStruct V3 documentation pipeline as "Thép cột". This keeps the
        // ribbon's standalone create/update drawing buttons from falling back to the old
        // two-view placeholder implementation below.
        internal static Result ExecuteV3(ExternalCommandData data, ref string message, bool update)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                DeploymentSettingsFile settings = V3DeploymentControl.LoadSavedSettings(doc);
                if (!V3DeploymentControl.IsSavedDeploymentEnabled(settings))
                {
                    TaskDialog.Show("KStruct - Triển khai cột", DeploymentSetupMessage(settings));
                    return Result.Cancelled;
                }

                List<FamilyInstance> picked = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>().Where(c => new ColumnFilter().AllowElement(c)).Distinct().ToList();
                if (picked.Count == 0)
                    picked = ui.Selection.PickObjects(ObjectType.Element, new ColumnFilter(),
                            "Chọn cột KStruct cần " + (update ? "cập nhật" : "triển khai") + " bản vẽ")
                        .Select(r => doc.GetElement(r)).OfType<FamilyInstance>().Distinct().ToList();
                if (picked.Count == 0) return Result.Cancelled;

                var seen = new HashSet<ElementId>();
                var jobs = new List<List<V3SegmentWork>>();
                foreach (FamilyInstance selected in picked)
                {
                    if (seen.Contains(selected.Id)) continue;
                    var segments = ColumnStackFinder.Find(doc, selected);
                    var works = new List<V3SegmentWork>();
                    foreach (ColumnSegment segment in segments)
                    {
                        var column = doc.GetElement(segment.Id) as FamilyInstance;
                        if (column == null) continue;
                        seen.Add(column.Id);
                        V3RebarConfig config = V3RebarConfigStorage.Read(column);
                        if (config == null)
                            throw new InvalidOperationException("Cột " + column.Id.Value + " chưa có cấu hình KStruct V3 đã lưu. Hãy tạo thép bằng KStruct trước.");
                        bool hasKStructBars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                            .Any(r => string.Equals(r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString(),
                                    V3RebarBuilder.Partition(column.Id), StringComparison.Ordinal));
                        if (!hasKStructBars)
                            throw new InvalidOperationException("Cột " + column.Id.Value + " chưa có thép KStruct V3 để triển khai.");
                        works.Add(new V3SegmentWork { Column = column, Segment = segment,
                            Geometry = V3GeometryReader.Read(column), Config = config });
                    }
                    if (works.Count > 0) jobs.Add(works.OrderBy(w => w.Geometry.Bottom).ToList());
                }

                if (jobs.Count == 0) return Result.Cancelled;
                var builders = jobs.Select(_ => new V3DrawingBuilder(settings, update)).ToList();
                for (int i = 0; i < jobs.Count; i++)
                {
                    V3StarterConnection.Resolve(jobs[i]);
                    V3RebarValidator.Preflight(jobs[i]);
                    builders[i].UseSheetIdentity(jobs[i], i, jobs.Count);
                    builders[i].Validate(doc, jobs[i]);
                }

                int completed = 0;
                var failures = new List<string>();
                using (var group = new TransactionGroup(doc, update ? "KStruct - Cập nhật triển khai cột" : "KStruct - Triển khai bản vẽ cột"))
                {
                    group.Start();
                    for (int i = 0; i < jobs.Count; i++)
                    {
                        using (var tx = new Transaction(doc, "KStruct - Bản vẽ tuyến cột " + (i + 1)))
                        {
                            tx.Start();
                            try
                            {
                                builders[i].Build(doc, jobs[i]);
                                if (tx.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận bản vẽ.");
                                completed++;
                            }
                            catch (Exception ex)
                            {
                                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                                failures.Add("Tuyến " + (i + 1) + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed > 0) group.Assimilate(); else group.RollBack();
                }

                if (completed == 0)
                    throw new InvalidOperationException("Không triển khai được tuyến cột nào.\n" + string.Join("\n", failures.Take(8)));
                string report = (update ? "Đã cập nhật " : "Đã tạo ") + completed + "/" + jobs.Count +
                    " bộ bản vẽ V3 dạng DRAFT (mặt cắt dọc, mặt cắt ngang, kích thước, tag và bảng thép theo thiết lập). " +
                    "DRAFT không thay thế phê duyệt thiết kế hoặc kiểm tra phát hành.";
                if (failures.Count > 0) report += "\nBỏ qua:\n" + string.Join("\n", failures.Take(8));
                TaskDialog.Show("KStruct - Triển khai cột", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Triển khai cột", ex.Message);
                return Result.Failed;
            }
        }

        private static string DeploymentSetupMessage(DeploymentSettingsFile settings)
        {
            string status = settings == null
                ? "Project này chưa có thiết lập Triển khai đã lưu."
                : "Thiết lập Triển khai của project đang tắt.";
            return status + " Mở KStruct > Tạo thép cột > Triển khai, bật triển khai, chọn Title Block và loại View mặt cắt, rồi nhấn Lưu thiết lập triển khai. Sau đó chạy lại lệnh này. Không cần tạo lại thép cột.";
        }

        private sealed class DrawingTypes { public FamilySymbol TitleBlock; public ViewFamilyType SectionType; }
        private sealed class Bounds
        {
            public double MinX = double.MaxValue, MinY = double.MaxValue, MinZ = double.MaxValue;
            public double MaxX = double.MinValue, MaxY = double.MinValue, MaxZ = double.MinValue;
        }

        internal static Result Execute(ExternalCommandData data, ref string message, bool update)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> columns = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>().Where(c => new ColumnFilter().AllowElement(c)).Distinct().ToList();
                if (columns.Count == 0)
                    columns = ui.Selection.PickObjects(ObjectType.Element, new ColumnFilter(),
                            "Chọn cột cần " + (update ? "cập nhật" : "tạo") + " bản vẽ")
                        .Select(r => doc.GetElement(r)).OfType<FamilyInstance>().Distinct().ToList();
                if (columns.Count == 0) return Result.Cancelled;
                DrawingTypes types = ChooseTypes(data.Application.MainWindowHandle, doc);
                if (types == null) return Result.Cancelled;
                int completed = 0;
                var errors = new List<string>();
                using (Transaction tx = new Transaction(doc, update ? "KStruct - Cập nhật bản vẽ cột" : "KStruct - Tạo bản vẽ cột"))
                {
                    tx.Start();
                    foreach (FamilyInstance column in columns)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ViewSheet> old = FindSheets(doc, column.UniqueId);
                                if (update && old.Count == 0) throw new InvalidOperationException("Chưa có bản vẽ KStruct để cập nhật.");
                                if (!update && old.Count > 0) throw new InvalidOperationException("Đã có bản vẽ KStruct; dùng lệnh Cập nhật.");
                                List<Rebar> bars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                                    .Where(r => r.GetHostId() == column.Id).ToList();
                                if (bars.Count == 0) throw new InvalidOperationException("Cột chưa có Rebar host.");
                                DeleteOld(doc, old);
                                Build(doc, column, bars, types);
                                if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận bản vẽ.");
                                completed++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                errors.Add("Cột " + column.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Bản vẽ cột", "Không tạo/cập nhật được bản vẽ nào.\n" + string.Join("\n", errors.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận bản vẽ cột.");
                }
                string report = (update ? "Đã cập nhật " : "Đã tạo ") + completed + "/" + columns.Count + " bản vẽ cột.";
                if (errors.Count > 0) report += "\nBỏ qua:\n" + string.Join("\n", errors.Take(8));
                TaskDialog.Show("KStruct - Bản vẽ cột", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.ToString(); TaskDialog.Show("KStruct - Bản vẽ cột", ex.Message); return Result.Failed; }
        }

        private static void Build(Document doc, FamilyInstance column, IList<Rebar> bars, DrawingTypes types)
        {
            LocationPoint loc = column.Location as LocationPoint;
            if (loc == null) throw new InvalidOperationException("Chỉ hỗ trợ cột đứng dạng điểm.");
            Transform basis = column.GetTransform();
            if (Math.Abs(basis.BasisZ.Z) < .999) throw new InvalidOperationException("Cột nghiêng chưa được hỗ trợ.");
            XYZ x = new XYZ(basis.BasisX.X, basis.BasisX.Y, 0).Normalize();
            XYZ y = XYZ.BasisZ.CrossProduct(x).Normalize();
            Bounds b = ReadBounds(column, bars, loc.Point, x, y);
            double margin = UnitUtils.ConvertToInternalUnits(200, UnitTypeId.Millimeters);
            double depth = UnitUtils.ConvertToInternalUnits(40, UnitTypeId.Millimeters);
            Transform crossT = Transform.Identity;
            crossT.Origin = new XYZ(loc.Point.X, loc.Point.Y, (b.MinZ + b.MaxZ) / 2);
            crossT.BasisX = x; crossT.BasisY = y; crossT.BasisZ = XYZ.BasisZ;
            var crossBox = new BoundingBoxXYZ { Transform = crossT,
                Min = new XYZ(b.MinX - margin, b.MinY - margin, -depth / 2),
                Max = new XYZ(b.MaxX + margin, b.MaxY + margin, depth / 2) };
            ViewSection cross = ViewSection.CreateSection(doc, types.SectionType.Id, crossBox);
            XYZ normal = x.CrossProduct(XYZ.BasisZ).Normalize();
            Transform longT = Transform.Identity;
            longT.Origin = new XYZ(loc.Point.X, loc.Point.Y, b.MinZ);
            longT.BasisX = x; longT.BasisY = XYZ.BasisZ; longT.BasisZ = normal;
            var longBox = new BoundingBoxXYZ { Transform = longT,
                Min = new XYZ(b.MinX - margin, -margin, -b.MaxY - margin),
                Max = new XYZ(b.MaxX + margin, b.MaxZ - b.MinZ + margin, -b.MinY + margin) };
            ViewSection longitudinal = ViewSection.CreateSection(doc, types.SectionType.Id, longBox);
            string mark = column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            string label = string.IsNullOrWhiteSpace(mark) ? "C" + column.Id.Value : mark.Trim();
            cross.Name = UniqueViewName(doc, "KSTR COL MCN " + label);
            longitudinal.Name = UniqueViewName(doc, "KSTR COL MCD " + label);
            foreach (ViewSection view in new[] { cross, longitudinal })
            {
                view.DetailLevel = ViewDetailLevel.Fine; view.Scale = 20;
                view.CropBoxActive = true; view.CropBoxVisible = false;
            }
            doc.Regenerate();
            ViewSheet sheet = ViewSheet.Create(doc, types.TitleBlock.Id);
            sheet.SheetNumber = UniqueSheetNumber(doc, "KC-" + Sanitize(mark, column.Id.Value.ToString(CultureInfo.InvariantCulture)));
            sheet.Name = "Bản vẽ cột " + label;
            doc.Regenerate();
            BoundingBoxUV outline = sheet.Outline;
            double width = outline.Max.U - outline.Min.U, yCenter = (outline.Max.V + outline.Min.V) / 2;
            Viewport.Create(doc, sheet.Id, cross.Id, new XYZ(outline.Min.U + width * .28, yCenter, 0));
            Viewport.Create(doc, sheet.Id, longitudinal.Id, new XYZ(outline.Min.U + width * .72, yCenter, 0));
            Entity entity = new Entity(GetSchema());
            entity.Set(GetSchema().GetField("ColumnUniqueId"), column.UniqueId);
            sheet.SetEntity(entity);
        }

        private static Bounds ReadBounds(FamilyInstance column, IList<Rebar> bars, XYZ origin, XYZ x, XYZ y)
        {
            var result = new Bounds();
            foreach (Element element in new[] { (Element)column }.Concat(bars))
            {
                BoundingBoxXYZ box = element.get_BoundingBox(null);
                if (box == null) continue;
                for (int a = 0; a < 2; a++) for (int c = 0; c < 2; c++) for (int d = 0; d < 2; d++)
                {
                    XYZ p = box.Transform.OfPoint(new XYZ(a == 0 ? box.Min.X : box.Max.X,
                        c == 0 ? box.Min.Y : box.Max.Y, d == 0 ? box.Min.Z : box.Max.Z));
                    XYZ rel = p - origin;
                    double px = rel.DotProduct(x), py = rel.DotProduct(y);
                    result.MinX = Math.Min(result.MinX, px); result.MaxX = Math.Max(result.MaxX, px);
                    result.MinY = Math.Min(result.MinY, py); result.MaxY = Math.Max(result.MaxY, py);
                    result.MinZ = Math.Min(result.MinZ, p.Z); result.MaxZ = Math.Max(result.MaxZ, p.Z);
                }
            }
            if (result.MinX == double.MaxValue || result.MaxZ - result.MinZ <= 1e-6)
                throw new InvalidOperationException("Không đọc được phạm vi cột và Rebar.");
            return result;
        }

        private static List<ViewSheet> FindSheets(Document doc, string uniqueId) =>
            new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(sheet =>
            {
                try
                {
                    Entity e = sheet.GetEntity(GetSchema());
                    return e.IsValid() && e.Get<string>(GetSchema().GetField("ColumnUniqueId")) == uniqueId;
                }
                catch { return false; }
            }).ToList();

        private static void DeleteOld(Document doc, IList<ViewSheet> sheets)
        {
            foreach (ViewSheet sheet in sheets)
            {
                List<ElementId> views = sheet.GetAllPlacedViews().ToList();
                doc.Delete(sheet.Id);
                foreach (ElementId id in views) if (doc.GetElement(id) != null) doc.Delete(id);
            }
        }

        private static Schema GetSchema()
        {
            Schema schema = Schema.Lookup(SchemaId);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaId);
            builder.SetSchemaName("KStructIndependentColumnDrawing");
            builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("ColumnUniqueId", typeof(string));
            return builder.Finish();
        }

        private static DrawingTypes ChooseTypes(IntPtr owner, Document doc)
        {
            List<FamilySymbol> titleBlocks = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().OrderBy(t => t.FamilyName + " : " + t.Name).ToList();
            List<ViewFamilyType> sections = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().Where(t => t.ViewFamily == ViewFamily.Section).OrderBy(t => t.Name).ToList();
            if (titleBlocks.Count == 0) throw new InvalidOperationException("Project chưa nạp Title Block.");
            if (sections.Count == 0) throw new InvalidOperationException("Project chưa có Section View Family Type.");
            var window = new Window { Title = "Thiết lập bản vẽ cột", Width = 440, Height = 190,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Khung tên:" });
            var title = new System.Windows.Controls.ComboBox { ItemsSource = titleBlocks, DisplayMemberPath = "Name", SelectedIndex = 0, Margin = new Thickness(0, 3, 0, 7) };
            root.Children.Add(title); root.Children.Add(new TextBlock { Text = "Loại mặt cắt:" });
            var section = new System.Windows.Controls.ComboBox { ItemsSource = sections, DisplayMemberPath = "Name", SelectedIndex = 0, Margin = new Thickness(0, 3, 0, 9) };
            root.Children.Add(section);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Tiếp tục", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) => window.DialogResult = true;
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
            window.Content = root; new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new DrawingTypes { TitleBlock = title.SelectedItem as FamilySymbol, SectionType = section.SelectedItem as ViewFamilyType };
        }

        private static string UniqueViewName(Document doc, string baseName)
        {
            var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(baseName)) return baseName;
            for (int i = 2; ; i++) if (!names.Contains(baseName + " (" + i + ")")) return baseName + " (" + i + ")";
        }
        private static string UniqueSheetNumber(Document doc, string baseNumber)
        {
            var numbers = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber), StringComparer.OrdinalIgnoreCase);
            if (!numbers.Contains(baseNumber)) return baseNumber;
            for (int i = 2; ; i++) if (!numbers.Contains(baseNumber + "-" + i)) return baseNumber + "-" + i;
        }
        private static string Sanitize(string value, string fallback)
        {
            string source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            string clean = new string(source.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-').ToArray()).Trim('-');
            return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
        }
    }
}
