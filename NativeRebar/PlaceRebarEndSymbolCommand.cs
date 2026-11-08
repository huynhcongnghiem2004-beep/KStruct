using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class PlaceRebarEndSymbolCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class DetailTypeChoice
        {
            public FamilySymbol Symbol { get; private set; }
            public string Display { get; private set; }

            public DetailTypeChoice(FamilySymbol symbol)
            {
                Symbol = symbol;
                Display = symbol == null ? "KStruct native (không cần family)" : symbol.Family.Name + " : " + symbol.Name;
            }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                if (ui.ActiveView is View3D || ui.ActiveView.ViewType == ViewType.DrawingSheet ||
                    ui.ActiveView.ViewType == ViewType.Undefined)
                    throw new InvalidOperationException("Chọn một mặt bằng, mặt cắt hoặc view chi tiết để đặt ký hiệu đầu thanh.");

                List<FamilySymbol> symbols = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DetailComponents)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .Where(x => x.Family != null && x.Family.FamilyPlacementType == FamilyPlacementType.ViewBased)
                    .OrderBy(x => x.Family.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                FamilySymbol symbol = ChooseSymbol(data.Application.MainWindowHandle, symbols);
                if (symbol == null) return Result.Cancelled;

                int placed = 0, skipped = 0;
                var skipReasons = new List<string>();
                while (true)
                {
                    Reference picked;
                    try
                    {
                        picked = ui.Selection.PickObject(ObjectType.Element, new RebarFilter(),
                            "Chọn Rebar gần đầu cần đặt ký hiệu (nhấn Esc để kết thúc)");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; }

                    Rebar rebar = doc.GetElement(picked) as Rebar;
                    if (rebar == null) continue;
                    using (Transaction tx = new Transaction(doc, "KStruct - Ký hiệu đầu thép"))
                    {
                        tx.Start();
                        try
                        {
                            if (PlaceAtNearestOpenEnd(doc, ui.ActiveView, rebar, picked.GlobalPoint, symbol))
                                placed++;
                            else
                            {
                                skipped++;
                                skipReasons.Add("Rebar " + rebar.Id.Value + ": đã có móc ở đầu được chọn hoặc hình học không phù hợp.");
                            }
                            tx.Commit();
                        }
                        catch (Exception ex)
                        {
                            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                            skipped++;
                            skipReasons.Add("Rebar " + rebar.Id.Value + ": " + ex.Message);
                        }
                    }
                }

                string report = "Đã đặt " + placed + " ký hiệu đầu thanh.";
                if (skipped > 0) report += "\nBỏ qua " + skipped + " Rebar:\n" + string.Join("\n", skipReasons.Take(8));
                TaskDialog.Show("KStruct - Ký hiệu đầu thép", report);
                return placed > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Ký hiệu đầu thép", ex.Message);
                return Result.Failed;
            }
        }

        private static bool PlaceAtNearestOpenEnd(Document doc, View view, Rebar rebar, XYZ pickedPoint, FamilySymbol symbol)
        {
            Parameter startHook = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);
            Parameter endHook = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
            bool hasStartHook = startHook != null && startHook.StorageType == StorageType.ElementId &&
                startHook.AsElementId() != ElementId.InvalidElementId;
            bool hasEndHook = endHook != null && endHook.StorageType == StorageType.ElementId &&
                endHook.AsElementId() != ElementId.InvalidElementId;
            if (hasStartHook && hasEndHook) return false;

            IList<Curve> curves = rebar.GetCenterlineCurves(false, false, true,
                MultiplanarOption.IncludeOnlyPlanarCurves, 0);
            if (curves == null || curves.Count == 0) return false;
            Line first = curves[0] as Line;
            Line last = curves[curves.Count - 1] as Line;
            XYZ start = curves[0].GetEndPoint(0);
            XYZ end = curves[curves.Count - 1].GetEndPoint(1);
            bool useStart = start.DistanceTo(pickedPoint) < end.DistanceTo(pickedPoint);
            if ((useStart && hasStartHook) || (!useStart && hasEndHook)) return false;
            Line endLine = useStart ? first : last;
            if (endLine == null) return false;

            XYZ point = useStart ? start : end;
            XYZ direction = useStart ? endLine.Direction : -endLine.Direction;
            if (symbol == null)
            {
                PlaceNativeSymbol(doc, view, rebar, point);
                return true;
            }
            if (!symbol.IsActive) symbol.Activate();
            FamilyInstance instance = doc.Create.NewFamilyInstance(point, symbol, view);
            if (instance == null) throw new InvalidOperationException("Revit không tạo được ký hiệu Detail Item.");

            double angle = direction.AngleTo(view.RightDirection);
            Line rotationAxis = Line.CreateUnbound(point, view.ViewDirection);
            ElementTransformUtils.RotateElement(doc, instance.Id, rotationAxis, angle);
            if (instance.CanFlipHand) instance.flipHand();
            ElementTransformUtils.MoveElement(doc, instance.Id,
                view.RightDirection.Negate() * UnitUtils.ConvertToInternalUnits(7.7, UnitTypeId.Millimeters));

            double diameter = ((RebarBarType)doc.GetElement(rebar.GetTypeId())).BarNominalDiameter;
            Parameter diameterParameter = instance.LookupParameter("DK");
            if (diameterParameter == null || diameterParameter.IsReadOnly)
                throw new InvalidOperationException("Family ký hiệu phải có tham số instance DK có thể ghi.");
            switch (diameterParameter.StorageType)
            {
                case StorageType.Double:
                    diameterParameter.Set(diameter);
                    break;
                case StorageType.String:
                    diameterParameter.Set(UnitUtils.ConvertFromInternalUnits(diameter, UnitTypeId.Millimeters)
                        .ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case StorageType.Integer:
                    diameterParameter.Set((int)Math.Round(UnitUtils.ConvertFromInternalUnits(diameter, UnitTypeId.Millimeters)));
                    break;
                default:
                    throw new InvalidOperationException("Tham số DK phải là Length, Number hoặc Text.");
            }
            return true;
        }

        private static void PlaceNativeSymbol(Document doc, View view, Rebar rebar, XYZ point)
        {
            RebarBarType barType = doc.GetElement(rebar.GetTypeId()) as RebarBarType;
            if (barType == null) throw new InvalidOperationException("Không đọc được RebarBarType để ghi đường kính.");
            double diameterMm = UnitUtils.ConvertFromInternalUnits(barType.BarNominalDiameter, UnitTypeId.Millimeters);
            double scale = Math.Max(1, view.Scale);
            double radius = UnitUtils.ConvertToInternalUnits(1.5 * scale, UnitTypeId.Millimeters);
            double offset = UnitUtils.ConvertToInternalUnits(7.7 * scale, UnitTypeId.Millimeters);
            double textGap = UnitUtils.ConvertToInternalUnits(1.5 * scale, UnitTypeId.Millimeters);
            double depth = (view.Origin - point).DotProduct(view.ViewDirection);
            XYZ projected = point + view.ViewDirection * depth;
            XYZ center = projected - view.RightDirection * offset;

            FilledRegionType solidType = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>().FirstOrDefault(type =>
                {
                    FillPatternElement pattern = doc.GetElement(type.ForegroundPatternId) as FillPatternElement;
                    return pattern != null && pattern.GetFillPattern().IsSolidFill;
                });
            bool filled = false;
            if (solidType != null)
            {
                using (SubTransaction fillTransaction = new SubTransaction(doc))
                {
                    fillTransaction.Start();
                    try
                    {
                        var loop = new CurveLoop();
                        loop.Append(Arc.Create(center, radius, 0, Math.PI, view.RightDirection, view.UpDirection));
                        loop.Append(Arc.Create(center, radius, Math.PI, 2 * Math.PI, view.RightDirection, view.UpDirection));
                        FilledRegion.Create(doc, solidType.Id, view.Id, new List<CurveLoop> { loop });
                        filled = fillTransaction.Commit() == TransactionStatus.Committed;
                    }
                    catch
                    {
                        if (fillTransaction.GetStatus() == TransactionStatus.Started) fillTransaction.RollBack();
                        filled = false;
                    }
                }
            }
            if (!filled)
            {
                doc.Create.NewDetailCurve(view, Arc.Create(center, radius, 0, Math.PI, view.RightDirection, view.UpDirection));
                doc.Create.NewDetailCurve(view, Arc.Create(center, radius, Math.PI, 2 * Math.PI, view.RightDirection, view.UpDirection));
            }

            TextNoteType textType = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
            if (textType == null) throw new InvalidOperationException("Project không có TextNoteType để ghi đường kính.");
            var textOptions = new TextNoteOptions(textType.Id)
            {
                HorizontalAlignment = HorizontalTextAlignment.Left,
                VerticalAlignment = VerticalTextAlignment.Middle
            };
            XYZ labelPoint = center + view.RightDirection * (radius + textGap) - view.UpDirection * radius;
            TextNote.Create(doc, view.Id, labelPoint, "Ø" + diameterMm.ToString("0.#", CultureInfo.CurrentCulture), textOptions);
        }

        private static FamilySymbol ChooseSymbol(IntPtr owner, IList<FamilySymbol> symbols)
        {
            var choices = new List<DetailTypeChoice> { new DetailTypeChoice(null) };
            choices.AddRange(symbols.Select(x => new DetailTypeChoice(x)));
            var window = new Window
            {
                Title = "Chọn family ký hiệu đầu thanh - KStruct",
                Width = 500,
                Height = 150,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
                root.Children.Add(new TextBlock { Text = "Chọn ký hiệu KStruct native hoặc family Detail Item có tham số DK:", Margin = new Thickness(0, 0, 0, 5) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 460, MinHeight = 24, DisplayMemberPath = "Display", ItemsSource = choices, SelectedIndex = 0 };
            root.Children.Add(combo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Chọn", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) => { if (combo.SelectedItem != null) window.DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? ((DetailTypeChoice)combo.SelectedItem).Symbol : null;
        }
    }
}
