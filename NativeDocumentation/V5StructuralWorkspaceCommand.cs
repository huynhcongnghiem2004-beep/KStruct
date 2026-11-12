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
using Panel = System.Windows.Controls.Panel;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Grid = System.Windows.Controls.Grid;
using Color = System.Windows.Media.Color;
using SDK_HD.KStruct.Core.V5;

namespace SDK_HD.KStruct.NativeDocumentation
{
    /// <summary>WPF entry workspace for V5. Read-only: no transaction, no Rebar creation.</summary>
    [Transaction(TransactionMode.ReadOnly)]
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
                var inventory=V5HostSolidInspector.Inspect(selected);
                var cover=V5HostCoverInspector.Inspect(selected);
                var window=new V5StructuralWorkspaceWindow(doc.Title,selected,barDiameters,inventory,cover);
                new WindowInteropHelper(window).Owner=data.Application.MainWindowHandle;
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch(Exception ex)
            {
                message=ex.ToString();
                TaskDialog.Show("KStruct V5","Không mở được giao diện: "+ex.Message);
                return Result.Failed;
            }
        }
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
            header.Children.Add(Label("Bố trí kết cấu • Kiểm tra nhập liệu • Chế độ an toàn (chưa tạo thép)",
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
            shapePreview=new Border{Width=230,Height=155,Margin=new Thickness(10),
                BorderThickness=new Thickness(3),BorderBrush=Accent,
                Background=ColorBrush(226,237,239),CornerRadius=new CornerRadius(3),
                HorizontalAlignment=HorizontalAlignment.Center};
            var marker=new TextBlock{Text="TIẾT DIỆN MINH HỌA\nKhông phải hình học từ mô hình",
                TextAlignment=TextAlignment.Center,Foreground=Navy,FontSize=12,
                VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
            shapePreview.Child=marker;
            previewPanel.Children.Add(shapePreview);right.Children.Add(previewPanel);
            status=Label("",13,Accent,true,new Thickness(0,0,0,8));right.Children.Add(status);
            result=Label("",12,Navy,false,new Thickness(0,0,0,0));
            result.TextWrapping=TextWrapping.Wrap;
            right.Children.Add(result);
            right.Children.Add(Label("Chú ý: Chưa kiểm chứng Solid, lớp bảo vệ thực, nút khung, neo và nối chồng. "+
                "Tính năng tạo/cập nhật Rebar V5 đang khóa. V4 không bị thay đổi.",
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
            var footer=Label("KStruct V5 • Kiểm tra sơ bộ, không ghi dữ liệu vào RVT",
                11,Muted,false,new Thickness(26,23,0,0));
            bottom.Children.Add(footer);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,13,24,0)};
            var check=Button("Kiểm tra thông số",Accent,Brushes.White);
            check.Click+=(sender,e)=>Validate();
            var close=Button("Đóng",ColorBrush(225,231,235),Navy);
            close.Click+=(sender,e)=>Close();
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
            if(AuditV2Policy.IsFinite(input.WidthMm)&&AuditV2Policy.IsFinite(input.HeightMm)&&
                input.WidthMm>0&&input.HeightMm>0)
            {
                double ratio=input.WidthMm/input.HeightMm;
                shapePreview.Width=Math.Max(90,Math.Min(225,160*ratio));
                shapePreview.Height=Math.Max(90,Math.Min(155,160/Math.Max(ratio,0.5)));
            }
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
