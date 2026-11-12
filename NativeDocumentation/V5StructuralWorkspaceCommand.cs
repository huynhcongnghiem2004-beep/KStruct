using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Panel = System.Windows.Controls.Panel;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Grid = System.Windows.Controls.Grid;
using Color = System.Windows.Media.Color;
using Shapes = System.Windows.Shapes;
using SDK_HD.KStruct.Core.V5;

namespace SDK_HD.KStruct.NativeDocumentation
{
    /// <summary>V5 WPF workspace: diagnostic by default; isolated explicitly confirmed pilot writes only.</summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class V5StructuralWorkspaceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data,ref string message,ElementSet elements)
        {
            UIDocument ui=data.Application.ActiveUIDocument;
            if(ui==null||ui.Document.IsFamilyDocument)
            {
                TaskDialog.Show("KStruct V5","Hãy mở một mô hình Project RVT.");
                return Result.Cancelled;
            }
            try
            {
                Document doc=ui.Document;
                var selectedHosts=ui.Selection.GetElementIds()
                    .Select(id=>doc.GetElement(id))
                    .Where(e=>e is FamilyInstance &&
                        (e.Category?.Id.Value==(long)BuiltInCategory.OST_StructuralFraming ||
                         e.Category?.Id.Value==(long)BuiltInCategory.OST_StructuralColumns ||
                         e.Category?.Id.Value==(long)BuiltInCategory.OST_StructuralFoundation))
                    .Take(2).ToList();
                // Never silently choose an arbitrary Host from a multi-selection.
                if(selectedHosts.Count>1)
                {
                    TaskDialog.Show("KStruct V5",
                        "Có nhiều cấu kiện kết cấu đang được chọn. Hãy chỉ chọn một dầm, cột hoặc móng rồi mở lại Workspace.");
                    return Result.Cancelled;
                }
                Element selected=selectedHosts.FirstOrDefault();
                double[] barDiameters=new FilteredElementCollector(doc)
                    .OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                    .Select(x=>AuditV2Policy.Mm(x.BarModelDiameter))
                    .Where(AuditV2Policy.IsFinite).Distinct().OrderBy(x=>x).ToArray();
                while(true)
                {
                    var inventory=V5HostSolidInspector.Inspect(selected);
                    var cover=V5HostCoverInspector.Inspect(selected);
                    var window=new V5StructuralWorkspaceWindow(doc.Title,selected,barDiameters,inventory,cover);
                    new WindowInteropHelper(window).Owner=data.Application.MainWindowHandle;
                    window.ShowDialog();
                    if(window.RequestPilotCreation)
                    {
                        if(!V5PilotReleaseGate.Enabled)
                            throw new InvalidOperationException(
                                "Pilot V5 chưa được nghiệm thu trên Revit thực. " +
                                "Hãy sử dụng công cụ tạo thép V4 ở panel Thép cột / KStruct Dầm / Thép móng.");
                        if(!(selected is FamilyInstance pilotColumn))
                            throw new InvalidOperationException("Phải chọn một cột trước khi chạy Pilot.");
                        var confirm=new TaskDialog("KStruct V5 - PILOT")
                        {
                            MainInstruction="Tạo thử 4 thanh dọc và bộ đai Ø theo Maximum Spacing?",
                            MainContent="Chỉ sử dụng trên BẢN SAO RVT. Lệnh không tính toán kết cấu, " +
                                "chưa neo/nối hay tạo bản vẽ. Pilot sẽ từ chối cột " +
                                "không phải lăng trụ chữ nhật bê tông đơn nhất, và cột đã có thép. " +
                                "Không sử dụng các thanh này làm hồ sơ thi công.",
                            CommonButtons=TaskDialogCommonButtons.Yes|TaskDialogCommonButtons.No,
                            DefaultButton=TaskDialogResult.No
                        };
                        if(confirm.Show()!=TaskDialogResult.Yes) return Result.Cancelled;
                        int n=V5ColumnPilotWriter.Create(doc,pilotColumn,window.PilotInput);
                        TaskDialog.Show("KStruct V5 PILOT",
                            "Tạo xong "+n+" thanh thép dọc và bộ đai thử nghiệm. CHƯA có neo / nối. " +
                            "Không sử dụng cho thi công.");
                        return Result.Succeeded;
                    }
                    if(!window.RequestHostSelection) return Result.Succeeded;
                    try
                    {
                        // Modal WPF is closed before entering Revit's PickObject loop.
                        // This command is read-only and creates no model transaction.
                        Reference picked=ui.Selection.PickObject(ObjectType.Element,
                            new V5StructuralHostSelectionFilter(),
                            "Chọn một dầm, cột hoặc móng kết cấu rồi nhấn Finish.");
                        selected=doc.GetElement(picked.ElementId);
                        if(selected==null)
                        {
                            TaskDialog.Show("KStruct V5","Không đọc được cấu kiện đã chọn.");
                            return Result.Cancelled;
                        }
                    }
                    catch(Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        // Escape during selection exits without modifying the RVT.
                        return Result.Cancelled;
                    }
                }
            }
            catch(Exception ex)
            {
                message=ex.ToString();
                TaskDialog.Show("KStruct V5","Không hoàn thành lệnh V5: "+ex.Message);
                return Result.Failed;
            }
        }
    }

    // The unified production add-in uses V4 for real reinforcement and drawings.
    // V5 experimental writes require independent Revit runtime approval before enablement.
    internal static class V5PilotReleaseGate
    {
        internal const bool Enabled=false;
    }

    internal sealed class V5StructuralHostSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            if(!(element is FamilyInstance) || element.Category==null) return false;
            long id=element.Category.Id.Value;
            return id==(long)BuiltInCategory.OST_StructuralFraming ||
                   id==(long)BuiltInCategory.OST_StructuralColumns ||
                   id==(long)BuiltInCategory.OST_StructuralFoundation;
        }
        public bool AllowReference(Reference reference,XYZ position) => false;
    }

    internal sealed class V5StructuralWorkspaceWindow : Window
    {
        private static readonly Brush Navy=ColorBrush(21,39,59);
        private static readonly Brush Accent=ColorBrush(25,109,122);
        private static readonly Brush Muted=ColorBrush(92,105,119);
        private static readonly Brush Light=ColorBrush(245,248,250);
        private readonly Dictionary<string,TextBox> fields=new Dictionary<string,TextBox>();
        private readonly double[] types;
        private readonly string hostId;
        private readonly V5StructuralModule? hostModule;
        private readonly ComboBox module;
        private readonly TextBlock result;
        private readonly TextBlock status;
        private readonly Border shapePreview;
        private readonly Canvas sectionCanvas;
        internal bool RequestHostSelection { get; private set; }
        internal bool RequestPilotCreation { get; private set; }
        internal V5RebarInput PilotInput { get; private set; }

        internal V5StructuralWorkspaceWindow(string docTitle,Element host,double[] availableTypes,
            V5HostSolidInventory inventory,V5HostCoverSnapshot cover)
        {
            types=availableTypes??new double[0];
            hostId=host?.UniqueId;
            long? categoryId=host?.Category?.Id.Value;
            hostModule=categoryId==(long)BuiltInCategory.OST_StructuralFraming?V5StructuralModule.Beam:
                categoryId==(long)BuiltInCategory.OST_StructuralColumns?V5StructuralModule.Column:
                categoryId==(long)BuiltInCategory.OST_StructuralFoundation?V5StructuralModule.Footing:
                (V5StructuralModule?)null;
            Title="KStruct V5  |  Structural Workspace";
            Width=980;Height=720;MinWidth=850;MinHeight=650;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;
            Background=Light;
            FontFamily=new FontFamily("Segoe UI");
            var root=new Grid();
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            Content=root;

            var header=new StackPanel{Orientation=Orientation.Vertical,Background=Navy,
                Margin=new Thickness(0),};
            header.Children.Add(Label("KSTRUCT  /  V5 STRUCTURAL WORKSPACE",22,Brushes.White,true,
                new Thickness(28,20,20,2)));
            header.Children.Add(Label("Nhập liệu • Solid & Cover • Pilot tạo thép dọc + đai (cần xác nhận)",
                12,ColorBrush(192,211,224),false,new Thickness(28,0,20,19)));
            root.Children.Add(header);Grid.SetRow(header,0);

            var columns=new Grid{Margin=new Thickness(24,20,24,16)};
            columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1.25,GridUnitType.Star)});
            columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            Grid.SetRow(columns,1);root.Children.Add(columns);

            var left=new StackPanel{Margin=new Thickness(0,0,12,0)};
            var leftScroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=left};
            columns.Children.Add(leftScroll);Grid.SetColumn(leftScroll,0);
            left.Children.Add(Label("01  /  THÔNG TIN CẤU KIỆN",15,Navy,true,new Thickness(0,0,0,12)));
            left.Children.Add(Label("Mô hình: "+docTitle,12,Muted,false,new Thickness(0,0,0,5)));
            left.Children.Add(Label(host!=null
                ?"Host đã chọn: "+host.Id.Value+"  ·  "+SafeName(host)
                :"Chưa chọn Host. Chọn dầm/cột/móng trong Revit rồi mở lại Workspace.",
                12,host==null?ColorBrush(164,81,30):Accent,false,new Thickness(0,0,0,14)));
            var pickHost=Button(host==null?"Chọn cấu kiện trong Revit":"Đổi cấu kiện",
                Accent,Brushes.White);
            pickHost.MinWidth=188;
            pickHost.HorizontalAlignment=HorizontalAlignment.Left;
            pickHost.Margin=new Thickness(0,0,0,12);
            pickHost.Click+=(sender,e)=>{RequestHostSelection=true;Close();};
            left.Children.Add(pickHost);
            left.Children.Add(Label("SOLID THỰC TẾ (CHỈ ĐỌC): "+
                (inventory==null?"Không có kết quả":
                    inventory.SolidCount+" Solid, "+inventory.ConcreteMaterialCandidateSolids+
                    " ứng viên vật liệu bê tông, "+inventory.UnknownOrMixedMaterialSolids+
                    " Solid chưa rõ vật liệu, "+inventory.PlanarFaceCount+" mặt phẳng."),
                12,Navy,true,new Thickness(0,0,0,6)));
            left.Children.Add(Label(inventory?.Warnings.FirstOrDefault() ??
                "Không thể xác minh bê tông hay lớp bảo vệ chỉ bằng thống kê Solid.",
                11,ColorBrush(164,81,30),false,new Thickness(0,0,0,11)));
            left.Children.Add(Label("REBAR COVER TRONG REVIT (CHỈ ĐỌC): "+
                (cover==null || cover.CoverByFaceMm.Count==0?"Chưa đọc được":
                string.Join(" • ",cover.CoverByFaceMm.Select(p=>
                    p.Key+" = "+p.Value.ToString("0.#",CultureInfo.InvariantCulture)+" mm"))),
                11,Navy,false,new Thickness(0,0,0,8)));
                        left.Children.Add(Label("Module",12,Navy,true,new Thickness(0,0,0,5)));
            module=new ComboBox{Height=36,FontSize=13,Margin=new Thickness(0,0,0,17)};
            module.Items.Add("Dầm / Beam");module.Items.Add("Cột / Column");
            module.Items.Add("Móng / Foundation");
            module.SelectedIndex=hostModule.HasValue?(int)hostModule.Value:0;
            // A selected Host fixes the structural module; avoid accidental cross-host settings.
            module.IsEnabled=!hostModule.HasValue;
            module.ToolTip=hostModule.HasValue
                ?"Module được khóa theo Category của Host đã chọn trong Revit."
                :"Chọn cấu kiện trong Revit để tự động xác định Module.";
            module.SelectionChanged+=(sender,e)=>Validate();
            left.Children.Add(module);
            left.Children.Add(Label("02  /  CẤU HÌNH SƠ BỘ (mm)",15,Navy,true,new Thickness(0,0,0,11)));
            left.Children.Add(Label("Giá trị khởi tạo chỉ là ví dụ, KHÔNG lấy từ kích thước Host thực tế.",
                12,ColorBrush(164,81,30),false,new Thickness(0,0,0,10)));
            AddField(left,"width","Chiều rộng cấu kiện","200");
            AddField(left,"height","Chiều cao cấu kiện","350");
            AddField(left,"cover","Lớp bảo vệ nhập","25");
            AddField(left,"main","Đường kính thép chủ","16");
            AddField(left,"tie","Đường kính thép đai","8");
            AddField(left,"spacing","Khoảng cách đai nhập","150");
            AddField(left,"count","Số thanh chủ","4");
            left.Children.Add(Label("Đường kính có trong Revit: "+
                (types.Length>0?string.Join(" · ",types.Select(d=>d.ToString("0.#",CultureInfo.InvariantCulture))):"Không có RebarBarType"),
                11,Muted,false,new Thickness(0,8,0,0)));

            var right=new StackPanel{Margin=new Thickness(12,0,0,0)};
            var rightScroll=new ScrollViewer{
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=right};
            columns.Children.Add(rightScroll);Grid.SetColumn(rightScroll,1);
            right.Children.Add(Label("03  /  KIỂM TRA VÀ XEM TRƯỚC",15,Navy,true,new Thickness(0,0,0,12)));
            var previewPanel=new StackPanel{Background=Brushes.White,Margin=new Thickness(0,0,0,12)};
            shapePreview=new Border{Margin=new Thickness(10),
                BorderThickness=new Thickness(1),BorderBrush=ColorBrush(203,216,222),
                Background=Brushes.White,CornerRadius=new CornerRadius(5),
                HorizontalAlignment=HorizontalAlignment.Stretch};
            sectionCanvas=new Canvas{Width=310,Height=244,
                HorizontalAlignment=HorizontalAlignment.Center};
            shapePreview.Child=sectionCanvas;
            previewPanel.Children.Add(shapePreview);right.Children.Add(previewPanel);
            status=Label("",13,Accent,true,new Thickness(0,0,0,8));right.Children.Add(status);
            result=Label("",12,Navy,false,new Thickness(0,0,0,0));
            result.TextWrapping=TextWrapping.Wrap;
            right.Children.Add(result);
            right.Children.Add(Label("Chú ý: Chưa kiểm chứng Solid, lớp bảo vệ thực, nút khung, neo và nối chồng. "+
                "Tạo/cập nhật Rebar V5 đầy đủ vẫn khóa. Pilot thử 4 thanh dọc + đai, V4 không đổi.",
                12,ColorBrush(164,81,30),false,new Thickness(0,22,0,0)));
            var gateReview=V5ExecutionGatePolicy.Review(new V5ExecutionEvidence());
            right.Children.Add(Label("04  /  ĐIỀU KIỆN CHƯA ĐẠT",15,Navy,true,
                new Thickness(0,18,0,8)));
            right.Children.Add(Label(string.Join("\n",gateReview.Blockers.Select(x=>"• "+x)),
                12,ColorBrush(164,81,30),false,new Thickness(0,0,0,14)));

            var bottom=new Grid{Background=Brushes.White,Height=65};
            bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            Grid.SetRow(bottom,2);root.Children.Add(bottom);
            var footer=Label("V5 • Xem trước chỉ đọc; Pilot tạo thép trên BẢN SAO RVT",
                11,Muted,false,new Thickness(26,23,0,0));
            bottom.Children.Add(footer);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,13,24,0)};
            var check=Button("Kiểm tra thông số",Accent,Brushes.White);
            check.Click+=(sender,e)=>Validate();
            var close=Button("Đóng",ColorBrush(225,231,235),Navy);
            close.Click+=(sender,e)=>Close();
            var pilot=Button("PILOT: thép cột 3D",ColorBrush(174,91,48),Brushes.White);
            pilot.MinWidth=174;
            pilot.IsEnabled=V5PilotReleaseGate.Enabled;
            pilot.ToolTip="Đang khóa: Pilot V5 chưa nghiệm thu Revit. Dùng lệnh vẽ thép V4 trong Ribbon.";
            pilot.Click+=(sender,e)=>
            {
                Validate();
                if(hostModule!=V5StructuralModule.Column || PilotInput==null ||
                   !V5RebarInputPolicy.Review(PilotInput,types).IsValid ||
                   PilotInput.MainBarCount!=4)
                {
                    MessageBox.Show(this,
                        "Pilot chỉ dùng cho cột đã chọn, đúng 4 thanh, mọi thông số nhập hợp lệ.",
                        "KStruct V5 PILOT",MessageBoxButton.OK,MessageBoxImage.Warning);
                    return;
                }
                RequestPilotCreation=true;
                Close();
            };
            buttons.Children.Add(pilot);
            buttons.Children.Add(check);buttons.Children.Add(close);
            Grid.SetColumn(buttons,1);bottom.Children.Add(buttons);
            Validate();
        }

        private void AddField(Panel panel,string key,string name,string value)
        {
            var grid=new Grid{Margin=new Thickness(0,0,0,9)};
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(118)});
            var label=Label(name,12,Navy,false,new Thickness(0,10,8,0));
            grid.Children.Add(label);
            var box=new TextBox{Text=value,Height=35,FontSize=13,Padding=new Thickness(8,6,8,4),
                BorderBrush=ColorBrush(203,216,222),BorderThickness=new Thickness(1)};
            fields[key]=box;
            box.TextChanged+=(sender,e)=>Validate();
            Grid.SetColumn(box,1);grid.Children.Add(box);panel.Children.Add(grid);
        }
        private void Validate()
        {
            if(module==null||result==null||status==null||fields.Count<7)return;
            double number(string key)
            {
                if(V5RebarInputPolicy.TryMillimeters(fields[key].Text,out double n))return n;
                return double.NaN;
            }
            double countValue=number("count");
            int count=AuditV2Policy.IsFinite(countValue)&&countValue>=int.MinValue&&countValue<=int.MaxValue &&
                Math.Truncate(countValue)==countValue?(int)countValue:0;
            var input=new V5RebarInput{
                Module=(V5StructuralModule)module.SelectedIndex,HostUniqueId=hostId,
                HostModule=hostModule,
                WidthMm=number("width"),HeightMm=number("height"),CoverMm=number("cover"),
                MainBarDiameterMm=number("main"),TieDiameterMm=number("tie"),
                TieSpacingMm=number("spacing"),MainBarCount=count
            };
            PilotInput=input;
            var review=V5RebarInputPolicy.Review(input,types);
            status.Text=review.IsValid?"Dữ liệu nhập hợp lệ (chưa cho phép tạo thép)":
                "Cần điều chỉnh: "+review.Errors.Count+" vấn đề";
            status.Foreground=review.IsValid?Accent:ColorBrush(170,69,45);
            var messages=new List<string>();
            if(review.Errors.Count>0)messages.AddRange(review.Errors.Select(x=>"• "+x));
            if(AuditV2Policy.IsFinite(review.InnerWidthMm)&&review.InnerWidthMm>0)
                messages.Add("Khoảng tim thanh khả dụng theo bề rộng (sơ bộ): "+
                    review.InnerWidthMm.ToString("0.#",CultureInfo.InvariantCulture)+" mm.");
            if(AuditV2Policy.IsFinite(review.InnerHeightMm)&&review.InnerHeightMm>0)
                messages.Add("Khoảng tim thanh khả dụng theo chiều cao (sơ bộ): "+
                    review.InnerHeightMm.ToString("0.#",CultureInfo.InvariantCulture)+" mm.");
            messages.AddRange(review.Warnings.Select(x=>"• "+x));
            result.Text=string.Join("\n\n",messages);
            DrawSectionPreview(input,review);
        }

        // Diagram only: scaled to available viewport, never based on certified
        // Revit Solid geometry or a construction-approved bar arrangement.
        private void DrawSectionPreview(V5RebarInput input,V5InputReview review)
        {
            sectionCanvas.Children.Clear();
            const double viewportX=28,viewportY=26,viewportW=254,viewportH=174;
            AddCanvasText("MINH HỌA TIẾT DIỆN • KHÔNG PHẢI THÉP THỰC",9,Muted,12,5);
            if(!AuditV2Policy.IsFinite(input.WidthMm) ||
               !AuditV2Policy.IsFinite(input.HeightMm) ||
               input.WidthMm<=0 || input.HeightMm<=0)
            {
                AddCanvasText("Nhập chiều rộng và chiều cao hợp lệ.",12,Muted,25,90);
                return;
            }
            double scale=Math.Min(viewportW/input.WidthMm,viewportH/input.HeightMm);
            double w=Math.Max(1,input.WidthMm*scale),h=Math.Max(1,input.HeightMm*scale);
            double x=viewportX+(viewportW-w)/2,y=viewportY+(viewportH-h)/2;
            var concrete=new Shapes.Rectangle{Width=w,Height=h,
                Stroke=Accent,StrokeThickness=2,Fill=ColorBrush(232,241,244)};
            Canvas.SetLeft(concrete,x);Canvas.SetTop(concrete,y);
            sectionCanvas.Children.Add(concrete);
            double offset=(input.CoverMm+input.TieDiameterMm/2)*scale;
            if(AuditV2Policy.IsFinite(offset) && offset>1 && offset*2<Math.Min(w,h)-4)
            {
                var tie=new Shapes.Rectangle{Width=w-2*offset,Height=h-2*offset,
                    Stroke=ColorBrush(183,107,65),StrokeThickness=2,
                    Fill=Brushes.Transparent};
                Canvas.SetLeft(tie,x+offset);Canvas.SetTop(tie,y+offset);
                sectionCanvas.Children.Add(tie);
                // Four corner dots are symbolic, independent of entered bar count.
                double barOffset=(input.CoverMm+input.TieDiameterMm+
                    input.MainBarDiameterMm/2)*scale;
                if(AuditV2Policy.IsFinite(barOffset) && barOffset>2 &&
                    barOffset*2<Math.Min(w,h))
                {
                    foreach(double px in new[]{x+barOffset,x+w-barOffset})
                        foreach(double py in new[]{y+barOffset,y+h-barOffset})
                        {
                            var dot=new Shapes.Ellipse{Width=7,Height=7,
                                Fill=Navy,Stroke=Brushes.White,StrokeThickness=0.5};
                            Canvas.SetLeft(dot,px-3.5);Canvas.SetTop(dot,py-3.5);
                            sectionCanvas.Children.Add(dot);
                        }
                }
            }
            AddCanvasText("B = "+input.WidthMm.ToString("0.#",CultureInfo.InvariantCulture)+
                " mm",11,Navy,15,207);
            AddCanvasText("H = "+input.HeightMm.ToString("0.#",CultureInfo.InvariantCulture)+
                " mm",11,Navy,174,207);
            AddCanvasText("4 chấm thép tượng trưng; không thể hiện số lượng thanh thực.",
                9,Muted,12,226);
        }

        private void AddCanvasText(string value,double size,Brush color,double x,double y)
        {
            var t=new TextBlock{Text=value,FontSize=size,Foreground=color,
                TextWrapping=TextWrapping.NoWrap};
            Canvas.SetLeft(t,x);Canvas.SetTop(t,y);
            sectionCanvas.Children.Add(t);
        }
        private static string SafeName(Element e){try{return e.Name;}catch{return "unavailable";}}
        private static SolidColorBrush ColorBrush(byte r,byte g,byte b)=>
            new SolidColorBrush(Color.FromRgb(r,g,b));
        private static TextBlock Label(string text,double size,Brush color,bool bold,Thickness margin)=>
            new TextBlock{Text=text,FontSize=size,Foreground=color,Margin=margin,
                TextWrapping=TextWrapping.Wrap,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal};
        private static Button Button(string text,Brush background,Brush foreground)=>
            new Button{Content=text,MinWidth=112,Height=38,Margin=new Thickness(6,0,0,0),
                Padding=new Thickness(12,0,12,0),Background=background,Foreground=foreground,
                FontSize=12,BorderThickness=new Thickness(0),Cursor=System.Windows.Input.Cursors.Hand};
    }
}
