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
    public sealed class SplitStraightRebarCommand : IExternalCommand
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
                List<Rebar> selected = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (selected.Count == 0)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new RebarFilter(), "Chọn một thanh Rebar thẳng cần chia");
                    selected.Add(doc.GetElement(picked) as Rebar);
                }
                if (selected.Count != 1)
                {
                    TaskDialog.Show("KStruct - Chia Rebar", "Lệnh này xử lý một Rebar mỗi lần. Hãy chỉ chọn một thanh.");
                    return Result.Cancelled;
                }
                Rebar source = selected[0];
                if (source == null || source.IsRebarFreeForm() || source.NumberOfBarPositions != 1)
                {
                    TaskDialog.Show("KStruct - Chia Rebar", "Hiện chỉ hỗ trợ một thanh đơn, không phải free-form hoặc Rebar set.");
                    return Result.Cancelled;
                }
                IList<Curve> curves = source.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeAllMultiplanarCurves, 0);
                if (curves == null || curves.Count != 1 || !(curves[0] is Line axis))
                {
                    TaskDialog.Show("KStruct - Chia Rebar", "Hiện chỉ hỗ trợ Rebar có một đoạn thẳng. Thanh cong nhiều đoạn không bị thay đổi.");
                    return Result.Cancelled;
                }
                XYZ a = axis.GetEndPoint(0), b = axis.GetEndPoint(1);
                XYZ direction = (b - a).Normalize();
                XYZ cutPick = ui.Selection.PickPoint("Chọn vị trí chia thanh thép");
                double parameter = (cutPick - a).DotProduct(direction);
                double minSegment = Math.Max(doc.Application.ShortCurveTolerance * 10, UnitUtils.ConvertToInternalUnits(25, UnitTypeId.Millimeters));
                double length = a.DistanceTo(b);
                if (parameter <= minSegment || parameter >= length - minSegment)
                {
                    TaskDialog.Show("KStruct - Chia Rebar", "Điểm chia phải cách hai đầu thanh ít nhất 25 mm.");
                    return Result.Cancelled;
                }
                XYZ cut = a + direction * parameter;

                Element host = doc.GetElement(source.GetHostId());
                RebarBarType barType = doc.GetElement(source.GetTypeId()) as RebarBarType;
                if (host == null || barType == null) throw new InvalidOperationException("Không đọc được host hoặc RebarBarType của thanh.");
                RebarHookType startHook = GetHook(doc, source, 0);
                RebarHookType endHook = GetHook(doc, source, 1);
                RebarHookOrientation startOrientation = source.GetHookOrientation(0);
                RebarHookOrientation endOrientation = source.GetHookOrientation(1);
                XYZ normal = direction.CrossProduct(XYZ.BasisZ);
                if (normal.GetLength() < 1e-6) normal = direction.CrossProduct(XYZ.BasisY);
                normal = normal.Normalize();

                using (Transaction tx = new Transaction(doc, "KStruct - Chia thanh Rebar"))
                {
                    tx.Start();
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            Rebar first = Rebar.CreateFromCurves(doc, RebarStyle.Standard, barType,
                                startHook, null, host, normal, new List<Curve> { Line.CreateBound(a, cut) },
                                startOrientation, endOrientation, true, true);
                            Rebar second = Rebar.CreateFromCurves(doc, RebarStyle.Standard, barType,
                                null, endHook, host, normal, new List<Curve> { Line.CreateBound(cut, b) },
                                startOrientation, endOrientation, true, true);
                            if (first == null || second == null) throw new InvalidOperationException("Revit không tạo được cả hai đoạn thép.");
                            CopyMetadata(source, first);
                            CopyMetadata(source, second);
                            doc.Delete(source.Id);
                            if (sub.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Revit không chấp nhận thao tác chia.");
                        }
                        catch
                        {
                            if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                            throw;
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận hai thanh mới.");
                }
                TaskDialog.Show("KStruct - Chia Rebar", "Đã thay thanh gốc bằng hai đoạn tại điểm đã chọn.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Chia Rebar", ex.Message);
                return Result.Failed;
            }
        }

        private static RebarHookType GetHook(Document doc, Rebar rebar, int end)
        {
            ElementId id = rebar.GetHookTypeId(end);
            return id == null || id == ElementId.InvalidElementId ? null : doc.GetElement(id) as RebarHookType;
        }

        private static void CopyMetadata(Rebar source, Rebar target)
        {
            target.ScheduleMark = source.ScheduleMark;
            Parameter partition = source.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            Parameter targetPartition = target.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (partition != null && targetPartition != null && !targetPartition.IsReadOnly && partition.HasValue)
                targetPartition.Set(partition.AsString());
        }
    }
}
