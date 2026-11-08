using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ExplodeRebarSetCommand : IExternalCommand
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
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(), "Chọn Rebar set cần tách thành các thanh riêng");
                    rebars = picked.Select(r => doc.GetElement(r)).OfType<Rebar>().Distinct().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;

                int sets = 0;
                int bars = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Tách Rebar set"))
                {
                    tx.Start();
                    foreach (Rebar source in rebars)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                int count = ExplodeOne(doc, source);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận các thanh riêng.");
                                sets++;
                                bars += count;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Rebar " + source.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (sets == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Tách Rebar", "Không tách được Rebar set nào. Hãy chọn set có từ hai vị trí trở lên và có RebarShape hợp lệ.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi Rebar.");
                }
                string report = "Đã tách " + sets + " Rebar set thành " + bars + " thanh đơn.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " set:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Tách Rebar", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tách Rebar", ex.Message);
                return Result.Failed;
            }
        }

        private static int ExplodeOne(Document doc, Rebar source)
        {
            if (source.IsRebarFreeForm() || !source.IsRebarShapeDriven())
                throw new InvalidOperationException("Chỉ hỗ trợ Rebar set shape-driven.");
            int count = source.NumberOfBarPositions;
            if (count < 2) throw new InvalidOperationException("Rebar đã là một thanh đơn.");
            RebarShape shape = doc.GetElement(source.GetShapeId()) as RebarShape;
            RebarBarType type = doc.GetElement(source.GetTypeId()) as RebarBarType;
            Element host = doc.GetElement(source.GetHostId());
            if (shape == null || type == null || host == null)
                throw new InvalidOperationException("Không đọc được RebarShape, RebarBarType hoặc host.");
            RebarShapeDrivenAccessor accessor = source.GetShapeDrivenAccessor();
            XYZ normal = accessor.Normal;
            if (normal == null || normal.GetLength() < 1e-8) throw new InvalidOperationException("Không xác định được mặt phẳng Rebar.");
            RebarHookType startHook = GetHook(doc, source, 0);
            RebarHookType endHook = GetHook(doc, source, 1);
            RebarHookOrientation startOrientation = source.GetHookOrientation(0);
            RebarHookOrientation endOrientation = source.GetHookOrientation(1);

            var replacements = new List<Rebar>();
            for (int i = 0; i < count; i++)
            {
                IList<Curve> curves = source.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeAllMultiplanarCurves, i);
                if (curves == null || curves.Count == 0) throw new InvalidOperationException("Không đọc được đường cong ở vị trí " + (i + 1) + ".");
                Rebar single = Rebar.CreateFromCurves(doc, shape.RebarStyle, type,
                    startHook, endHook, host, normal, curves,
                    startOrientation, endOrientation, true, true);
                if (single == null) throw new InvalidOperationException("Revit không tạo được thanh số " + (i + 1) + ".");
                CopyMetadata(source, single);
                replacements.Add(single);
            }
            doc.Delete(source.Id);
            return replacements.Count;
        }

        private static RebarHookType GetHook(Document doc, Rebar rebar, int end)
        {
            ElementId id = rebar.GetHookTypeId(end);
            return id == null || id == ElementId.InvalidElementId ? null : doc.GetElement(id) as RebarHookType;
        }

        private static void CopyMetadata(Rebar source, Rebar target)
        {
            target.ScheduleMark = source.ScheduleMark;
            Parameter sourcePartition = source.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            Parameter targetPartition = target.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (sourcePartition != null && targetPartition != null && !targetPartition.IsReadOnly && sourcePartition.HasValue)
                targetPartition.Set(sourcePartition.AsString());
        }
    }
}
