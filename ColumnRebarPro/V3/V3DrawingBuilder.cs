using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    internal sealed partial class V3DrawingBuilder
    {
        private readonly Dictionary<string, string> values;
        private int sheetIndex;
        private readonly bool replaceExistingSheet;

        internal V3DrawingBuilder(DeploymentSettingsFile settings, bool replaceSheet = false)
        {
            values = new Dictionary<string, string>();
            foreach (var entry in settings.Entries)
                values[entry.Name] = entry.Value ?? "";
            replaceExistingSheet = replaceSheet;
        }

        private string S(string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : "";
        }

        private bool B(string key)
        {
            return S(key) == "true";
        }

        private ElementId Id(string key)
        {
            long id;
            return long.TryParse(S(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)
                ? new ElementId(id) : ElementId.InvalidElementId;
        }

        private int Scale(string prefix)
        {
            int n;
            if (!int.TryParse(S(prefix + "Scale"), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1 || n > 24000)
                throw new InvalidOperationException("Tỷ lệ mặt cắt không hợp lệ.");
            return n;
        }

        internal bool Enabled
        {
            get { return B("EnableCheck"); }
        }

        // Nhiều tuyến cột dùng chung một cấu hình triển khai nên số sheet phải khác nhau:
        // tuyến đầu giữ nguyên số nhập, các tuyến sau tăng dần phần số ở cuối (KC-05-01 -> KC-05-02).
        internal void UseSheetIdentity(IList<V3SegmentWork> works, int index, int total)
        {
            sheetIndex = Math.Max(0, index);
        }

        private string SheetNumberFor()
        {
            string number = S("SheetNumber").Trim();
            if (sheetIndex == 0) return number;

            int start = number.Length;
            while (start > 0 && char.IsDigit(number[start - 1])) start--;
            string digits = number.Substring(start);
            long value;
            if (digits.Length == 0 || !long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return number + "-" + (sheetIndex + 1);

            return number.Substring(0, start) +
                (value + sheetIndex).ToString(CultureInfo.InvariantCulture).PadLeft(digits.Length, '0');
        }

        internal void Validate(Document doc, IList<V3SegmentWork> works)
        {
            if (!Enabled) return;
            if (works != null)
            {
                foreach (V3SegmentWork work in works)
                {
                    if (work.Config == null)
                        throw new InvalidOperationException("Thiếu cấu hình thép cột để triển khai bản vẽ.");
                    if (work.Config.Splice != V3SpliceStyle.None || work.Config.StaggerSplices ||
                        work.TransitionUp != null || work.TransitionDown != null ||
                        (work.BottleneckToUpper && work.ConnectionPlan == null))
                        throw new InvalidOperationException("Không đủ dữ liệu nối/chuyển tiếp đã kiểm tra để triển khai bản vẽ.");
                }
            }

            double reserved;
            if (!double.TryParse(S("SheetRightMargin"), NumberStyles.Float, CultureInfo.InvariantCulture, out reserved) ||
                reserved < 0 || reserved > 500)
                throw new InvalidOperationException("Vùng chừa khung tên phải từ 0 đến 500 mm.");

            if (string.IsNullOrWhiteSpace(S("SheetName")) || string.IsNullOrWhiteSpace(S("SheetNumber")))
                throw new InvalidOperationException("Nhập tên và số sheet trong tab Triển khai.");

            if (!(doc.GetElement(Id("TitleBlock")) is FamilySymbol titleBlock) ||
                titleBlock.Category == null ||
                titleBlock.Category.Id.Value != (long)BuiltInCategory.OST_TitleBlocks)
                throw new InvalidOperationException("Chọn Title Block hợp lệ.");

            foreach (var prefix in new[] { "Cross", "Long" })
            {
                Scale(prefix);

                var type = doc.GetElement(Id(prefix + "SectionType")) as ViewFamilyType;
                if (type == null)
                    throw new InvalidOperationException("Chọn một View Family Type từ danh sách.");

                if (type.ViewFamily != ViewFamily.Detail && type.ViewFamily != ViewFamily.Section)
                    throw new InvalidOperationException(
                        "Loại đã chọn: " + type.Name + " [" + type.ViewFamily + "]. Revit yêu cầu loại Detail hoặc Section.");

                foreach (var suffix in new[] { "Template", "Dimension", "TextType", "StirrupTag", "MainTag" })
                {
                    if (Id(prefix + suffix) != ElementId.InvalidElementId &&
                        doc.GetElement(Id(prefix + suffix)) == null)
                        throw new InvalidOperationException("Loại đã bị xóa: " + prefix + suffix);
                }

                if (B(prefix + "ShowTag") &&
                    Id(prefix + "StirrupTag") == ElementId.InvalidElementId &&
                    Id(prefix + "MainTag") == ElementId.InvalidElementId)
                    throw new InvalidOperationException("Đang bật Hiện Tag: hãy chọn ít nhất một Rebar Tag hoặc tắt tùy chọn.");

                if (B(prefix + "ShowValue") && Id(prefix + "TextType") == ElementId.InvalidElementId)
                    throw new InvalidOperationException("Đang bật Hiện giá trị: hãy chọn Text Type hoặc tắt tùy chọn.");

                var template = doc.GetElement(Id(prefix + "Template")) as View;
                if (template != null)
                {
                    var scaleId = new ElementId(BuiltInParameter.VIEW_SCALE);
                    if (template.GetTemplateParameterIds().Contains(scaleId) &&
                        !template.GetNonControlledTemplateParameterIds().Contains(scaleId) &&
                        template.Scale != Scale(prefix))
                    {
                        throw new InvalidOperationException("View Template đang khóa tỷ lệ khác tỷ lệ nhập. Chỉnh template hoặc tỷ lệ trước khi chạy.");
                    }
                }
            }

            string wanted = SheetNumberFor();
            ViewSheet existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .FirstOrDefault(x => string.Equals((x.SheetNumber ?? "").Trim(), wanted, StringComparison.OrdinalIgnoreCase));

            if (existing != null && !replaceExistingSheet)
                throw new InvalidOperationException("Sheet đã tồn tại: " + wanted + ". Chọn số sheet mới.");
            if (existing != null && replaceExistingSheet &&
                !V4SheetOwnership.IsOwned(doc, existing, works))
                throw new InvalidOperationException(
                    "Sheet " + wanted + " chưa có metadata V4 đúng tuyến cột/đúng tập View. " +
                    "Không thể tự động thay Sheet này. Hồ sơ V3 cũ cần được chuyển đổi có kiểm soát.");
        }

        private void RemoveExistingKStructV3Sheet(Document doc, IList<V3SegmentWork> works)
        {
            string wanted = SheetNumberFor();
            ViewSheet existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .FirstOrDefault(x => string.Equals((x.SheetNumber ?? "").Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return;
            if (!replaceExistingSheet || !V4SheetOwnership.IsOwned(doc, existing, works))
                throw new InvalidOperationException(
                    "Không được thay Sheet chưa xác thực sở hữu V4: " + wanted);

            List<ElementId> generatedViews = existing.GetAllPlacedViews().ToList();
            // These views are exactly the ones stamped by V4 when the sheet was created.
            // They must not have been removed, moved to another sheet or mixed with user views.
            doc.Delete(existing.Id);
            foreach (ElementId viewId in generatedViews)
                if (doc.GetElement(viewId) != null) doc.Delete(viewId);
            doc.Regenerate();
        }

        // Một điểm vào duy nhất; phần dựng thực tế nằm ở BuildStack (V3DrawingLayout.cs).
        internal List<ElementId> Build(Document doc, IList<V3SegmentWork> works)
        {
            return BuildStack(doc, works);
        }

        // Chiếu vuông góc một điểm lên mặt phẳng của view (đi qua View.Origin).
        private static XYZ Project(View view, XYZ point)
        {
            XYZ normal = view.ViewDirection;
            return point - normal * (point - view.Origin).DotProduct(normal);
        }

        private static string UniqueViewName(Document doc, string baseName)
        {
            var used = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(baseName)) return baseName;
            for (int i = 2; ; i++)
            {
                string candidate = baseName + " (" + i + ")";
                if (!used.Contains(candidate)) return candidate;
            }
        }

        private static string SafeDrawingLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            char[] invalid = { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '[', ']' };
            string clean = new string(value.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c).ToArray());
            return clean.Length > 80 ? clean.Substring(0, 80) : clean;
        }

        private ViewSection Section(Document doc, V3SegmentWork work, string prefix, double z, double top)
        {
            var g = work.Geometry;
            var t = Transform.Identity;

            XYZ x = g.X;
            XYZ y = prefix == "Cross" ? new XYZ(-x.Y, x.X, 0) : XYZ.BasisZ;
            XYZ normal = x.CrossProduct(y).Normalize();

            t.Origin = new XYZ(g.Center.X, g.Center.Y, z);
            t.BasisX = x;
            t.BasisY = y;
            t.BasisZ = normal;

            double margin = V3Units.Ft(250);
            var box = new BoundingBoxXYZ { Transform = t };

            if (prefix == "Cross")
            {
                box.Min = new XYZ(-g.Width / 2 - margin, -g.Depth / 2 - margin, -V3Units.Ft(25));
                box.Max = new XYZ(g.Width / 2 + margin, g.Depth / 2 + margin, V3Units.Ft(25));
            }
            else
            {
                box.Min = new XYZ(-g.Width / 2 - margin, -margin, -g.Depth / 2 - margin);
                box.Max = new XYZ(g.Width / 2 + margin, top - g.Bottom + margin, g.Depth / 2 + margin);
            }

            if (prefix == "Cross" && B("PullBars"))
                box.Max = new XYZ(box.Max.X + g.Width + V3Units.Ft(300), box.Max.Y, box.Max.Z);

            var viewFamilyType = doc.GetElement(Id(prefix + "SectionType")) as ViewFamilyType;
            ViewSection view;

            if (viewFamilyType != null && viewFamilyType.ViewFamily == ViewFamily.Detail)
                view = ViewSection.CreateDetail(doc, Id(prefix + "SectionType"), box);
            else
                view = ViewSection.CreateSection(doc, Id(prefix + "SectionType"), box);

            string mark = work.Column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            string markName = string.IsNullOrWhiteSpace(mark) ? ("C" + work.Column.Id.Value) : mark.Trim();

            string viewName = "KSTR V3 " + (prefix == "Cross" ? "MCN" : "MCD") + " " +
                (B("UseMarkForViewportName") ? markName : work.Column.Id.Value.ToString(CultureInfo.InvariantCulture));
            viewName += " - DRAFT";
            var level = doc.GetElement(work.Column.LevelId) as Level;
            if (prefix == "Cross" && level != null) viewName += " - " + level.Name;
            // Các tầng cùng Mark nên tên view phải được làm duy nhất, nếu không Revit báo trùng tên.
            view.Name = UniqueViewName(doc, viewName);

            if (Id(prefix + "Template") != ElementId.InvalidElementId)
            {
                if (!view.IsValidViewTemplate(Id(prefix + "Template")))
                    throw new InvalidOperationException("View Template '" + doc.GetElement(Id(prefix + "Template")).Name + "' không tương thích với mặt cắt.");
                view.ViewTemplateId = Id(prefix + "Template");
            }

            int scale = Scale(prefix);
            if (view.Scale != scale) view.Scale = scale;
            view.CropBoxActive = true;
            view.CropBoxVisible = false;

            foreach (var category in new[]
            {
                BuiltInCategory.OST_Levels,
                BuiltInCategory.OST_Grids,
                BuiltInCategory.OST_Sections,
                BuiltInCategory.OST_Elev,
                BuiltInCategory.OST_VolumeOfInterest
            })
            {
                var categoryId = new ElementId(category);
                try
                {
                    if (view.CanCategoryBeHidden(categoryId))
                        view.SetCategoryHidden(categoryId, true);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // View Template đang điều khiển Visibility/Graphics: giữ nguyên theo template.
                }
            }

            var annotationCrop = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
            if (annotationCrop != null && !annotationCrop.IsReadOnly)
                annotationCrop.Set(0);

            return view;
        }

        // ---------------------------------------------------------------------------------
        // Ghi chú view: Tag thép, Multi-Rebar Annotation, kích thước tiết diện.
        // ---------------------------------------------------------------------------------

        private void Annotate(Document doc, V3SegmentWork work, ViewSection view, string prefix)
        {
            if (!Enabled) return;
            bool isLong = prefix == "Long";

            if (!isLong && B("CrossShowValue")) CrossDimensions(doc, work, view);
            if (!B(prefix + "ShowTag")) return;

            FamilySymbol stirrupTag = ActiveTagSymbol(doc, Id(prefix + "StirrupTag"));
            FamilySymbol mainTag = ActiveTagSymbol(doc, Id(prefix + "MainTag"));

            var stirrups = new List<Rebar>();
            var mains = new List<Rebar>();
            foreach (Rebar bar in ColumnRebars(doc, work, view))
            {
                if (IsStirrupBar(bar)) stirrups.Add(bar); else mains.Add(bar);
            }

            if (isLong)
            {
                // Tag thép dọc của mặt cắt dọc được đặt cùng hình khai triển (DrawBarShapes).
                if (stirrupTag == null || stirrups.Count == 0) return;
                var spans = new HashSet<string>();
                int tagged = 0, failed = 0;
                foreach (Rebar bar in stirrups.OrderBy(b => b.Id.Value))
                {
                    try { TagStirrupLeft(doc, work, view, bar, stirrupTag, spans); tagged++; }
                    catch (Autodesk.Revit.Exceptions.ArgumentException) { failed++; }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException) { failed++; }
                }
                if (failed > 0)
                    throw new InvalidOperationException("Thiếu " + failed + " Tag thép đai ở mặt cắt dọc; hủy bản vẽ chưa đầy đủ.");
                return;
            }

            var g = work.Geometry;
            double s = view.Scale;
            XYZ right = view.RightDirection, up = view.UpDirection;
            XYZ center = Project(view, g.At(0, 0, 0));

            HashSet<ElementId> covered = mains.Count > 0
                ? AnnotateMultiRebar(doc, work, view, mains)
                : new HashSet<ElementId>();

            if (mainTag != null)
            {
                foreach (Rebar bar in mains.OrderBy(b => b.Id.Value))
                {
                    if (covered.Contains(bar.Id)) continue;
                    XYZ at;
                    if (!BarPoint(bar, out at)) continue;
                    XYZ onView = Project(view, at);
                    XYZ outward = onView - center;
                    outward = new XYZ(outward.DotProduct(right), outward.DotProduct(up), 0);
                    outward = outward.GetLength() < 1e-6 ? XYZ.BasisX : outward.Normalize();
                    XYZ direction = right * outward.X + up * outward.Y;
                    if (!TryTag(doc, view, mainTag, bar, onView + direction * V3Units.Ft(10 * s)))
                        throw new InvalidOperationException("Không thể gắn Tag thép chủ " + bar.Id.Value + " tại " + view.Name);
                }
            }

            if (stirrupTag != null && stirrups.Count > 0)
            {
                double tol = doc.Application.ShortCurveTolerance;
                double cutZ = view.Origin.Z;
                Rebar chosen = null;
                foreach (Rebar bar in stirrups.OrderBy(b => b.Id.Value))
                {
                    double lo, hi;
                    if (SpanZ(bar, out lo, out hi) && cutZ >= lo - tol && cutZ <= hi + tol) { chosen = bar; break; }
                }
                if (chosen == null) chosen = stirrups.OrderBy(b => b.Id.Value).First();
                XYZ head = center + right * (g.Width / 2 + V3Units.Ft(12 * s)) + up * (g.Depth / 2);
                if (!TryTag(doc, view, stirrupTag, chosen, head))
                    throw new InvalidOperationException("Không thể gắn Tag đai tại " + view.Name);
            }
        }

        private static List<Rebar> ColumnRebars(Document doc, V3SegmentWork work, View view)
        {
            string partition = V3RebarBuilder.Partition(work.Column.Id);
            var result = new List<Rebar>();
            foreach (Element element in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Rebar)))
            {
                var bar = element as Rebar;
                if (bar == null) continue;
                Parameter p = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                if (p == null || p.AsString() != partition) continue;
                result.Add(bar);
            }
            return result;
        }

        private static FamilySymbol ActiveTagSymbol(Document doc, ElementId id)
        {
            if (id == ElementId.InvalidElementId) return null;
            var symbol = doc.GetElement(id) as FamilySymbol;
            if (symbol != null && !symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
            return symbol;
        }

        private static bool BarPoint(Rebar bar, out XYZ point)
        {
            point = null;
            try
            {
                var curves = bar.GetCenterlineCurves(false, true, true, bar.IsRebarFreeForm() ? MultiplanarOption.IncludeAllMultiplanarCurves : MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                if (curves.Count == 0) return false;
                point = curves[0].GetEndPoint(0);
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return false; }
        }

        private static bool SpanZ(Rebar bar, out double lo, out double hi)
        {
            lo = 0; hi = 0;
            try
            {
                var first = bar.GetCenterlineCurves(false, true, true, bar.IsRebarFreeForm() ? MultiplanarOption.IncludeAllMultiplanarCurves : MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                var last = bar.GetCenterlineCurves(false, true, true, bar.IsRebarFreeForm() ? MultiplanarOption.IncludeAllMultiplanarCurves : MultiplanarOption.IncludeOnlyPlanarCurves,
                    Math.Max(0, bar.NumberOfBarPositions - 1));
                if (first.Count == 0 || last.Count == 0) return false;
                double z0 = first[0].GetEndPoint(0).Z, z1 = last[0].GetEndPoint(0).Z;
                lo = Math.Min(z0, z1); hi = Math.Max(z0, z1);
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return false; }
        }

        private static bool TryTag(Document doc, View view, FamilySymbol symbol, Rebar bar, XYZ head)
        {
            try
            {
                IndependentTag.Create(doc, symbol.Id, view.Id, new Reference(bar), true, TagOrientation.Horizontal, head);
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { return false; }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { return false; }
        }

        // Hai kích thước bao tiết diện cột: chiều rộng (phía trên) và chiều sâu (phía trái).
        private void CrossDimensions(Document doc, V3SegmentWork work, ViewSection view)
        {
            var type = doc.GetElement(Id("CrossDimension")) as DimensionType;
            if (type == null)
                throw new InvalidOperationException("Chưa chọn loại Dimension cho mặt cắt ngang.");

            var g = work.Geometry;
            XYZ right = view.RightDirection, up = view.UpDirection;
            XYZ center = Project(view, g.At(0, 0, 0));
            double offset = V3DrawStyle.P(view, V3DrawStyle.CrossDimOffset);

            try
            {
                ReferenceArray widthRefs = V3ColumnReferences.Find(work.Column, right,
                    FamilyInstanceReferenceType.Left, FamilyInstanceReferenceType.Right);
                XYZ top = center + up * (g.Depth / 2 + offset);
                doc.Create.NewDimension(view,
                    Line.CreateBound(top - right * (g.Width / 2), top + right * (g.Width / 2)), widthRefs, type);

                ReferenceArray depthRefs = V3ColumnReferences.Find(work.Column, up,
                    FamilyInstanceReferenceType.Front, FamilyInstanceReferenceType.Back);
                XYZ left = center - right * (g.Width / 2 + offset);
                doc.Create.NewDimension(view,
                    Line.CreateBound(left - up * (g.Depth / 2), left + up * (g.Depth / 2)), depthRefs, type);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                throw new InvalidOperationException("Không tạo được kích thước tiết diện cột " + work.Column.Id.Value +
                    ". Cần family có Reference hợp lệ.", ex);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
        }
    }
}
