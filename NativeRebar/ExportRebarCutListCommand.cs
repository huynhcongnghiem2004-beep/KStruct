using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ExportRebarCutListCommand : IExternalCommand
    {
        private sealed class CutRow
        {
            public string Mark;
            public string HostMark;
            public string Partition;
            public string BarType;
            public string BarShape;
            public string Geometry;
            public double DiameterMm;
            public double BarLengthMm;
            public int Quantity;
            public double TotalLengthMm;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> selection = ui.Selection.GetElementIds().Select(doc.GetElement).Where(element => element != null).ToList();
                List<Rebar> rebars = selection.OfType<Rebar>().Distinct().ToList();
                List<ElementId> selectedBeamIds = selection.OfType<FamilyInstance>()
                    .Where(element => element.Category != null && element.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming)
                    .Select(element => element.Id).ToList();
                if (rebars.Count == 0 && selectedBeamIds.Count > 0)
                {
                    HashSet<ElementId> hosts = new HashSet<ElementId>(selectedBeamIds);
                    rebars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                        .Cast<Rebar>().Where(bar => hosts.Contains(bar.GetHostId())).ToList();
                }
                if (rebars.Count == 0 && selectedBeamIds.Count == 0)
                {
                    rebars = new FilteredElementCollector(doc, ui.ActiveView.Id).OfClass(typeof(Rebar))
                        .WhereElementIsNotElementType().Cast<Rebar>().ToList();
                }
                if (rebars.Count == 0)
                {
                    TaskDialog.Show("KStruct - Phiếu cắt thép", "Không tìm thấy Rebar trong thép đã chọn, các dầm đã chọn hoặc view hiện tại.");
                    return Result.Cancelled;
                }

                List<CutRow> rows = rebars.Select(r => MakeRow(doc, r))
                    .GroupBy(r => new { r.Mark, r.HostMark, r.Partition, r.BarType, r.BarShape, r.Geometry, r.DiameterMm, r.BarLengthMm })
                    .Select(g => new CutRow
                    {
                        Mark = g.Key.Mark,
                        HostMark = g.Key.HostMark,
                        Partition = g.Key.Partition,
                        BarType = g.Key.BarType,
                        BarShape = g.Key.BarShape,
                        Geometry = g.Key.Geometry,
                        DiameterMm = g.Key.DiameterMm,
                        BarLengthMm = g.Key.BarLengthMm,
                        Quantity = g.Sum(x => x.Quantity),
                        TotalLengthMm = g.Sum(x => x.TotalLengthMm)
                    })
                    .OrderBy(r => r.Partition, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(r => r.Mark, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(r => r.DiameterMm)
                    .ThenBy(r => r.BarLengthMm)
                    .ToList();

                var dialog = new SaveFileDialog
                {
                    Title = "Xuất phiếu cắt thép KStruct",
                    Filter = "CSV UTF-8 (*.csv)|*.csv",
                    DefaultExt = ".csv",
                    AddExtension = true,
                    FileName = "KStruct_CutList_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".csv"
                };
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                string[] lines = new[] { "Mark,Host Mark,Partition,Rebar Type,Shape,Bar Geometry,Diameter (mm),Bar Length (mm),Quantity,Total Length (mm)" }
                    .Concat(rows.Select(ToCsv)).ToArray();
                File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));

                string scope = selectedBeamIds.Count > 0 && selection.OfType<Rebar>().Count() == 0
                    ? " từ Rebar của " + selectedBeamIds.Count + " dầm đã chọn"
                    : " từ " + rebars.Count + " phần tử Rebar";
                TaskDialog.Show("KStruct - Phiếu cắt thép", "Đã xuất " + rows.Count + " dòng thống kê" + scope +
                    ". Kiểm tra quy tắc làm tròn và nối thanh trước khi dùng để đặt cắt.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Phiếu cắt thép", ex.Message);
                return Result.Failed;
            }
        }

        private static CutRow MakeRow(Document doc, Rebar rebar)
        {
            Parameter mark = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_SCHEDULE_MARK);
            Parameter hostMark = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_HOST_MARK);
            Parameter partition = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            Parameter diameter = rebar.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
            Parameter length = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH);
            Parameter quantity = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS);
            Parameter total = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH);
            Element type = doc.GetElement(rebar.GetTypeId());
            Element shape = doc.GetElement(rebar.GetShapeId());
            return new CutRow
            {
                Mark = StringValue(mark),
                HostMark = StringValue(hostMark),
                Partition = StringValue(partition),
                BarType = type == null ? "" : type.Name,
                BarShape = shape == null ? "Thanh tự do/không có RebarShape" : shape.Name,
                Geometry = GeometryText(rebar),
                DiameterMm = MmValue(diameter),
                BarLengthMm = MmValue(length),
                Quantity = quantity != null && quantity.HasValue ? quantity.AsInteger() : rebar.Quantity,
                TotalLengthMm = MmValue(total)
            };
        }

        private static string StringValue(Parameter parameter)
        {
            return parameter == null || !parameter.HasValue ? "" : (parameter.AsString() ?? parameter.AsValueString() ?? "");
        }

        private static double MmValue(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue || parameter.StorageType != StorageType.Double) return 0;
            return UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Millimeters);
        }

        private static string ToCsv(CutRow row)
        {
            return string.Join(",", new[]
            {
                Csv(row.Mark), Csv(row.HostMark), Csv(row.Partition), Csv(row.BarType), Csv(row.BarShape), Csv(row.Geometry),
                Number(row.DiameterMm), Number(row.BarLengthMm), row.Quantity.ToString(CultureInfo.InvariantCulture),
                Number(row.TotalLengthMm)
            });
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string GeometryText(Rebar rebar)
        {
            try
            {
                IList<Curve> curves = rebar.GetCenterlineCurves(false, false, false,
                    MultiplanarOption.IncludeAllMultiplanarCurves, 0);
                if (curves == null || curves.Count == 0) return "";

                var parts = new List<string>();
                for (int i = 0; i < curves.Count; i++)
                {
                    Curve curve = curves[i];
                    Arc arc = curve as Arc;
                    parts.Add(arc == null
                        ? "L" + Number(UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Millimeters))
                        : "A" + Number(UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Millimeters)) +
                          " R" + Number(UnitUtils.ConvertFromInternalUnits(arc.Radius, UnitTypeId.Millimeters)));

                    if (i + 1 < curves.Count)
                    {
                        XYZ endTangent = curve.ComputeDerivatives(1.0, true).BasisX.Normalize();
                        XYZ nextTangent = curves[i + 1].ComputeDerivatives(0.0, true).BasisX.Normalize();
                        double angle = endTangent.AngleTo(nextTangent) * 180.0 / Math.PI;
                        parts.Add("B" + Number(angle));
                    }
                }
                return string.Join("-", parts);
            }
            catch { return "Không đọc được hình dạng"; }
        }

        private static string Number(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
