using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class RebarSegmentLengthCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Rebar> rebars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (rebars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(), "Chọn các thanh thép cần đo chiều dài từng đoạn");
                    rebars = picked.Select(r => doc.GetElement(r)).OfType<Rebar>().Distinct().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;

                var rows = new List<Tuple<Rebar, IList<Curve>>>();
                var failures = new List<string>();
                foreach (Rebar rebar in rebars)
                {
                    try
                    {
                        IList<Curve> curves = rebar.GetCenterlineCurves(false, false, false,
                            MultiplanarOption.IncludeAllMultiplanarCurves, 0);
                        if (curves == null || curves.Count == 0) failures.Add(rebar.Id + ": không đọc được đường tim.");
                        else rows.Add(Tuple.Create(rebar, curves));
                    }
                    catch (Exception ex) { failures.Add(rebar.Id + ": " + ex.Message); }
                }
                if (rows.Count == 0)
                {
                    TaskDialog.Show("KStruct - Chiều dài Rebar", "Không đọc được hình học thanh thép đã chọn.\n" + string.Join("\n", failures.Take(8)));
                    return Result.Cancelled;
                }

                double total = 0;
                var report = new StringBuilder();
                report.AppendLine("Chiều dài đường tim theo từng đoạn (mm)");
                report.AppendLine("Đơn vị: mm | Các đoạn cong được tính theo chiều dài cung.");
                foreach (var row in rows)
                {
                    double[] segmentLengths = row.Item2.Select(c => UnitUtils.ConvertFromInternalUnits(c.Length, UnitTypeId.Millimeters)).ToArray();
                    double barTotal = segmentLengths.Sum();
                    total += barTotal;
                    report.Append("Rebar ").Append(row.Item1.Id).Append(": ")
                        .Append(string.Join(" + ", segmentLengths.Select(x => x.ToString("0.##"))))
                        .Append(" = ").Append(barTotal.ToString("0.##")).AppendLine();
                }
                report.AppendLine("--------------------------------");
                report.Append("Tổng ").Append(rows.Count).Append(" Rebar: ").Append(total.ToString("0.##")).Append(" mm");
                if (failures.Count > 0)
                {
                    report.AppendLine().Append("Không đọc được ").Append(failures.Count).Append(" thanh.");
                    foreach (string failure in failures.Take(5)) report.AppendLine().Append(failure);
                    if (failures.Count > 5) report.AppendLine().Append("...");
                }
                TaskDialog.Show("KStruct - Chiều dài Rebar", report.ToString());
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Chiều dài Rebar", ex.Message);
                return Result.Failed;
            }
        }
    }
}
