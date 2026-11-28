using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    internal sealed partial class V3DrawingBuilder
    {
        private List<ElementId> BuildStack(Document doc,IList<V3SegmentWork> works)
        {
            var result=new List<ElementId>();if(!Enabled)return result;
            if(works==null||works.Count==0)return result;
            RemoveExistingKStructV3Sheet(doc, works);
            var first=works[0];var baseGeometry=first.Geometry;
            XYZ x=baseGeometry.X,y=new XYZ(-x.Y,x.X,0),origin=baseGeometry.Center;
            var points=new List<XYZ>();
            foreach(var w in works)
                foreach(double a in new[]{-w.Geometry.Width/2,w.Geometry.Width/2})
                    foreach(double b in new[]{-w.Geometry.Depth/2,w.Geometry.Depth/2})points.Add(w.Geometry.At(a,b,0)-new XYZ(origin.X,origin.Y,0));
            double bottom=works.Min(w=>w.Geometry.Bottom);
            double top=works.Max(w=>w.Geometry.Top+V3Units.Ft(w.Config.StarterBars
                ? (w.OutgoingStarterLengthMm > 0 ? w.OutgoingStarterLengthMm : w.Config.StarterLength)
                : 0));
            var partitions=new HashSet<string>(works.Select(w=>V3RebarBuilder.Partition(w.Column.Id)));
            foreach(var bar in new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>())
            {
                if(!partitions.Contains(bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString()))continue;
                var box=bar.get_BoundingBox(null);if(box==null)continue;
                foreach(double bx in new[]{box.Min.X,box.Max.X})
                foreach(double by in new[]{box.Min.Y,box.Max.Y})
                foreach(double bz in new[]{box.Min.Z,box.Max.Z})
                {
                    XYZ p=box.Transform.OfPoint(new XYZ(bx,by,bz));
                    points.Add(p-new XYZ(origin.X,origin.Y,0));bottom=Math.Min(bottom,p.Z);top=Math.Max(top,p.Z);
                }
            }
            double minX=points.Min(p=>p.DotProduct(x)),maxX=points.Max(p=>p.DotProduct(x));
            double minY=points.Min(p=>p.DotProduct(y)),maxY=points.Max(p=>p.DotProduct(y));
            var aggregate=new V3Geometry {
                Center=new XYZ(origin.X,origin.Y,0)+x*((minX+maxX)/2)+y*((minY+maxY)/2),X=x,Y=y,
                Width=maxX-minX,Depth=maxY-minY,Bottom=bottom,Top=top
            };
            var overview=new V3SegmentWork {Column=first.Column,Segment=first.Segment,Geometry=aggregate,Config=first.Config};
            var longitudinal=Section(doc,overview,"Long",aggregate.Bottom,top);
            string mark=first.Column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            longitudinal.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.Set(string.IsNullOrWhiteSpace(mark)?"MẶT CẮT DỌC TUYẾN CỘT":mark.Trim()+"_MCD1");
            doc.Regenerate();
            foreach(var w in works) { Annotate(doc,w,longitudinal,"Long"); ZoneDetail(doc,w,longitudinal); StarterDim(doc,w,longitudinal); }
            BreakLines(doc,works,longitudinal);
            DrawBarShapes(doc,works,longitudinal,aggregate);
            var ordered=works.OrderBy(w=>w.Geometry.Bottom).ToList();
            FitCrop(doc,longitudinal);
            var sections=new List<View>();int index=2;
            foreach(var w in ordered)
            {
                // Each story has three true horizontal cuts, not three duplicated mid-height views.
                double height=w.Geometry.Height;
                double inset=Math.Min(V3Units.Ft(100),height*0.12);
                var levels=new[]{
                    Tuple.Create("BASE",w.Geometry.Bottom+inset),
                    Tuple.Create("MID",w.Geometry.Bottom+height/2),
                    Tuple.Create("NODE",w.Geometry.Top-inset)
                };
                foreach(var level in levels)
                {
                    var cross=Section(doc,w,"Cross",level.Item2,w.Geometry.Top);
                    cross.Name = UniqueViewName(doc, "MC-" + index + "-" + index);
                    cross.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.Set(
                        "MC: "+index+"-"+index+" "+level.Item1);
                    doc.Regenerate();Annotate(doc,w,cross,"Cross");
                    if(B("CrossShowValue") && level.Item1=="MID")FamilySectionTable(doc,w,cross);
                    FitCrop(doc,cross);
                    sections.Add(cross);index++;
                }
            }
            // A real 3D rebar view belongs to the generated sheet, not a disposable preview.
            var threeDType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().FirstOrDefault(type => type.ViewFamily == ViewFamily.ThreeDimensional);
            if (threeDType == null)
                throw new InvalidOperationException("Không có ViewFamilyType 3D để tạo View thép cột.");
            var rebar3D = View3D.CreateIsometric(doc, threeDType.Id);
            rebar3D.Name = UniqueViewName(doc, "KSTR V3 3D " + first.Column.Id.Value + " - DRAFT");
            double boxMargin = V3Units.Ft(200);
            // The section box is world-axis aligned. The radius encloses columns
            // rotated relative to project X/Y; a local Width/Depth box would crop them.
            double radius = Math.Sqrt(aggregate.Width*aggregate.Width +
                aggregate.Depth*aggregate.Depth)/2 + boxMargin;
            rebar3D.SetSectionBox(new BoundingBoxXYZ {
                Min = new XYZ(aggregate.Center.X-radius, aggregate.Center.Y-radius, aggregate.Bottom-boxMargin),
                Max = new XYZ(aggregate.Center.X+radius, aggregate.Center.Y+radius, aggregate.Top+boxMargin)
            });
            rebar3D.IsSectionBoxActive = true;
            rebar3D.DetailLevel = ViewDetailLevel.Fine;
            foreach (var bar in new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>())
            {
                if (!partitions.Contains(bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString()))
                    continue;
                bar.SetUnobscuredInView(rebar3D, true);
                // Revit 2024's Rebar API does not expose SetSolidInView.
                // Unobscured visibility is supported; solid appearance depends on View detail/style.
            }
            sections.Add(rebar3D);
            string stackMark=first.Column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            string markName=string.IsNullOrWhiteSpace(stackMark)?("C"+first.Column.Id.Value):stackMark.Trim();
            string sheetName=B("UseMarkForSheetName")?markName:S("SheetName");
            // Project Marks and user-provided Sheet Names may contain symbols which
            // are not allowed in Revit element names. Normalize before assigning.
            sheetName = SafeSheetTitle(sheetName, 240);
            var sheet=ViewSheet.Create(doc,Id("TitleBlock"));sheet.SheetNumber=SheetNumberFor();
            sheet.Name=sheetName;
            doc.Regenerate();PlaceStack(doc,sheet,longitudinal,sections);
            InspectCompleteSheetLayout(doc, sheet);
            V4SheetOwnership.Stamp(doc, sheet, works);
            result.Add(sheet.Id);return result;
        }

        // Inspect the completed sheet, including any non-revision schedules.
        private void InspectCompleteSheetLayout(Document doc, ViewSheet sheet)
        {
            doc.Regenerate();
            double pad = V3Units.Ft(20);
            double reserve = V3Units.Ft(double.Parse(S("SheetRightMargin"),
                System.Globalization.CultureInfo.InvariantCulture));
            var outline = sheet.Outline;
            var allowed = new V4SheetLayoutGuard.Rect(outline.Min.U + pad,
                outline.Min.V + pad, outline.Max.U - pad - reserve, outline.Max.V - pad);
            var boxes = new List<V4SheetLayoutGuard.Rect>();
            foreach (var viewport in sheet.GetAllViewports()
                .Select(id => doc.GetElement(id) as Viewport).Where(item => item != null))
            {
                var box = viewport.GetBoxOutline();
                boxes.Add(new V4SheetLayoutGuard.Rect(box.MinimumPoint.X, box.MinimumPoint.Y,
                    box.MaximumPoint.X, box.MaximumPoint.Y));
            }
            foreach (var instance in new FilteredElementCollector(doc)
                .OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                .Where(item => item.OwnerViewId == sheet.Id && !item.IsTitleblockRevisionSchedule))
            {
                var box = instance.get_BoundingBox(sheet);
                if (box == null)
                    throw new InvalidOperationException("Không xác định được phạm vi bảng thống kê trên Sheet.");
                boxes.Add(new V4SheetLayoutGuard.Rect(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y));
            }
            if (V4SheetLayoutGuard.Inspect(allowed, boxes).Count == 0) return;
            const string suffix = " - CẦN CHỈNH VIEWPORT";
            string name = sheet.Name ?? "";
            if (!name.EndsWith(suffix, StringComparison.Ordinal))
                sheet.Name = SafeSheetTitle(name, 240 - suffix.Length) + suffix;
        }

        // Hai chuỗi Dim bên trái cột: chuỗi trong = từng vùng đai (kể cả đoạn lớp bảo vệ hai đầu),
        // chuỗi ngoài = tổng chiều cao đoạn cột. Khoảng cách lấy từ V3DrawStyle (mm trên giấy).
        private void ZoneDetail(Document doc,V3SegmentWork work,View view)
        {
            if(!B("LongShowValue"))return;
            var g=work.Geometry;var c=work.Config;
            double inset=V3Units.Ft(c.Cover+c.TieDiameter/2);
            double low=g.Bottom+inset,high=g.Top-inset;
            double zone=Math.Min(V3Units.Ft(c.EndZone),(high-low)/2);
            ChainLeft(doc,view,g,new[]{g.Bottom,low,low+zone,high-zone,high,g.Top},V3DrawStyle.ZoneChainOffset);
            ChainLeft(doc,view,g,new[]{g.Bottom,g.Top},V3DrawStyle.TotalChainOffset);
            ExtendCropLeft(view,g,V3DrawStyle.TotalChainOffset);
        }

        // Thép chờ lên tầng trên: Dim chiều dài thép chờ phía trên đỉnh cột, cùng đường Dim với chuỗi vùng đai.
        private void StarterDim(Document doc,V3SegmentWork work,View view)
        {
            if(!AnnotationEnabled("LongDimensionEnabled", "LongShowValue"))return;
            if(!work.Config.StarterBars)return;
            var g=work.Geometry;
            double starterLength = work.OutgoingStarterLengthMm > 0
                ? work.OutgoingStarterLengthMm : work.Config.StarterLength;
            double lap=V3Units.Ft(starterLength);
            if(lap<doc.Application.ShortCurveTolerance)return;
            ChainLeft(doc,view,g,new[]{g.Top,g.Top+lap},V3DrawStyle.ZoneChainOffset);
        }

        // Một chuỗi Dim dọc: đường Dim cách mép trái cột offsetPaper mm giấy, chân Dim (Detail Line ngắn) tại mép cột.
        private void ChainLeft(Document doc,View view,V3Geometry g,double[] zs,double offsetPaper)
        {
            var type=doc.GetElement(Id("LongDimension")) as DimensionType;
            if(type==null)return;
            XYZ edge=Project(view,g.At(0,0,0))-view.RightDirection*(g.Width/2);
            XYZ line=edge-view.RightDirection*V3DrawStyle.P(view,offsetPaper);
            double tick=V3DrawStyle.P(view,V3DrawStyle.TickLength),tol=doc.Application.ShortCurveTolerance;
            var references=new ReferenceArray();var used=new List<double>();
            foreach(double z in zs.OrderBy(v=>v))
            {
                if(used.Count>0&&Math.Abs(z-used.Last())<tol)continue;
                XYZ face=edge+XYZ.BasisZ*z;
                var stub=doc.Create.NewDetailCurve(view,Line.CreateBound(face-view.RightDirection*tick,face));
                references.Append(stub.GeometryCurve.Reference);used.Add(z);
            }
            if(used.Count<2)return;
            doc.Regenerate();
            doc.Create.NewDimension(view,Line.CreateBound(line+XYZ.BasisZ*used.First(),line+XYZ.BasisZ*used.Last()),references,type);
        }

        private void ExtendCropLeft(View view,V3Geometry g,double offsetPaper)
        {
            XYZ p=Project(view,g.At(0,0,0))-view.RightDirection*(g.Width/2+V3DrawStyle.P(view,offsetPaper+V3DrawStyle.CropMargin));
            var box=view.CropBox;XYZ q=box.Transform.Inverse.OfPoint(p);
            box.Min=new XYZ(Math.Min(box.Min.X,q.X),box.Min.Y,box.Min.Z);view.CropBox=box;
        }

        // Bảng thép dưới mặt cắt: canh mép trái theo mặt cắt, khung đỏ, 3 dòng như bản vẽ mẫu.
        private void FamilySectionTable(Document doc, V3SegmentWork work, View view)
        {
            const string familyOrTypeName = "BS_Rebar_Detail 2D";
            FamilySymbol symbol = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(item => string.Equals(item.FamilyName, familyOrTypeName, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(item.Name, familyOrTypeName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (symbol == null)
                throw new InvalidOperationException(
                    "Project chưa có Detail Family 'BS_Rebar_Detail 2D'. Không dựng bảng bằng TextNote/DetailLine thay thế.");

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            var c = work.Config;
            bool fixedNumber = c.TieLayout == V3TieLayout.FixedNumber;
            XYZ anchor = Project(view, work.Geometry.At(0, 0, work.Geometry.Bottom + work.Geometry.Height / 2)) -
                         view.RightDirection * (work.Geometry.Width / 2) -
                         view.UpDirection * (work.Geometry.Depth / 2 + V3DrawStyle.P(view, V3DrawStyle.TableGap));
            FamilyInstance table;
            try
            {
                table = doc.Create.NewFamilyInstance(anchor, symbol, view);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Không đặt được family bảng 'BS_Rebar_Detail 2D' vào mặt cắt ngang.", ex);
            }

            // Never edit a shared family type used by existing tables. If this family
            // stores table data on its type, give this new table a private type first.
            string[] tableFields = { "Quantity", "Bar Diameter_Main Rebar", "Bar Diameter_StirrupTop&Bot",
                "Bar Diameter_Stirrup Mid", "Spacing_Stirrup Top&Bot", "Spacing_Stirrup Mid", "VNI", "ENG" };
            if (tableFields.Any(name => FindTableParameter(table, name) == null &&
                                       FindTableParameter(table.Symbol, name) != null))
            {
                table.Symbol = (FamilySymbol)table.Symbol.Duplicate("KSTR_Table_" + Guid.NewGuid().ToString("N"));
                doc.Regenerate();
            }

            SetTableNumber(table, "Quantity", c.BarCount, false);
            SetTableNumber(table, "Bar Diameter_Main Rebar", c.MainDiameter, true);
            SetTableNumber(table, "Bar Diameter_StirrupTop&Bot", c.TieDiameter, true);
            SetTableNumber(table, "Bar Diameter_Stirrup Mid", c.TieDiameter, true);
            SetTableNumber(table, "Spacing_Stirrup Top&Bot", fixedNumber ? c.EndTieCount : c.EndSpacing, !fixedNumber);
            SetTableNumber(table, "Spacing_Stirrup Mid", fixedNumber ? c.MidTieCount : c.MidSpacing, !fixedNumber);
            SetTableBoolean(table, "VNI", true);
            SetTableBoolean(table, "ENG", false);
        }

        private static void SetTableNumber(FamilyInstance table, string name, double value, bool isLength)
        {
            Parameter parameter = RequireTableParameter(table, name);
            if (parameter.StorageType == StorageType.String)
            {
                string text = value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                if (parameter.AsString() == text) return;
                RequireWritableTableParameter(table, parameter, name);
                if (!parameter.Set(text)) throw new InvalidOperationException("Không ghi được tham số bảng: " + name);
                return;
            }
            if (parameter.StorageType == StorageType.Integer)
            {
                if (Math.Abs(value - Math.Round(value)) > 1e-6)
                    throw new InvalidOperationException("Tham số bảng Integer không thể chứa giá trị " + value + ": " + name);
                int integer = checked((int)Math.Round(value));
                if (parameter.AsInteger() == integer && parameter.HasValue) return;
                RequireWritableTableParameter(table, parameter, name);
                if (!parameter.Set(integer)) throw new InvalidOperationException("Không ghi được tham số bảng: " + name);
                return;
            }
            if (parameter.StorageType != StorageType.Double)
                throw new InvalidOperationException("Property bảng không phải Number/Length: " + name);
            var dataType = parameter.Definition.GetDataType();
            bool usesMillimeters = dataType != SpecTypeId.Number &&
                UnitUtils.IsMeasurableSpec(dataType) &&
                UnitUtils.IsValidUnit(dataType, UnitTypeId.Millimeters);
            if (!isLength && dataType != SpecTypeId.Number)
                throw new InvalidOperationException("Tham số số lượng không phải Number: " + name +
                    " (data type: " + dataType.TypeId + ")");
            if (isLength && dataType != SpecTypeId.Number && !usesMillimeters)
                throw new InvalidOperationException("Tham số bảng không hỗ trợ đơn vị mm: " + name +
                    " (data type: " + dataType.TypeId + ")");
            double stored = usesMillimeters
                ? UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters) : value;
            if (parameter.HasValue && Math.Abs(parameter.AsDouble() - stored) < 1e-8) return;
            RequireWritableTableParameter(table, parameter, name);
            if (!parameter.Set(stored)) throw new InvalidOperationException("Không ghi được tham số bảng: " + name);
        }

        private static void SetTableBoolean(FamilyInstance table, string name, bool value)
        {
            Parameter parameter = RequireTableParameter(table, name);
            if (parameter.StorageType != StorageType.Integer)
                throw new InvalidOperationException("Property bảng không phải Yes/No: " + name);
            int stored = value ? 1 : 0;
            if (parameter.HasValue && parameter.AsInteger() == stored) return;
            RequireWritableTableParameter(table, parameter, name);
            if (!parameter.Set(stored)) throw new InvalidOperationException("Không ghi được tham số bảng: " + name);
        }

        private static Parameter FindTableParameter(Element element, string name)
        {
            var parameters = element.Parameters.Cast<Parameter>().ToList();
            int index = V3TableParameterNames.Find(parameters.Select(p => p.Definition.Name).ToList(), name);
            return index < 0 ? null : parameters[index];
        }

        private static Parameter RequireTableParameter(FamilyInstance table, string name)
        {
            var parameter = FindTableParameter(table, name) ?? FindTableParameter(table.Symbol, name);
            if (parameter != null)
            {
                if (parameter.IsReadOnly) table.Document.Regenerate();
                return parameter;
            }
            string available = string.Join(", ", table.Parameters.Cast<Parameter>()
                .Concat(table.Symbol.Parameters.Cast<Parameter>()).Select(p => p.Definition.Name)
                .Where(n => n.IndexOf("Stirrup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Diameter", StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct().OrderBy(n => n));
            throw new InvalidOperationException("Family bảng '" + table.Symbol.FamilyName + " : " + table.Symbol.Name +
                "' thiếu tham số '" + name + "' trên cả Instance và Type. Các tham số thép hiện có: " + available);
        }

        private static void RequireWritableTableParameter(FamilyInstance table, Parameter parameter, string name)
        {
            if (parameter.IsReadOnly)
                throw new InvalidOperationException("Family bảng '" + table.Symbol.FamilyName + "': tham số '" +
                    name + "' chỉ đọc/có công thức và giá trị hiện tại không khớp thép cột. Cần sửa tham số đầu vào của family.");
        }

        // Legacy implementation retained for source compatibility only. New
        // sheets use the project family above so its labels and Property values
        // remain controlled by the office template.
        private void LegacyTextSectionTable(Document doc,V3SegmentWork w,View view)
        {
            var c=w.Config;var g=w.Geometry;
            double width=V3DrawStyle.P(view,V3DrawStyle.TableWidth),row=V3DrawStyle.P(view,V3DrawStyle.TableRow);
            double split=V3DrawStyle.TableSplit;
            XYZ at=Project(view,g.At(0,0,g.Bottom+g.Height/2))-view.RightDirection*(g.Width/2)-
                view.UpDirection*(g.Depth/2+V3DrawStyle.P(view,V3DrawStyle.TableGap));
            bool fixedN=c.TieLayout==V3TieLayout.FixedNumber;
            string[,] data={ {"THÉP CHỦ",c.BarCount+"Ø"+c.MainDiameter},
                {"THÉP ĐAI TOP & BOT", "Ø"+c.TieDiameter+(fixedN?" | "+c.EndTieCount+" vị trí/vùng":"a"+c.EndSpacing)},
                {"THÉP ĐAI MID", "Ø"+c.TieDiameter+(fixedN?" | "+c.MidTieCount+" vị trí/vùng":"a"+c.MidSpacing)} };
            var red=V3DrawStyle.Line(doc,"KSTR Bảng đỏ",new Color(255,0,0),2,null);
            for(int r=0;r<=3;r++)
                doc.Create.NewDetailCurve(view,Line.CreateBound(at-view.UpDirection*row*r,at+view.RightDirection*width-view.UpDirection*row*r)).LineStyle=red;
            foreach(double col in new[]{0.0,split,1.0})
                doc.Create.NewDetailCurve(view,Line.CreateBound(at+view.RightDirection*(width*col),at+view.RightDirection*(width*col)-view.UpDirection*row*3)).LineStyle=red;
            for(int r=0;r<3;r++)for(int col=0;col<2;col++)
                TextNote.Create(doc,view.Id,at+view.RightDirection*(width*(col==0?.02:split+.02))-view.UpDirection*(row*(r+.15)),data[r,col],Id("CrossTextType"));
            // Detail lines must lie inside the model crop as well as the annotation region.
            var box=view.CropBox;Transform inverse=box.Transform.Inverse;
            var corners=new[]{at,at+view.RightDirection*width,at-view.UpDirection*row*3,at+view.RightDirection*width-view.UpDirection*row*3}.Select(p=>inverse.OfPoint(p)).ToList();
            box.Min=new XYZ(Math.Min(box.Min.X,corners.Min(p=>p.X))-V3Units.Ft(30),Math.Min(box.Min.Y,corners.Min(p=>p.Y))-V3Units.Ft(30),box.Min.Z);
            box.Max=new XYZ(Math.Max(box.Max.X,corners.Max(p=>p.X))+V3Units.Ft(30),Math.Max(box.Max.Y,corners.Max(p=>p.Y))+V3Units.Ft(30),box.Max.Z);
            view.CropBox=box;
        }

        // Revit 2024 rejects names containing these punctuation/control characters.
        // Keep Unicode Vietnamese letters while replacing invalid punctuation with '-'.
        private static string SafeSheetTitle(string proposed, int maxLength)
        {
            if (maxLength < 1) throw new ArgumentOutOfRangeException(nameof(maxLength));
            var forbidden = new HashSet<char>("{ }[ ]|;<>?`~\\:".Replace(" ", "").ToCharArray());
            var normalized = new string((proposed ?? "").Select(ch =>
                char.IsControl(ch) || forbidden.Contains(ch) ? '-' : ch).ToArray()).Trim();
            if (normalized.Length > maxLength) normalized = normalized.Substring(0, maxLength).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                throw new InvalidOperationException("Không có tên Sheet hợp lệ sau khi chuẩn hóa.");
            if (!NamingUtils.IsValidName(normalized))
                throw new InvalidOperationException("Tên Sheet vẫn chứa ký tự không được Revit cho phép: " + normalized);
            return normalized;
        }

        private void PlaceStack(Document doc,ViewSheet sheet,View longitudinal,IList<View> sections)
        {
            var bounds=sheet.Outline;
            double pad=V3Units.Ft(20),gap=V3Units.Ft(15);
            double reserve=V3Units.Ft(double.Parse(S("SheetRightMargin"),System.Globalization.CultureInfo.InvariantCulture));
            double left=bounds.Min.U+pad,right=bounds.Max.U-pad-reserve,bottom=bounds.Min.V+pad,top=bounds.Max.V-pad;
            if(right<=left)throw new InvalidOperationException("Vùng chừa khung tên vượt bề rộng sheet.");
            var longPort=Viewport.Create(doc,sheet.Id,longitudinal.Id,XYZ.Zero);
            var ports=sections.Select(v=>Viewport.Create(doc,sheet.Id,v.Id,XYZ.Zero)).ToList();doc.Regenerate();
            var longBox=longPort.GetBoxOutline();double longWidth=longBox.MaximumPoint.X-longBox.MinimumPoint.X;
            double longHeight=longBox.MaximumPoint.Y-longBox.MinimumPoint.Y;
            var widths=ports.Select(p=>p.GetBoxOutline().MaximumPoint.X-p.GetBoxOutline().MinimumPoint.X).ToList();
            var heights=ports.Select(p=>p.GetBoxOutline().MaximumPoint.Y-p.GetBoxOutline().MinimumPoint.Y).ToList();
            double labelSpace=V3Units.Ft(12),crossWidth=widths.Max();
            longPort.SetBoxCenter(new XYZ(left+longWidth/2,top-longHeight/2,0));
            longPort.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.Set("1");
            // Bố trí mặt cắt ngang theo thuật toán tự động chia cột (nhiều tầng thì xuống cột kế bên)
            double cursorY = top;
            double currentColRight = right;
            for (int i = 0; i < ports.Count; i++)
            {
                double hNeeded = heights[i] + labelSpace + gap;
                if (cursorY - hNeeded < bottom && cursorY < top - 0.01)
                {
                    currentColRight -= (crossWidth + gap);
                    cursorY = top;
                }
                ports[i].SetBoxCenter(new XYZ(currentColRight - widths[i] / 2, cursorY - heights[i] / 2, 0));
                ports[i].get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.Set((i + 2).ToString());
                cursorY -= hNeeded;
            }

            // Canh giữa tiêu đề viewport dưới khung view.
            doc.Regenerate();
            CenterViewportTitle(doc, longPort);
            foreach (var port in ports) CenterViewportTitle(doc, port);

            // Validate model viewports against the printable region before commit.
            // The transaction will rollback a stack that does not fit instead of
            // silently leaving cross sections outside the sheet or overlapping.
            doc.Regenerate();
            var viewportRects = new[] { longPort }.Concat(ports).Select(port =>
            {
                Outline box = port.GetBoxOutline();
                return new V4SheetLayoutGuard.Rect(
                    box.MinimumPoint.X, box.MinimumPoint.Y,
                    box.MaximumPoint.X, box.MaximumPoint.Y);
            }).ToList();
            // The operator prefers a generated sheet even when all viewports cannot
            // fit. Inspect the placement but do not roll back for overlap/overflow.
            // All views remain editable in Revit for manual layout corrections.
            var layoutWarnings = V4SheetLayoutGuard.Inspect(
                new V4SheetLayoutGuard.Rect(left, bottom, right, top), viewportRects);
            if (layoutWarnings.Count > 0)
            {
                // Square brackets are forbidden by Revit for element names.
                const string suffix = " - CẦN CHỈNH VIEWPORT";
                string currentName = sheet.Name ?? "";
                sheet.Name = SafeSheetTitle(currentName, 240 - suffix.Length) + suffix;
            }

            // Không tự chèn schedule đầu tiên của project: nó có thể thống kê toàn mô hình,
            // không giới hạn vào tuyến cột đang triển khai. Tạo schedule riêng qua nút nhóm "Tạo thép cột".
        }
    }
}
