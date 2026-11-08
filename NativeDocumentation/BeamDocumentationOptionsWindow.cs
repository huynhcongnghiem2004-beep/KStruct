using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Serialization;
using Autodesk.Revit.DB;
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;
using Path = System.IO.Path;
using Rectangle = System.Windows.Shapes.Rectangle;
using Line = System.Windows.Shapes.Line;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal sealed class BeamDocumentationOptions
    {
        public ViewFamilyType SectionType { get; set; }
        public FamilySymbol TitleBlock { get; set; }
        public bool CreateLongitudinal { get; set; }
        public bool CreateCross { get; set; }
        public bool AddDimensions { get; set; }
        public bool CreateCrossForMany { get; set; }
        public bool Create3D { get; set; }
        public ElementId ExistingSheetId { get; set; }
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public double ElevationOffsetMm { get; set; }
    }

    internal static class BeamDocumentationOptionsWindow
    {
        private sealed class TypeChoice
        {
            public Element Value { get; private set; }
            public string Label { get; private set; }
            public TypeChoice(Element value, string label) { Value=value; Label=label; }
        }

        public sealed class Template
        {
            public string Name { get; set; }
            public string SheetNumber { get; set; }
            public string SheetName { get; set; }
            public string TitleBlockName { get; set; }
            public string SectionTypeName { get; set; }
            public bool CreateLongitudinal { get; set; }
            public bool CreateCross { get; set; }
            public bool AddDimensions { get; set; }
            public double ElevationOffsetMm { get; set; }
        }

        private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(39,99,245));
        private static readonly Brush Teal = new SolidColorBrush(Color.FromRgb(0,128,117));
        private static readonly Brush Border = new SolidColorBrush(Color.FromRgb(214,223,235));

        internal static BeamDocumentationOptions Show(IntPtr owner, IList<ViewFamilyType> sectionTypes, IList<FamilySymbol> titleBlocks, IList<ViewSheet> existingSheets)
        {
            if (sectionTypes == null || sectionTypes.Count == 0) throw new InvalidOperationException("Project chưa có Section View Family Type.");
            if (titleBlocks == null || titleBlocks.Count == 0) throw new InvalidOperationException("Project chưa nạp Title Block Family Type.");

            var window = new Window { Title="Bản vẽ dầm", Width=1500, Height=850, MinWidth=1120, MinHeight=680,
                ResizeMode=ResizeMode.CanResizeWithGrip, WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Background=new SolidColorBrush(Color.FromRgb(244,246,249)), ShowInTaskbar=false };
            var views = sectionTypes.Select(x=>new TypeChoice(x,x.Name)).ToList();
            var blocks = titleBlocks.Select(x=>new TypeChoice(x,x.FamilyName+": "+x.Name)).ToList();
            var cross = new CheckBox { Content="Mặt cắt ngang dầm", IsChecked=true, Margin=new Thickness(3,5,3,5) };
            var longitudinal = new CheckBox { Content="Mặt cắt dọc dầm", IsChecked=true, Margin=new Thickness(3,5,3,5) };
            var dimensions = new CheckBox { Content="Ghi kích thước", IsChecked=true, Margin=new Thickness(3,5,3,5) };
            var multipleCross = new CheckBox { Content="Mặt cắt ngang cho nhiều dầm", Margin=new Thickness(3,5,3,5) };
            var create3D = new CheckBox { Content="Tạo view 3D", Margin=new Thickness(3,5,3,5), IsEnabled=false, ToolTip="Tạo view 3D sẽ được bổ sung trong bản cập nhật tiếp theo." };
            var titleBlock = new ComboBox { ItemsSource=blocks, DisplayMemberPath="Label", SelectedIndex=0, Height=30, Margin=new Thickness(3,4,3,8) };
            var sectionType = new ComboBox { ItemsSource=views, DisplayMemberPath="Label", SelectedIndex=0, Height=30, Margin=new Thickness(3,4,3,8) };
            var sheetNumber = new TextBox { Text="COVER PAGE", Height=29, Margin=new Thickness(3,4,3,8), Padding=new Thickness(7,4,7,4) };
            var sheetName = new TextBox { Text="COVER PAGE", Height=29, Margin=new Thickness(3,4,3,8), Padding=new Thickness(7,4,7,4) };
            var offset = new TextBox { Text="0", Height=29, Margin=new Thickness(3,4,3,8), Padding=new Thickness(7,4,7,4) };
            ElementId existingSheetId = null;
            var existingSheetButton = Button("Chọn bản vẽ hiện có",290);
            existingSheetButton.Click += (s,e) =>
            {
                var choices = (existingSheets ?? new List<ViewSheet>()).Select(x=>new TypeChoice(x,x.SheetNumber+"  ·  "+x.Name)).ToList();
                if (choices.Count==0) { MessageBox.Show(window,"Project chưa có bản vẽ để chọn.","KStruct - Bản vẽ dầm",MessageBoxButton.OK,MessageBoxImage.Information); return; }
                var picker=new Window{Title="Chọn bản vẽ hiện có",Owner=window,Width=440,Height=520,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White};
                var list=new ListBox{ItemsSource=choices,DisplayMemberPath="Label",SelectedIndex=0,Margin=new Thickness(10)};
                var done=Button("Dùng bản vẽ này",150); var content=new DockPanel{Margin=new Thickness(6)}; DockPanel.SetDock(done,Dock.Bottom); content.Children.Add(done); content.Children.Add(list); picker.Content=content;
                done.Click+=(sender,args)=>{if(list.SelectedItem is TypeChoice chosen){existingSheetId=chosen.Value.Id;existingSheetButton.Content="Đang chọn: "+chosen.Label;picker.DialogResult=true;}};
                picker.ShowDialog();
            };
            var preview = new Canvas { Background=Brushes.White, MinHeight=480, ClipToBounds=true };
            var tabControl = new TabControl { Margin=new Thickness(0,10,8,0), Background=Brushes.White };
            var drawingTab = new TabItem { Header="Drawing", Content=MakeDrawingTab(cross,longitudinal,titleBlock,sheetNumber,sheetName,sectionType,offset,dimensions,multipleCross,create3D,existingSheetButton) };
            var detailTab = new TabItem { Header="Detail", Content=MakeInfoPage("Thiết lập chi tiết dầm", "Chọn tạo mặt cắt dọc để thể hiện chiều dài, vị trí gối và thép dọc. KStruct tạo view dựa trên hình học dầm đang chọn.") };
            var sectionTab = new TabItem { Header="Section", Content=MakeInfoPage("Thiết lập mặt cắt", "Chọn tạo mặt cắt ngang để thể hiện hình học tiết diện và cốt thép trong model. Kích thước dầm lấy từ cấu kiện Revit.") };
            var templatesTab = new TabItem { Header="Mẫu đã lưu", Content=MakeTemplateTab(window, titleBlocks, sectionTypes, titleBlock, sectionType, cross, longitudinal, dimensions, sheetNumber, sheetName, offset) };
            tabControl.Items.Add(drawingTab); tabControl.Items.Add(detailTab); tabControl.Items.Add(sectionTab); tabControl.Items.Add(templatesTab);

            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(340)}); body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            Grid.SetColumn(tabControl,0); body.Children.Add(tabControl);
            var previewCard = new Border{Background=Brushes.White,BorderBrush=Border,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Padding=new Thickness(14),Margin=new Thickness(8,10,10,0)};
            var previewGrid = new Grid(); previewGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto}); previewGrid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)}); previewGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var previewHeader = new DockPanel{Margin=new Thickness(2,2,2,10)}; previewHeader.Children.Add(new TextBlock{Text="XEM TRƯỚC BỐ TRÍ",Foreground=Teal,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center}); var zoom=Button("Phóng to bản vẽ",120); DockPanel.SetDock(zoom,Dock.Right); previewHeader.Children.Add(zoom); previewGrid.Children.Add(previewHeader);
            Grid.SetRow(preview,1); previewGrid.Children.Add(preview); var footnote=new TextBlock{Text="Sơ đồ minh họa; không theo tỷ lệ. Family tag và hình học thực tế được lấy từ model.",Foreground=new SolidColorBrush(Color.FromRgb(89,116,164)),Margin=new Thickness(2,10,2,2)}; Grid.SetRow(footnote,2); previewGrid.Children.Add(footnote); previewCard.Child=previewGrid; Grid.SetColumn(previewCard,1); body.Children.Add(previewCard);

            var root = new DockPanel { Margin=new Thickness(8) };
            var header = new StackPanel{Margin=new Thickness(10,5,10,7)}; header.Children.Add(new TextBlock{Text="Bản vẽ dầm",FontSize=22,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(39,61,99))}); header.Children.Add(new TextBlock{Text="Cấu hình bản vẽ • Xem trước trực quan",Foreground=new SolidColorBrush(Color.FromRgb(89,116,164)),Margin=new Thickness(0,3,0,0)}); header.Children.Add(new TextBlock{Text="BS-A1-25-BEAM-DX",Foreground=Teal,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,0)}); DockPanel.SetDock(header,Dock.Top); root.Children.Add(header);
            var footer = new DockPanel{Margin=new Thickness(8,10,8,3),LastChildFill=false}; var brand=new TextBlock{Text="KSTRUCT · Beam documentation",Foreground=new SolidColorBrush(Color.FromRgb(89,116,164)),VerticalAlignment=VerticalAlignment.Center}; DockPanel.SetDock(brand,Dock.Left); footer.Children.Add(brand); var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right}; var cancel=Button("Đóng",70); var apply=Button("Áp dụng",92); apply.Background=Teal; apply.Foreground=Brushes.White; buttons.Children.Add(cancel); buttons.Children.Add(apply); DockPanel.SetDock(buttons,Dock.Right); footer.Children.Add(buttons); DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer); root.Children.Add(body);
            window.Content=root; new WindowInteropHelper(window).Owner=owner;
            Action draw=()=>DrawPreview(preview, titleBlock.SelectedItem as TypeChoice, sheetNumber.Text, sheetName.Text, cross.IsChecked==true, longitudinal.IsChecked==true);
            window.Loaded+=(s,e)=>draw(); window.SizeChanged+=(s,e)=>draw(); titleBlock.SelectionChanged+=(s,e)=>draw(); sheetNumber.TextChanged+=(s,e)=>draw(); sheetName.TextChanged+=(s,e)=>draw(); cross.Checked+=(s,e)=>draw(); cross.Unchecked+=(s,e)=>draw(); longitudinal.Checked+=(s,e)=>draw(); longitudinal.Unchecked+=(s,e)=>draw();
            cancel.Click+=(s,e)=>window.DialogResult=false;
            BeamDocumentationOptions result=null;
            apply.Click+=(s,e)=>
            {
                double mm;
                if (cross.IsChecked!=true && longitudinal.IsChecked!=true) { MessageBox.Show(window,"Chọn ít nhất một loại mặt cắt.","KStruct - Bản vẽ dầm",MessageBoxButton.OK,MessageBoxImage.Warning); return; }
                if (!double.TryParse(offset.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out mm) && !double.TryParse(offset.Text,out mm)) { MessageBox.Show(window,"Cao độ cách dầm phải là số.","KStruct - Bản vẽ dầm",MessageBoxButton.OK,MessageBoxImage.Warning); return; }
                result=new BeamDocumentationOptions { SectionType=(sectionType.SelectedItem as TypeChoice)?.Value as ViewFamilyType, TitleBlock=(titleBlock.SelectedItem as TypeChoice)?.Value as FamilySymbol,
                    CreateCross=cross.IsChecked==true, CreateLongitudinal=longitudinal.IsChecked==true, AddDimensions=dimensions.IsChecked==true, CreateCrossForMany=multipleCross.IsChecked==true, Create3D=create3D.IsChecked==true,
                    ExistingSheetId=existingSheetId,
                    SheetNumber=sheetNumber.Text.Trim(), SheetName=sheetName.Text.Trim(), ElevationOffsetMm=mm };
                if (result.SectionType==null || result.TitleBlock==null || string.IsNullOrWhiteSpace(result.SheetNumber) || string.IsNullOrWhiteSpace(result.SheetName)) { MessageBox.Show(window,"Chọn khung tên và kiểu mặt cắt, đồng thời nhập số/tên bản vẽ.","KStruct - Bản vẽ dầm",MessageBoxButton.OK,MessageBoxImage.Warning); return; }
                window.DialogResult=true;
            };
            return window.ShowDialog()==true ? result : null;
        }

        private static UIElement MakeDrawingTab(CheckBox cross, CheckBox longitudinal, ComboBox titleBlock, TextBox number, TextBox name, ComboBox viewType, TextBox offset, CheckBox dimensions, CheckBox many, CheckBox threeD, Button existingSheetButton)
        {
            var panel=new StackPanel{Margin=new Thickness(12)}; panel.Children.Add(cross); panel.Children.Add(longitudinal); panel.Children.Add(Label("Khung tên")); panel.Children.Add(titleBlock); panel.Children.Add(Label("Số bản vẽ")); panel.Children.Add(number); panel.Children.Add(Label("Tên bản vẽ")); panel.Children.Add(name); panel.Children.Add(Label("Kiểu cao độ")); panel.Children.Add(viewType); panel.Children.Add(Label("Cao độ cách dầm (mm)")); panel.Children.Add(offset); panel.Children.Add(dimensions); panel.Children.Add(many); panel.Children.Add(new CheckBox{Content="Pick gối tựa uốn dim",Margin=new Thickness(3,5,3,5),IsEnabled=false}); panel.Children.Add(threeD); panel.Children.Add(existingSheetButton); return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        }
        private static UIElement MakeInfoPage(string heading,string text)=>new StackPanel{Margin=new Thickness(14),Children={new TextBlock{Text=heading,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new Thickness(2,2,2,12)},new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray}}};
        private static UIElement MakeTemplateTab(Window owner,IList<FamilySymbol> blocks,IList<ViewFamilyType> types,ComboBox block,ComboBox view,CheckBox cross,CheckBox longitudinal,CheckBox dimensions,TextBox number,TextBox name,TextBox offset)
        {
            var panel=new StackPanel{Margin=new Thickness(12)}; panel.Children.Add(Label("Tên mẫu")); var templateName=new TextBox{Height=28,Margin=new Thickness(3)}; panel.Children.Add(templateName); var list=new ListBox{Height=300,Margin=new Thickness(3)}; panel.Children.Add(list); var save=Button("Lưu mẫu hiện tại",170); panel.Children.Add(save);
            void Reload(){list.Items.Clear();foreach(var x in ReadTemplates())list.Items.Add(x.Name);}
            save.Click+=(s,e)=>{if(string.IsNullOrWhiteSpace(templateName.Text)){MessageBox.Show(owner,"Nhập tên mẫu trước khi lưu.");return;}var c=new Template{Name=templateName.Text.Trim(),SheetNumber=number.Text,SheetName=name.Text,TitleBlockName=(block.SelectedItem as TypeChoice)?.Label,SectionTypeName=(view.SelectedItem as TypeChoice)?.Label,CreateCross=cross.IsChecked==true,CreateLongitudinal=longitudinal.IsChecked==true,AddDimensions=dimensions.IsChecked==true,ElevationOffsetMm=double.TryParse(offset.Text,out double d)?d:0};var all=ReadTemplates();all.RemoveAll(x=>x.Name==c.Name);all.Add(c);WriteTemplates(all);Reload();};
            list.SelectionChanged+=(s,e)=>{Template c=ReadTemplates().FirstOrDefault(x=>x.Name==(list.SelectedItem as string));if(c==null)return;number.Text=c.SheetNumber;name.Text=c.SheetName;cross.IsChecked=c.CreateCross;longitudinal.IsChecked=c.CreateLongitudinal;dimensions.IsChecked=c.AddDimensions;offset.Text=c.ElevationOffsetMm.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);block.SelectedIndex=Math.Max(0,blocks.ToList().FindIndex(x=>x.FamilyName+": "+x.Name==c.TitleBlockName));view.SelectedIndex=Math.Max(0,types.ToList().FindIndex(x=>x.Name==c.SectionTypeName));};
            Reload(); return new StackPanel{Children={panel}};
        }
        private static List<Template> ReadTemplates(){try{string p=TemplatePath();if(!File.Exists(p))return new List<Template>();using(var f=File.OpenRead(p))return ((TemplateList)new XmlSerializer(typeof(TemplateList)).Deserialize(f)).Items??new List<Template>();}catch{return new List<Template>();}}
        private static void WriteTemplates(List<Template> templates){string p=TemplatePath();Directory.CreateDirectory(Path.GetDirectoryName(p));using(var f=File.Create(p))new XmlSerializer(typeof(TemplateList)).Serialize(f,new TemplateList{Items=templates});}
        [XmlRoot("BeamDocumentationTemplates")] public sealed class TemplateList{[XmlElement("Template")]public List<Template> Items{get;set;}=new List<Template>();}
        private static string TemplatePath()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KStruct","BeamDocumentationTemplates.xml");
        private static TextBlock Label(string text)=>new TextBlock{Text=text,Margin=new Thickness(3,2,3,0),Foreground=new SolidColorBrush(Color.FromRgb(56,75,109))};
        private static Button Button(string text,double width)=>new Button{Content=text,Width=width,Height=30,Margin=new Thickness(3),Padding=new Thickness(6,3,6,3),Background=Brushes.White,BorderBrush=Border};
        private static void DrawPreview(Canvas canvas,TypeChoice block,string number,string name,bool createCross,bool createLong)
        {
            canvas.Children.Clear();double w=Math.Max(400,canvas.ActualWidth),h=Math.Max(300,canvas.ActualHeight);double pad=24,sw=w-pad*2,sh=h-pad*2;var sheet=new Rectangle{Width=sw,Height=sh,Stroke=new SolidColorBrush(Color.FromRgb(119,140,170)),StrokeThickness=1,Fill=Brushes.White};Canvas.SetLeft(sheet,pad);Canvas.SetTop(sheet,pad);canvas.Children.Add(sheet);
            var title=new TextBlock{Text="DẦM D1.Y5A-1  (SL: 02; L=6.800)",Foreground=Brushes.Black,FontSize=11};Canvas.SetLeft(title,pad+sw*.40);Canvas.SetTop(title,pad+sh*.08);canvas.Children.Add(title);
            double left=pad+sw*.14,right=pad+sw*.86,y=pad+sh*.31;var body=new Rectangle{Width=right-left,Height=42,Stroke=Brushes.Gray,Fill=new SolidColorBrush(Color.FromRgb(250,250,250))};Canvas.SetLeft(body,left);Canvas.SetTop(body,y);canvas.Children.Add(body);
            for(int i=0;i<2;i++){double yy=y+8+i*26;var bar=new Line{X1=left+8,X2=right-8,Y1=yy,Y2=yy,Stroke=i==0?Blue:Teal,StrokeThickness=1};canvas.Children.Add(bar);}
            var dim=new Line{X1=left,X2=right,Y1=y+62,Y2=y+62,Stroke=Brushes.Gray};canvas.Children.Add(dim);var length=new TextBlock{Text="6800",FontSize=10};Canvas.SetLeft(length,(left+right)/2-14);Canvas.SetTop(length,y+64);canvas.Children.Add(length);
            if(createCross)for(int i=0;i<2;i++){double x=pad+sw*(.32+i*.36),cy=pad+sh*.72;var sec=new Rectangle{Width=42,Height=88,Stroke=Brushes.Gray,Fill=new SolidColorBrush(Color.FromRgb(250,250,250))};Canvas.SetLeft(sec,x);Canvas.SetTop(sec,cy);canvas.Children.Add(sec);var cap=new TextBlock{Text="MẶT CẮT 1-1",FontSize=9};Canvas.SetLeft(cap,x-8);Canvas.SetTop(cap,cy-14);canvas.Children.Add(cap);}
            if(!createCross&&!createLong){var empty=new TextBlock{Text="Chọn loại mặt cắt để xem trước.",Foreground=Brushes.SlateGray};Canvas.SetLeft(empty,pad+20);Canvas.SetTop(empty,pad+20);canvas.Children.Add(empty);}
            var bottom=new Rectangle{Width=sw,Height=25,Stroke=new SolidColorBrush(Color.FromRgb(119,140,170)),Fill=new SolidColorBrush(Color.FromRgb(250,251,253))};Canvas.SetLeft(bottom,pad);Canvas.SetTop(bottom,pad+sh-25);canvas.Children.Add(bottom);var footer=new TextBlock{Text=name+"                                       "+number,FontSize=10,Foreground=Teal};Canvas.SetLeft(footer,pad+10);Canvas.SetTop(footer,pad+sh-20);canvas.Children.Add(footer);
        }
    }
}
