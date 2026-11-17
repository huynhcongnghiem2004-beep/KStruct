using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class OptimizeRebarCuttingCommand : IExternalCommand
    {
        private sealed class Piece
        {
            public string Mark;
            public string HostMark;
            public string TypeName;
            public string Partition;
            public string ShapeName;
            public string Geometry;
            public string Kind;
            public string BlankMethod;
            public string Validation;
            public int Position;
            public double DiameterMm;
            public double LengthMm;
            public double CenterlineLengthMm;
            public double RevitLengthMm;
            public double DeltaMm;
        }

        private sealed class StockBar
        {
            public int Number;
            public List<Piece> Pieces = new List<Piece>();
            public double StockLengthMm;
            public double KerfMm;
            public double UsedMm;
            public string Source;
            public string Warehouse;
            public string Bin;
            public string InventoryItemId;
            public double OffcutMm => Math.Max(0, StockLengthMm - UsedMm);
        }

        private sealed class Settings
        {
            public double StockLengthMm;
            public double KerfMm;
            public double ReusableOffcutMm;
            public List<RebarStockInventoryItem> Inventory;
            public bool IncludeLongitudinal;
            public bool IncludeStirrups;
            public bool IncludeUnclassified;
            public bool UseCadLegsLength;
        }

        private sealed class ReviewResult
        {
            public string CsvPath;
            public bool IssueInventory;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> selected = ui.Selection.GetElementIds()
                    .Select(doc.GetElement).Where(e => e != null).ToList();
                List<Rebar> rebars = selected.OfType<Rebar>().Distinct().ToList();
                List<ElementId> beamIds = selected.OfType<FamilyInstance>()
                    .Where(e => e.Category != null &&
                        e.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming)
                    .Select(e => e.Id).ToList();
                if (rebars.Count == 0 && beamIds.Count > 0)
                {
                    HashSet<ElementId> hosts = new HashSet<ElementId>(beamIds);
                    rebars = new FilteredElementCollector(doc).OfClass(typeof(Rebar))
                        .WhereElementIsNotElementType().Cast<Rebar>()
                        .Where(r => hosts.Contains(r.GetHostId())).ToList();
                }
                if (rebars.Count == 0 && beamIds.Count == 0)
                    rebars = new FilteredElementCollector(doc, ui.ActiveView.Id)
                        .OfClass(typeof(Rebar)).WhereElementIsNotElementType().Cast<Rebar>().ToList();
                if (rebars.Count == 0)
                {
                    TaskDialog.Show("KStruct - Tối ưu cắt thép", "Không tìm thấy Rebar trong lựa chọn hoặc view hiện hành.");
                    return Result.Cancelled;
                }

                Settings settings = ShowSettings(data.Application.MainWindowHandle);
                if (settings == null) return Result.Cancelled;
                settings.Inventory = RebarStockInventoryStore.Load().Items.ToList();
                List<Piece> allPieces = ExpandPieces(doc, rebars, settings);
                List<Piece> filteredOut = allPieces.Where(piece =>
                    (piece.Kind == "Thép dọc" && !settings.IncludeLongitudinal) ||
                    (piece.Kind == "Đai" && !settings.IncludeStirrups) ||
                    (piece.Kind == "Chưa phân loại" && !settings.IncludeUnclassified)).ToList();
                List<Piece> pieces = allPieces.Except(filteredOut).ToList();
                if (pieces.Count == 0)
                {
                    string detail = filteredOut.Count > 0
                        ? "Toàn bộ " + filteredOut.Count + " vị trí Rebar bị loại bởi bộ lọc đang chọn. Bật lại nhóm phù hợp rồi chạy lại."
                        : "Không đọc được chiều dài và số lượng thanh Rebar.";
                    TaskDialog.Show("KStruct - BeamCut", detail);
                    return Result.Cancelled;
                }

                List<StockBar> plan = new List<StockBar>();
                List<Piece> overlength = new List<Piece>();
                var remainingInventory = settings.Inventory.GroupBy(item => item.Id)
                    .ToDictionary(group => group.Key, group => group.Sum(item => Math.Max(0, item.Quantity)));
                foreach (var group in pieces.GroupBy(p => new { p.TypeName, p.DiameterMm, p.Partition })
                    .OrderBy(g => g.Key.TypeName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(g => g.Key.DiameterMm)
                    .ThenBy(g => g.Key.Partition, StringComparer.CurrentCultureIgnoreCase))
                {
                    List<Piece> eligible = new List<Piece>();
                    List<RebarStockInventoryItem> availableStock = settings.Inventory.Where(item => item.Available && item.QcPassed &&
                        string.Equals(item.RebarTypeName, group.Key.TypeName, StringComparison.OrdinalIgnoreCase) &&
                        Math.Abs(item.DiameterMm - group.Key.DiameterMm) < 0.05 && item.Quantity > 0)
                        .Select(item => new RebarStockInventoryItem
                        {
                            Id = item.Id, RebarTypeName = item.RebarTypeName, DiameterMm = item.DiameterMm,
                            LengthMm = item.LengthMm, Quantity = remainingInventory.ContainsKey(item.Id) ? remainingInventory[item.Id] : 0,
                            Warehouse = item.Warehouse, Bin = item.Bin, Available = item.Available, QcPassed = item.QcPassed
                        }).Where(item => item.Quantity > 0).ToList();
                    double maxStockLength = Math.Max(settings.StockLengthMm,
                        availableStock.Select(item => item.LengthMm).DefaultIfEmpty(settings.StockLengthMm).Max());
                    foreach (Piece piece in group.OrderByDescending(p => p.LengthMm))
                    {
                        if (piece.LengthMm > maxStockLength + 1e-6)
                            overlength.Add(piece);
                        else
                            eligible.Add(piece);
                    }
                    List<StockBar> groupPlan = NestBestFitDecreasing(eligible, settings, availableStock);
                    int next = plan.Count + 1;
                    foreach (StockBar bar in groupPlan)
                    {
                        bar.Number = next++;
                        plan.Add(bar);
                    }
                    foreach (var used in groupPlan.Where(bar => bar.Source == "Kho").GroupBy(bar => bar.InventoryItemId))
                        if (remainingInventory.ContainsKey(used.Key)) remainingInventory[used.Key] -= used.Count();
                }

                List<Piece> shapeIssues = pieces.Where(piece => !string.IsNullOrWhiteSpace(piece.Validation)).ToList();
                ReviewResult review = ShowReview(data.Application.MainWindowHandle, pieces, plan, overlength, filteredOut, shapeIssues, settings);
                if (review == null) return Result.Cancelled;
                WriteCsv(review.CsvPath, plan, overlength, filteredOut, shapeIssues, settings);
                int issuedInventoryBars = 0;
                bool issued = false;
                if (review.IssueInventory)
                {
                    foreach (var used in plan.Where(bar => bar.Source == "Kho").GroupBy(bar => bar.InventoryItemId))
                    {
                        RebarStockInventoryItem item = settings.Inventory.FirstOrDefault(stock => stock.Id == used.Key);
                        if (item == null || item.Quantity < used.Count())
                            throw new InvalidOperationException("Tồn kho đã thay đổi; không thể trừ an toàn phôi " + used.Key + ". Kiểm tra file kế hoạch rồi đồng bộ kho thủ công.");
                        item.Quantity -= used.Count();
                        issuedInventoryBars += used.Count();
                    }
                    foreach (StockBar stock in plan.Where(bar => bar.OffcutMm >= settings.ReusableOffcutMm && bar.OffcutMm > 1e-6))
                    {
                        Piece sample = stock.Pieces.First();
                        settings.Inventory.Add(new RebarStockInventoryItem
                        {
                            RebarTypeName = sample.TypeName,
                            DiameterMm = sample.DiameterMm,
                            LengthMm = stock.OffcutMm,
                            Quantity = 1,
                            Warehouse = "Chờ QC",
                            Bin = "",
                            ReceiptReference = "Phần dư từ kế hoạch " + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture),
                            ReceivedAtUtc = DateTime.UtcNow,
                            Available = false,
                            QcPassed = false,
                            QcNote = "Chờ QC phần dư"
                        });
                    }
                    RebarStockInventoryStore.Save(new RebarStockInventoryData
                    {
                        Items = new System.Collections.ObjectModel.ObservableCollection<RebarStockInventoryItem>(settings.Inventory)
                    });
                    issued = true;
                }
                double demand = pieces.Sum(p => p.LengthMm);
                double stockTotal = plan.Sum(b => b.StockLengthMm);
                double purchasedTotal = plan.Where(b => b.Source == "Mua mới").Sum(b => b.StockLengthMm);
                double offcutTotal = plan.Sum(b => b.OffcutMm);
                int reusable = plan.Count(b => b.OffcutMm > 1e-6 && b.OffcutMm >= settings.ReusableOffcutMm);
                int stockUsed = plan.Count(b => b.Source == "Kho");
                TaskDialog.Show("KStruct - Tối ưu cắt thép",
                    "Đã xếp " + pieces.Count + " thanh vào " + plan.Count + " cây thép." +
                    "\nChiều dài yêu cầu: " + F(demand) + " mm" +
                    "\nPhôi tồn kho dùng trong phương án: " + stockUsed + " cây" +
                    "\nChiều dài mua mới theo cây chuẩn: " + F(purchasedTotal) + " mm" +
                    "\nTổng chiều dài phôi được phối: " + F(stockTotal) + " mm" +
                    "\nVị trí đã loại theo bộ lọc: " + filteredOut.Count +
                    (issued ? "\nĐã phát hành; đã trừ kho " + issuedInventoryBars + " cây tồn kho và ghi phần dư đạt ngưỡng vào mục Chờ QC." : "\nPhương án chưa phát hành/trừ kho.") +
                    "\nPhần dư tổng: " + F(offcutTotal) + " mm (" + reusable + " đoạn đạt ngưỡng lưu lại)" +
                    (overlength.Count > 0 ? "\nCó " + overlength.Count + " thanh dài hơn cây thép, được ghi riêng để xử lý." : "") +
                    "\nĐã xuất: " + review.CsvPath);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tối ưu cắt thép", ex.Message);
                return Result.Failed;
            }
        }

        private static List<Piece> ExpandPieces(Document doc, IEnumerable<Rebar> rebars, Settings settings)
        {
            var pieces = new List<Piece>();
            foreach (Rebar rebar in rebars)
            {
                RebarBarType type = doc.GetElement(rebar.GetTypeId()) as RebarBarType;
                Parameter diameter = rebar.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                double diameterMm = diameter != null && diameter.HasValue
                    ? UnitUtils.ConvertFromInternalUnits(diameter.AsDouble(), UnitTypeId.Millimeters)
                    : type == null ? 0 : UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters);
                string mark = rebar.ScheduleMark ?? "";
                string hostMark = ReadString(rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_HOST_MARK));
                string partition = ReadString(rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM));
                RebarShape shape = doc.GetElement(rebar.GetShapeId()) as RebarShape;
                string kind = shape == null ? "Chưa phân loại" : shape.RebarStyle == RebarStyle.StirrupTie ? "Đai" : "Thép dọc";
                int positionCount = Math.Max(1, rebar.NumberOfBarPositions);
                for (int position = 0; position < positionCount; position++)
                {
                    if (positionCount > 1 && !rebar.DoesBarExistAtPosition(position)) continue;
                    IList<Curve> curves;
                    try
                    {
                        curves = rebar.GetCenterlineCurves(false, false, false,
                            MultiplanarOption.IncludeAllMultiplanarCurves, position);
                    }
                    catch { curves = null; }
                    Parameter lengthParameter = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH);
                    double revitLengthMm = positionCount == 1 && lengthParameter != null && lengthParameter.HasValue
                        ? UnitUtils.ConvertFromInternalUnits(lengthParameter.AsDouble(), UnitTypeId.Millimeters)
                        : curves == null ? 0 : UnitUtils.ConvertFromInternalUnits(curves.Sum(curve => curve.Length), UnitTypeId.Millimeters);
                    double centerlineLengthMm = curves == null ? 0 : UnitUtils.ConvertFromInternalUnits(curves.Sum(curve => curve.Length), UnitTypeId.Millimeters);
                    if (revitLengthMm <= 0 && lengthParameter != null && lengthParameter.HasValue)
                        revitLengthMm = UnitUtils.ConvertFromInternalUnits(lengthParameter.AsDouble(), UnitTypeId.Millimeters);
                    if (revitLengthMm <= 0) revitLengthMm = centerlineLengthMm;
                    if (revitLengthMm <= 0) continue;
                    var validation = ValidateGeometry(curves, type, shape);
                    double cadLegLengthMm = GetCadLegLengthMm(curves, shape, centerlineLengthMm);
                    bool useCadLegs = settings.UseCadLegsLength && shape != null && !shape.SimpleArc;
                    double cutLengthMm = useCadLegs
                        ? cadLegLengthMm : revitLengthMm;
                    pieces.Add(new Piece
                    {
                        Mark = mark,
                        HostMark = hostMark,
                        TypeName = type == null ? "(unknown type)" : type.Name,
                        Partition = partition,
                        ShapeName = shape == null ? "Thanh tự do/không có RebarShape" : shape.Name,
                        Geometry = GeometryText(curves),
                        Kind = kind,
                        BlankMethod = useCadLegs ? "Chân CAD; cung uốn thành hai chân" : "Chiều dài Revit/đường tim",
                        Validation = validation,
                        Position = position + 1,
                        DiameterMm = diameterMm,
                        LengthMm = cutLengthMm,
                        CenterlineLengthMm = centerlineLengthMm,
                        RevitLengthMm = revitLengthMm,
                        DeltaMm = cutLengthMm - revitLengthMm
                    });
                }
            }
            return pieces;
        }

        private static double GetCadLegLengthMm(IList<Curve> curves, RebarShape shape, double centerlineLengthMm)
        {
            if (curves == null || curves.Count == 0 || shape == null || shape.SimpleArc) return centerlineLengthMm;
            double total = 0;
            foreach (Curve curve in curves)
            {
                Arc arc = curve as Arc;
                if (arc == null) total += curve.Length;
                else
                {
                    double includedAngle = arc.Length / Math.Max(arc.Radius, 1e-9);
                    if (includedAngle <= 1e-6 || includedAngle >= Math.PI - 1e-4) total += arc.Length;
                    else total += 2.0 * arc.Radius * Math.Tan(includedAngle / 2.0);
                }
            }
            return UnitUtils.ConvertFromInternalUnits(total, UnitTypeId.Millimeters);
        }

        private static string GetBlankMethod(Piece piece, Settings settings)
        {
            return piece.BlankMethod ?? "Chiều dài Revit/đường tim";
        }

        private static string ValidateGeometry(IList<Curve> curves, RebarBarType type, RebarShape shape)
        {
            if (curves == null || curves.Count == 0) return "Không đọc được đường tim Rebar";
            var issues = new List<string>();
            double shortTolerance = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Millimeters);
            if (shape == null) issues.Add("Không có RebarShape");
            for (int i = 0; i < curves.Count; i++)
            {
                Curve curve = curves[i];
                if (curve.Length <= shortTolerance) issues.Add("Có đoạn ngắn gần 0");
                if (i + 1 < curves.Count && curve.GetEndPoint(1).DistanceTo(curves[i + 1].GetEndPoint(0)) >
                    UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters))
                    issues.Add("Các đoạn đường tim không nối liên tục");
                Arc arc = curve as Arc;
                if (arc == null || type == null) continue;
                if (arc.Radius < type.BarNominalDiameter / 2.0 - 1e-7)
                    issues.Add("Bán kính tim cung nhỏ hơn bán kính danh nghĩa thanh");
                if (type.MaximumBendRadius > 0 && arc.Radius > type.MaximumBendRadius + 1e-7)
                    issues.Add("Bán kính cung vượt MaximumBendRadius của RebarBarType");
            }
            return string.Join("; ", issues.Distinct());
        }

        private static string GeometryText(IList<Curve> curves)
        {
            if (curves == null || curves.Count == 0) return "Không đọc được hình dạng";
            var segments = new List<string>();
            for (int i = 0; i < curves.Count; i++)
            {
                Curve curve = curves[i];
                Arc arc = curve as Arc;
                segments.Add(arc == null
                    ? "L" + Number(UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Millimeters))
                    : "A" + Number(UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Millimeters)) +
                      " R" + Number(UnitUtils.ConvertFromInternalUnits(arc.Radius, UnitTypeId.Millimeters)));
                if (i + 1 < curves.Count)
                {
                    XYZ endTangent = curve.ComputeDerivatives(1.0, true).BasisX.Normalize();
                    XYZ nextTangent = curves[i + 1].ComputeDerivatives(0.0, true).BasisX.Normalize();
                    segments.Add("B" + Number(endTangent.AngleTo(nextTangent) * 180.0 / Math.PI));
                }
            }
            return string.Join("-", segments);
        }

        private static double GetCenterlineLengthMm(Rebar rebar)
        {
            try
            {
                IList<Curve> curves = rebar.GetCenterlineCurves(false, false, false,
                    MultiplanarOption.IncludeAllMultiplanarCurves, 0);
                return curves == null ? 0 :
                    UnitUtils.ConvertFromInternalUnits(curves.Sum(c => c.Length), UnitTypeId.Millimeters);
            }
            catch { return 0; }
        }

        private static List<StockBar> NestBestFitDecreasing(List<Piece> pieces, Settings settings,
            IList<RebarStockInventoryItem> availableStock)
        {
            var inventoryBins = new List<StockBar>();
            foreach (RebarStockInventoryItem item in availableStock.OrderBy(item => item.LengthMm))
            {
                int count = Math.Min(item.Quantity, 10000);
                for (int i = 0; i < count; i++)
                    inventoryBins.Add(new StockBar
                    {
                        StockLengthMm = item.LengthMm,
                        KerfMm = settings.KerfMm,
                        Source = "Kho",
                        Warehouse = item.Warehouse,
                        Bin = item.Bin,
                        InventoryItemId = item.Id
                    });
            }
            var newBins = new List<StockBar>();
            foreach (Piece piece in pieces.OrderByDescending(p => p.LengthMm)
                .ThenBy(p => p.Mark, StringComparer.CurrentCultureIgnoreCase))
            {
                StockBar best = null;
                double bestRemaining = double.MaxValue;
                foreach (StockBar bin in inventoryBins)
                {
                    double kerf = bin.Pieces.Count == 0 ? 0 : settings.KerfMm;
                    double remaining = bin.StockLengthMm - bin.UsedMm - kerf - piece.LengthMm;
                    if (remaining >= -1e-6 && remaining < bestRemaining)
                    {
                        best = bin;
                        bestRemaining = remaining;
                    }
                }
                foreach (StockBar bin in newBins)
                {
                    double kerf = bin.Pieces.Count == 0 ? 0 : settings.KerfMm;
                    double remaining = bin.StockLengthMm - bin.UsedMm - kerf - piece.LengthMm;
                    if (remaining >= -1e-6 && remaining < bestRemaining)
                    {
                        best = bin;
                        bestRemaining = remaining;
                    }
                }
                if (best == null)
                {
                    if (piece.LengthMm > settings.StockLengthMm + 1e-6) continue;
                    best = new StockBar
                    {
                        StockLengthMm = settings.StockLengthMm,
                        KerfMm = settings.KerfMm,
                        Source = "Mua mới"
                    };
                    newBins.Add(best);
                }
                double addedKerf = best.Pieces.Count == 0 ? 0 : settings.KerfMm;
                best.UsedMm += addedKerf + piece.LengthMm;
                best.Pieces.Add(piece);
            }
            return inventoryBins.Concat(newBins).Where(bin => bin.Pieces.Count > 0).ToList();
        }

        private static void WriteCsv(string path, IList<StockBar> plan, IList<Piece> overlength,
            IList<Piece> filteredOut, IList<Piece> shapeIssues, Settings settings)
        {
            var rows = new List<string>
            {
                "Rebar Type,Diameter (mm),Partition,Stock No,Source,Warehouse,Bin,Stock Length (mm),Mark,Host Mark,Position,Kind,Shape,Bar Geometry,Blank Method,Revit Length (mm),Centerline Length (mm),Cut Length (mm),Delta vs Revit (mm),Geometry Validation,Kerf (mm),Offcut (mm),Reusable,Status,Reason"
            };
            foreach (StockBar stock in plan)
            {
                foreach (Piece piece in stock.Pieces)
                    rows.Add(string.Join(",", new[]
                    {
                        Csv(piece.TypeName), Number(piece.DiameterMm), Csv(piece.Partition),
                        stock.Number.ToString(CultureInfo.InvariantCulture), Csv(stock.Source), Csv(stock.Warehouse), Csv(stock.Bin), Number(stock.StockLengthMm),
                        Csv(piece.Mark), Csv(piece.HostMark), piece.Position.ToString(CultureInfo.InvariantCulture), Csv(piece.Kind), Csv(piece.ShapeName), Csv(piece.Geometry),
                        Csv(GetBlankMethod(piece, settings)), Number(piece.RevitLengthMm), Number(piece.CenterlineLengthMm), Number(piece.LengthMm), Number(piece.DeltaMm), Csv(piece.Validation),
                        Number(stock.KerfMm), Number(stock.OffcutMm),
                        stock.OffcutMm > 1e-6 && stock.OffcutMm >= settings.ReusableOffcutMm ? "YES" : "NO", "PLANNED", ""
                    }));
            }
            foreach (Piece piece in overlength)
                rows.Add(string.Join(",", new[]
                {
                    Csv(piece.TypeName), Number(piece.DiameterMm), Csv(piece.Partition), "OVERLENGTH", "",
                    "", "", Number(settings.StockLengthMm), Csv(piece.Mark), Csv(piece.HostMark),
                    piece.Position.ToString(CultureInfo.InvariantCulture), Csv(piece.Kind), Csv(piece.ShapeName), Csv(piece.Geometry),
                    Csv(GetBlankMethod(piece, settings)), Number(piece.RevitLengthMm), Number(piece.CenterlineLengthMm), Number(piece.LengthMm), Number(piece.DeltaMm), Csv(piece.Validation),
                    Number(settings.KerfMm), "", "NO", "OVERLENGTH", Csv("Dài hơn phôi khả dụng")
                }));
            foreach (Piece piece in filteredOut)
                rows.Add(string.Join(",", new[]
                {
                    Csv(piece.TypeName), Number(piece.DiameterMm), Csv(piece.Partition), "FILTERED", "", "", "", "",
                    Csv(piece.Mark), Csv(piece.HostMark), piece.Position.ToString(CultureInfo.InvariantCulture), Csv(piece.Kind),
                    Csv(piece.ShapeName), Csv(piece.Geometry), Csv(GetBlankMethod(piece, settings)), Number(piece.RevitLengthMm), Number(piece.CenterlineLengthMm), Number(piece.LengthMm), Number(piece.DeltaMm), Csv(piece.Validation),
                    "", "", "NO", "FILTERED_OUT", "Không chọn nhóm Rebar này"
                }));
            foreach (Piece piece in shapeIssues)
                rows.Add(string.Join(",", new[]
                {
                    Csv(piece.TypeName), Number(piece.DiameterMm), Csv(piece.Partition), "GEOMETRY_REVIEW", "", "", "", "",
                    Csv(piece.Mark), Csv(piece.HostMark), piece.Position.ToString(CultureInfo.InvariantCulture), Csv(piece.Kind), Csv(piece.ShapeName), Csv(piece.Geometry),
                    Csv(GetBlankMethod(piece, settings)), Number(piece.RevitLengthMm), Number(piece.CenterlineLengthMm), Number(piece.LengthMm), Number(piece.DeltaMm), Csv(piece.Validation),
                    "", "", "NO", "GEOMETRY_REVIEW", Csv(piece.Validation)
                }));
            File.WriteAllLines(path, rows, new UTF8Encoding(true));
        }

        private static ReviewResult ShowReview(IntPtr owner, IList<Piece> pieces, IList<StockBar> plan,
            IList<Piece> overlength, IList<Piece> filteredOut, IList<Piece> shapeIssues, Settings settings)
        {
            var window = new Window
            {
                Title = "BeamCut - nhu cầu, kho và phương án cắt | KStruct",
                Width = 1120,
                Height = 720,
                MinWidth = 900,
                MinHeight = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new DockPanel { Margin = new Thickness(10) };
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            ReviewResult result = null;
            var draft = new Button { Content = "Xuất bản nháp CSV", MinWidth = 135, Margin = new Thickness(0, 0, 8, 0) };
            var issue = new Button { Content = "Phát hành & trừ kho…", MinWidth = 145, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 75, IsCancel = true };
            draft.Click += (sender, args) =>
            {
                string path = ChoosePlanPath();
                if (path == null) return;
                result = new ReviewResult { CsvPath = path, IssueInventory = false };
                window.DialogResult = true;
            };
            issue.Click += (sender, args) =>
            {
                if (plan.Count == 0)
                {
                    TaskDialog.Show("KStruct - BeamCut", "Không có cây cắt nào để phát hành.");
                    return;
                }
                if (overlength.Count + filteredOut.Count + shapeIssues.Count > 0 && MessageBox.Show("Có " + overlength.Count + " vị trí vượt phôi, " + filteredOut.Count +
                    " vị trí bị loại theo bộ lọc và " + shapeIssues.Count + " vị trí có cảnh báo hình học ở tab 4. Phát hành phần kế hoạch còn lại?",
                    "Xác nhận phạm vi phát hành", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
                    return;
                string path = ChoosePlanPath();
                if (path == null) return;
                if (MessageBox.Show("Phát hành phương án cắt này? KStruct sẽ trừ phôi tồn kho đã dùng và ghi phần dư đạt ngưỡng vào kho chờ QC.",
                    "Xác nhận phát hành", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return;
                result = new ReviewResult { CsvPath = path, IssueInventory = true };
                window.DialogResult = true;
            };
            footer.Children.Add(draft);
            footer.Children.Add(issue);
            footer.Children.Add(cancel);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var tabs = new TabControl();
            var demandRows = pieces.Select(piece => new
            {
                Beam = piece.HostMark,
                Mark = piece.Mark,
                Position = piece.Position,
                Type = piece.TypeName,
                DiameterMm = piece.DiameterMm,
                Kind = piece.Kind,
                Shape = piece.ShapeName,
                Geometry = piece.Geometry,
                BlankMethod = GetBlankMethod(piece, settings),
                RevitLengthMm = piece.RevitLengthMm,
                CenterlineLengthMm = piece.CenterlineLengthMm,
                CutLengthMm = piece.LengthMm,
                DeltaVsRevitMm = piece.DeltaMm,
                GeometryValidation = piece.Validation,
                Partition = piece.Partition
            }).ToList();
            tabs.Items.Add(new TabItem
            {
                Header = "1 · Nhu cầu shop (" + demandRows.Count + ")",
                Content = CreateReviewPage("Mỗi dòng là một vị trí Rebar. Kiểm tra Mark, hình dạng, đường tim và chiều dài trước khi dùng phương án.", demandRows)
            });

            var stockRows = settings.Inventory.Select(item => new
            {
                Type = item.RebarTypeName,
                item.DiameterMm,
                item.LengthMm,
                item.Quantity,
                item.Warehouse,
                item.Bin,
                item.Available,
                item.QcPassed,
                UsedInPlan = plan.Count(bar => bar.InventoryItemId == item.Id)
            }).ToList();
            tabs.Items.Add(new TabItem
            {
                Header = "2 · Kho phôi (" + stockRows.Count + ")",
                Content = CreateReviewPage("Chỉ phôi có Available và QC passed được phối. Cây chuẩn mua mới được tạo khi tồn kho không có phôi phù hợp.", stockRows)
            });

            var planRows = plan.Select(bar => new
            {
                StockNo = bar.Number,
                Source = bar.Source,
                Warehouse = bar.Warehouse,
                Bin = bar.Bin,
                StockLengthMm = bar.StockLengthMm,
                UsedMm = bar.UsedMm,
                OffcutMm = bar.OffcutMm,
                CutCount = bar.Pieces.Count,
                Cuts = string.Join(" | ", bar.Pieces.Select(piece => piece.Mark + " / " + piece.ShapeName + " / " + Number(piece.LengthMm) + " mm"))
            }).ToList();
            tabs.Items.Add(new TabItem
            {
                Header = "3 · Tổ hợp cắt (" + planRows.Count + ")",
                Content = CreateReviewPage("Xem nguồn phôi, vị trí kho, từng thanh cắt và phần dư của mỗi cây.", planRows)
            });

            double maxAvailableLength = Math.Max(settings.StockLengthMm,
                settings.Inventory.Where(item => item.Available && item.QcPassed)
                    .Select(item => item.LengthMm).DefaultIfEmpty(settings.StockLengthMm).Max());
            var issueRows = overlength.Select(piece => new
            {
                Beam = piece.HostMark,
                Mark = piece.Mark,
                Position = piece.Position,
                Type = piece.TypeName,
                DiameterMm = piece.DiameterMm,
                Shape = piece.ShapeName,
                LengthMm = piece.LengthMm,
                MaxAvailableMm = maxAvailableLength,
                Reason = "Dài hơn tất cả phôi khả dụng; cần chia/nối hoặc cấp phôi dài hơn.",
                GeometryValidation = piece.Validation
            }).Cast<object>().Concat(filteredOut.Select(piece => (object)new
            {
                Beam = piece.HostMark,
                Mark = piece.Mark,
                Position = piece.Position,
                Type = piece.TypeName,
                DiameterMm = piece.DiameterMm,
                Kind = piece.Kind,
                Shape = piece.ShapeName,
                LengthMm = piece.LengthMm,
                MaxAvailableMm = maxAvailableLength,
                Reason = "Bị loại theo bộ lọc thép dọc/đai đã chọn.",
                GeometryValidation = piece.Validation
            })).Concat(shapeIssues.Select(piece => (object)new
            {
                Beam = piece.HostMark,
                Mark = piece.Mark,
                Position = piece.Position,
                Type = piece.TypeName,
                DiameterMm = piece.DiameterMm,
                Kind = piece.Kind,
                Shape = piece.ShapeName,
                LengthMm = piece.LengthMm,
                MaxAvailableMm = maxAvailableLength,
                Reason = "Cần rà soát hình học Rebar trước khi dùng chiều dài cắt.",
                GeometryValidation = piece.Validation
            })).ToList();
            tabs.Items.Add(new TabItem
            {
                Header = "4 · Ngoài phạm vi (" + issueRows.Count + ")",
                Content = CreateReviewPage(issueRows.Count == 0 ? "Không có thanh vượt phôi hoặc cảnh báo hình học trong phương án này." : "Kiểm tra các thanh vượt phôi, bị loại bởi bộ lọc hoặc có cảnh báo hình học trước khi phát hành.", issueRows)
            });
            root.Children.Add(tabs);
            window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }

        private static string ChoosePlanPath()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Lưu phiếu cắt BeamCut KStruct",
                Filter = "CSV UTF-8 (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = "KStruct_CuttingPlan_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".csv"
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private static UIElement CreateReviewPage(string description, object rows)
        {
            var panel = new DockPanel { Margin = new Thickness(8) };
            var note = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(note, Dock.Top);
            panel.Children.Add(note);
            panel.Children.Add(new DataGrid
            {
                ItemsSource = rows as System.Collections.IEnumerable,
                AutoGenerateColumns = true,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow
            });
            return panel;
        }

        private static Settings ShowSettings(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Tối ưu cắt thép - KStruct",
                Width = 390,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new StackPanel { Margin = new Thickness(14) };
            var stockBox = AddNumber(root, "Chiều dài cây thép (mm):", "12000");
            var kerfBox = AddNumber(root, "Bề rộng lưỡi cắt (mm):", "3");
            var reuseBox = AddNumber(root, "Ngưỡng giữ lại phần dư (mm):", "1000");
            var includeLongitudinal = new CheckBox { Content = "Đưa thép dọc vào nhu cầu", IsChecked = true, Margin = new Thickness(0, 0, 0, 2) };
            var includeStirrups = new CheckBox { Content = "Đưa thép đai vào nhu cầu", IsChecked = true, Margin = new Thickness(0, 0, 0, 2) };
            var includeUnknown = new CheckBox { Content = "Giữ cả Rebar không có RebarShape để rà soát", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
            var cadLegs = new CheckBox { Content = "Blank length theo chân CAD (cung uốn đổi sang hai đoạn đến giao điểm)", IsChecked = false, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(includeLongitudinal);
            root.Children.Add(includeStirrups);
            root.Children.Add(includeUnknown);
            root.Children.Add(cadLegs);
            var manageStock = new Button { Content = "Quản lý kho cây/phôi…", MinWidth = 150, Margin = new Thickness(0, 0, 0, 10) };
            manageStock.Click += (s, e) => RebarStockInventoryEditor.Show(new WindowInteropHelper(window).Handle);
            root.Children.Add(manageStock);
            var note = new TextBlock
            {
                Text = "Xếp thanh theo thuật toán giảm dần/khớp phần dư tốt nhất. Mỗi loại thép, đường kính và Partition được tính riêng. Đai phân loại theo RebarShape.RebarStyle; Rebar tự do chưa phân loại có bộ lọc riêng.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(note);
            Settings result = null;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Tiếp tục", MinWidth = 85, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 75, IsCancel = true };
            ok.Click += (s, e) =>
            {
                double stock;
                double kerf;
                double reuse;
                if (includeLongitudinal.IsChecked != true && includeStirrups.IsChecked != true && includeUnknown.IsChecked != true)
                {
                    TaskDialog.Show("KStruct - Tối ưu cắt thép", "Chọn ít nhất một nhóm Rebar để đưa vào nhu cầu.");
                    return;
                }
                if (!TryNumber(stockBox.Text, out stock) || !TryNumber(kerfBox.Text, out kerf) ||
                    !TryNumber(reuseBox.Text, out reuse) || stock <= 0 || kerf < 0 || reuse < 0)
                {
                    TaskDialog.Show("KStruct - Tối ưu cắt thép", "Nhập chiều dài cây lớn hơn 0 và giá trị kerf/ngưỡng dư không âm.");
                    return;
                }
                result = new Settings
                {
                    StockLengthMm = stock,
                    KerfMm = kerf,
                    ReusableOffcutMm = reuse,
                    IncludeLongitudinal = includeLongitudinal.IsChecked == true,
                    IncludeStirrups = includeStirrups.IsChecked == true,
                    IncludeUnclassified = includeUnknown.IsChecked == true,
                    UseCadLegsLength = cadLegs.IsChecked == true
                };
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }

        private static System.Windows.Controls.TextBox AddNumber(System.Windows.Controls.Panel root, string label, string initial)
        {
            root.Children.Add(new TextBlock { Text = label });
            var box = new System.Windows.Controls.TextBox { Text = initial, MinWidth = 350, Margin = new Thickness(0, 3, 0, 8) };
            root.Children.Add(box);
            return box;
        }

        private static bool TryNumber(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string ReadString(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue) return "";
            return parameter.AsString() ?? parameter.AsValueString() ?? "";
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string Number(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string F(double value)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }
    }
}
