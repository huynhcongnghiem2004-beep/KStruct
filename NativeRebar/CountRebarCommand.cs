using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class CountRebarCommand : IExternalCommand
    {
        private sealed class RebarSummary
        {
            public string Mark;
            public string TypeName;
            public int Instances;
            public int Positions;
            public double TotalLength;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Rebar> rebars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                bool selectedOnly = rebars.Count > 0;
                if (!selectedOnly)
                    rebars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType().Cast<Rebar>().ToList();
                if (rebars.Count == 0)
                {
                    TaskDialog.Show("KStruct - Thống kê Rebar", "Project chưa có Rebar.");
                    return Result.Cancelled;
                }

                List<RebarSummary> summaries = rebars.GroupBy(rebar =>
                {
                    RebarBarType type = doc.GetElement(rebar.GetTypeId()) as RebarBarType;
                    string typeName = type == null ? "(không rõ loại)" : type.Name;
                    string mark = string.IsNullOrWhiteSpace(rebar.ScheduleMark) ? "(chưa có mã)" : rebar.ScheduleMark;
                    return new { Mark = mark, TypeName = typeName };
                })
                .Select(group => new RebarSummary
                {
                    Mark = group.Key.Mark,
                    TypeName = group.Key.TypeName,
                    Instances = group.Count(),
                    Positions = group.Sum(rebar => Math.Max(1, rebar.NumberOfBarPositions)),
                    TotalLength = group.Sum(rebar =>
                    {
                        Parameter p = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH);
                        return p != null && p.HasValue ? p.AsDouble() : 0;
                    })
                })
                .OrderBy(x => x.Mark, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.TypeName, StringComparer.CurrentCultureIgnoreCase).ToList();

                double totalLengthMm = UnitUtils.ConvertFromInternalUnits(summaries.Sum(x => x.TotalLength), UnitTypeId.Millimeters);
                string scope = selectedOnly ? "phần tử đang chọn" : "toàn bộ project";
                string report = "Phạm vi: " + scope + "\nRebar instances: " + rebars.Count +
                    "\nVị trí thanh: " + summaries.Sum(x => x.Positions) +
                    "\nTổng chiều dài: " + totalLengthMm.ToString("N0") + " mm\n\n";
                const int maxRows = 18;
                report += string.Join("\n", summaries.Take(maxRows).Select(x =>
                    x.Mark + " | " + x.TypeName + " | instances " + x.Instances + " | bars " + x.Positions));
                if (summaries.Count > maxRows) report += "\n… và " + (summaries.Count - maxRows) + " nhóm khác.";
                TaskDialog.Show("KStruct - Thống kê Rebar", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thống kê Rebar", ex.Message);
                return Result.Failed;
            }
        }
    }
}