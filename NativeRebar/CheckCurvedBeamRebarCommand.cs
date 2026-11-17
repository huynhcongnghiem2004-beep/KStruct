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
using Autodesk.Revit.UI.Selection;
using Microsoft.Win32;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class CheckCurvedBeamRebarCommand : IExternalCommand
    {
        private sealed class BeamFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                FamilyInstance instance = element as FamilyInstance;
                return instance != null && instance.Category != null &&
                    instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
            }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        private sealed class ReportRow
        {
            public string BeamId, BeamMark, BeamCurve, RebarId, RebarMark, BarType, Shape, Geometry, Status, Detail;
            public int Quantity;
            public double LengthMm;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> selected = ui.Selection.GetElementIds().Select(doc.GetElement).Where(x => x != null).ToList();
                List<Rebar> selectedBars = selected.OfType<Rebar>().Distinct().ToList();
                List<FamilyInstance> beams = selected.OfType<FamilyInstance>().Where(IsFraming).Distinct().ToList();
                if (beams.Count == 0 && selectedBars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new BeamFilter(),
                        "Chọn dầm cong cần kiểm tra cốt thép");
                    beams = picked.Select(x => doc.GetElement(x)).OfType<FamilyInstance>().Where(IsFraming).Distinct().ToList();
                }

                List<Rebar> rebars = selectedBars;
                if (beams.Count > 0)
                {
                    HashSet<ElementId> hostIds = new HashSet<ElementId>(beams.Select(x => x.Id));
                    rebars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                        .Cast<Rebar>().Where(x => hostIds.Contains(x.GetHostId())).ToList();
                }
                if (rebars.Count == 0)
                {
                    TaskDialog.Show("KStruct - Kiểm tra thép dầm cong", "Không tìm thấy Rebar trên dầm hoặc trong lựa chọn.");
                    return Result.Cancelled;
                }

                Dictionary<ElementId, FamilyInstance> beamById = beams.ToDictionary(x => x.Id, x => x);
                List<ReportRow> rows = rebars.Select(rebar => InspectRebar(doc, rebar, beamById)).ToList();
                var dialog = new SaveFileDialog
                {
                    Title = "Xuất báo cáo kiểm tra Rebar dầm cong",
                    Filter = "CSV UTF-8 (*.csv)|*.csv",
                    DefaultExt = ".csv",
                    AddExtension = true,
                    FileName = "KStruct_CurvedBeamRebarCheck_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".csv"
                };
                if (dialog.ShowDialog() != true) return Result.Cancelled;
                File.WriteAllLines(dialog.FileName, new[]
                {
                    "Beam Id,Beam Mark,Beam Curve,Rebar Id,Rebar Mark,Bar Type,Shape,Quantity,Bar Length (mm),Centerline Geometry,Status,Details"
                }.Concat(rows.Select(ToCsv)), new UTF8Encoding(true));

                int warnings = rows.Count(x => x.Status != "OK");
                TaskDialog.Show("KStruct - Kiểm tra thép dầm cong",
                    "Đã kiểm tra " + rows.Count + " Rebar; có " + warnings + " dòng cần xem lại.\nBáo cáo: " + dialog.FileName +
                    "\nĐây là kiểm tra hình học sơ bộ, không thay thế kiểm tra thiết kế/kết cấu.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Kiểm tra thép dầm cong", ex.Message);
                return Result.Failed;
            }
        }

        private static ReportRow InspectRebar(Document doc, Rebar rebar, IDictionary<ElementId, FamilyInstance> selectedBeams)
        {
            ElementId hostId = rebar.GetHostId();
            FamilyInstance beam;
            if (!selectedBeams.TryGetValue(hostId, out beam)) beam = doc.GetElement(hostId) as FamilyInstance;
            string beamCurve = "Không rõ";
            BoundingBoxXYZ hostBox = null;
            if (beam != null)
            {
                LocationCurve location = beam.Location as LocationCurve;
                beamCurve = location == null ? "Không có LocationCurve" : location.Curve is Arc ? "Cong" : location.Curve is Line ? "Thẳng" : "Khác";
                hostBox = beam.get_BoundingBox(null);
            }

            Parameter mark = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_SCHEDULE_MARK);
            Element barType = doc.GetElement(rebar.GetTypeId());
            Element shape = doc.GetElement(rebar.GetShapeId());
            Parameter quantityParameter = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS);
            Parameter lengthParameter = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH);
            int quantity = quantityParameter != null && quantityParameter.HasValue ? quantityParameter.AsInteger() : rebar.Quantity;
            double lengthMm = lengthParameter != null && lengthParameter.HasValue && lengthParameter.StorageType == StorageType.Double
                ? UnitUtils.ConvertFromInternalUnits(lengthParameter.AsDouble(), UnitTypeId.Millimeters) : 0;
            IList<Curve> curves = null;
            string geometry = "";
            var details = new List<string>();
            try
            {
                curves = rebar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, 0);
                if (curves == null || curves.Count == 0) details.Add("Không đọc được centerline.");
                else
                {
                    geometry = string.Join("-", curves.Select(FormatCurve));
                    for (int i = 0; i < curves.Count; i++)
                    {
                        if (curves[i].Length < doc.Application.ShortCurveTolerance)
                            details.Add("Đoạn " + (i + 1) + " ngắn hơn ShortCurveTolerance.");
                        if (i > 0 && curves[i - 1].GetEndPoint(1).DistanceTo(curves[i].GetEndPoint(0)) > UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters))
                            details.Add("Có khe hở giữa đoạn " + i + " và " + (i + 1) + ".");
                    }
                }
            }
            catch (Exception ex) { details.Add("Lỗi đọc hình học: " + ex.Message); }

            if (hostBox == null) details.Add("Không đọc được bounding box host.");
            else if (curves != null)
            {
                double tolerance = UnitUtils.ConvertToInternalUnits(100, UnitTypeId.Millimeters);
                foreach (Curve curve in curves)
                {
                    foreach (XYZ point in curve.Tessellate())
                    {
                        if (point.X < hostBox.Min.X - tolerance || point.X > hostBox.Max.X + tolerance ||
                            point.Y < hostBox.Min.Y - tolerance || point.Y > hostBox.Max.Y + tolerance ||
                            point.Z < hostBox.Min.Z - tolerance || point.Z > hostBox.Max.Z + tolerance)
                        {
                            details.Add("Centerline vượt bounding box host quá 100 mm.");
                            goto OutsideHost;
                        }
                    }
                }
            }
        OutsideHost:
            return new ReportRow
            {
                BeamId = beam == null ? hostId.Value.ToString() : beam.Id.Value.ToString(),
                BeamMark = beam == null ? "" : StringParameter(beam, BuiltInParameter.ALL_MODEL_MARK),
                BeamCurve = beamCurve,
                RebarId = rebar.Id.Value.ToString(),
                RebarMark = mark == null || !mark.HasValue ? "" : mark.AsString(),
                BarType = barType == null ? "" : barType.Name,
                Shape = shape == null ? "" : shape.Name,
                Quantity = quantity,
                LengthMm = lengthMm,
                Geometry = geometry,
                Status = details.Count == 0 ? "OK" : "KIỂM TRA",
                Detail = string.Join("; ", details.Distinct())
            };
        }

        private static string FormatCurve(Curve curve)
        {
            Arc arc = curve as Arc;
            double length = UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Millimeters);
            if (arc == null) return "L" + length.ToString("0.###", CultureInfo.InvariantCulture);
            double radius = UnitUtils.ConvertFromInternalUnits(arc.Radius, UnitTypeId.Millimeters);
            return "A" + length.ToString("0.###", CultureInfo.InvariantCulture) + " R" + radius.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string StringParameter(Element element, BuiltInParameter id)
        {
            Parameter parameter = element.get_Parameter(id);
            return parameter == null || !parameter.HasValue ? "" : (parameter.AsString() ?? parameter.AsValueString() ?? "");
        }

        private static string ToCsv(ReportRow row)
        {
            return string.Join(",", new[]
            {
                Csv(row.BeamId), Csv(row.BeamMark), Csv(row.BeamCurve), Csv(row.RebarId), Csv(row.RebarMark),
                Csv(row.BarType), Csv(row.Shape), row.Quantity.ToString(CultureInfo.InvariantCulture),
                row.LengthMm.ToString("0.###", CultureInfo.InvariantCulture), Csv(row.Geometry), Csv(row.Status), Csv(row.Detail)
            });
        }

        private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
        private static bool IsFraming(FamilyInstance instance) => instance.Category != null &&
            instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
    }
}
