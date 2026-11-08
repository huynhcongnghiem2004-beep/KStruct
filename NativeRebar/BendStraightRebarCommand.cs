using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class BendStraightRebarCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class SetLayout
        {
            public RebarLayoutRule Rule;
            public int PositionCount;
            public double ArrayLength;
            public double Spacing;
            public bool BarsOnNormalSide;
            public bool IncludeFirstBar;
            public bool IncludeLastBar;
            public List<int> ExcludedPositions = new List<int>();
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Rebar> selection = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().ToList();
                Rebar source;
                if (selection.Count == 1) source = selection[0];
                else if (selection.Count > 1)
                {
                    TaskDialog.Show("KStruct - Bẻ thép", "Chọn đúng một Rebar đơn để tạo đoạn bẻ.");
                    return Result.Cancelled;
                }
                else
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new RebarFilter(), "Chọn một Rebar đơn cần tạo đoạn bẻ");
                    source = doc.GetElement(picked) as Rebar;
                }
                if (source == null) return Result.Cancelled;
                bool isFreeForm = source.IsRebarFreeForm();
                if (isFreeForm && source.NumberOfBarPositions != 1)
                    throw new InvalidOperationException("FreeForm set nhiều vị trí chưa được xử lý an toàn; lệnh chỉ hỗ trợ một thanh FreeForm.");
                RebarShape sourceShape = doc.GetElement(source.GetShapeId()) as RebarShape;
                RebarStyle style = sourceShape == null ? RebarStyle.Standard : sourceShape.RebarStyle;
                if (style != RebarStyle.Standard && style != RebarStyle.StirrupTie)
                    throw new InvalidOperationException("Lệnh chỉ hỗ trợ Rebar Standard hoặc StirrupTie có hình học phẳng.");
                SetLayout layout = isFreeForm ? null : ReadLayout(source);

                IList<Curve> original = source.GetCenterlineCurves(false, true, false,
                    isFreeForm ? MultiplanarOption.IncludeAllMultiplanarCurves : MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                if (original == null || original.Count == 0 || original.Any(curve => curve == null))
                    throw new InvalidOperationException("Không đọc được đường tim Rebar.");
                if (isFreeForm && !IsPlanar(original, doc.Application.ShortCurveTolerance * 2))
                    throw new InvalidOperationException("FreeForm nhiều mặt phẳng chưa được hỗ trợ; thanh không bị thay đổi.");
                List<Line> straightSegments = original.OfType<Line>().Where(line => line.Length > doc.Application.ShortCurveTolerance).ToList();
                if (straightSegments.Count == 0)
                    throw new InvalidOperationException("Hình dạng Rebar không có đoạn thẳng để chọn vị trí bẻ.");

                XYZ firstPick = ui.Selection.PickPoint("Chọn điểm đầu của đoạn thẳng cần bẻ");
                XYZ secondPick = ui.Selection.PickPoint("Chọn điểm cuối của đoạn thẳng cần bẻ");
                var candidate = straightSegments.Select(line =>
                {
                    double stationA = (firstPick - line.GetEndPoint(0)).DotProduct(line.Direction);
                    double stationB = (secondPick - line.GetEndPoint(0)).DotProduct(line.Direction);
                    double offA = firstPick.DistanceTo(line.GetEndPoint(0) + line.Direction * stationA);
                    double offB = secondPick.DistanceTo(line.GetEndPoint(0) + line.Direction * stationB);
                    return new { Line = line, StationA = stationA, StationB = stationB, Score = offA + offB };
                }).Where(item => item.StationA >= -doc.Application.ShortCurveTolerance &&
                    item.StationA <= item.Line.Length + doc.Application.ShortCurveTolerance &&
                    item.StationB >= -doc.Application.ShortCurveTolerance &&
                    item.StationB <= item.Line.Length + doc.Application.ShortCurveTolerance)
                    .OrderBy(item => item.Score).FirstOrDefault();
                double maxPickOffset = UnitUtils.ConvertToInternalUnits(250, UnitTypeId.Millimeters);
                if (candidate == null || candidate.Score > 2 * maxPickOffset)
                    throw new InvalidOperationException("Hai điểm chọn phải nằm trên cùng một đoạn thẳng của Rebar.");
                Line selectedLine = candidate.Line;
                double stationA = candidate.StationA;
                double stationB = candidate.StationB;
                if (stationB < stationA) { double temp = stationA; stationA = stationB; stationB = temp; }
                XYZ direction = selectedLine.Direction.Normalize();
                double tolerance = doc.Application.ShortCurveTolerance;
                if (stationA <= tolerance || stationB >= selectedLine.Length - tolerance || stationB - stationA <= tolerance)
                    throw new InvalidOperationException("Hai giới hạn phải nằm bên trong đoạn thẳng đã chọn và cách nhau đủ xa.");

                XYZ pointA = selectedLine.GetEndPoint(0) + direction * stationA;
                XYZ pointB = selectedLine.GetEndPoint(0) + direction * stationB;
                XYZ target = ui.Selection.PickPoint("Chọn vị trí mới cho điểm bẻ đầu tiên (giữ trong mặt phẳng Rebar)");
                XYZ rawOffset = target - pointA;
                XYZ offset = rawOffset - direction * rawOffset.DotProduct(direction);
                XYZ planeNormal = FindPlaneNormal(original, selectedLine, direction);
                if (planeNormal != null && planeNormal.GetLength() > 1e-8)
                    offset -= planeNormal * offset.DotProduct(planeNormal);
                if (offset.GetLength() <= tolerance)
                    throw new InvalidOperationException("Điểm mới phải lệch khỏi trục và nằm trong mặt phẳng thanh để tạo đoạn bẻ.");

                XYZ offsetA = pointA + offset;
                XYZ offsetB = pointB + offset;
                var curves = new List<Curve>();
                foreach (Curve curve in original)
                {
                    if (!ReferenceEquals(curve, selectedLine))
                    {
                        curves.Add(curve);
                        continue;
                    }
                    XYZ segmentStart = selectedLine.GetEndPoint(0);
                    XYZ segmentEnd = selectedLine.GetEndPoint(1);
                    curves.Add(Line.CreateBound(segmentStart, pointA));
                    curves.Add(Line.CreateBound(pointA, offsetA));
                    curves.Add(Line.CreateBound(offsetA, offsetB));
                    curves.Add(Line.CreateBound(offsetB, pointB));
                    curves.Add(Line.CreateBound(pointB, segmentEnd));
                }
                if (planeNormal == null || planeNormal.GetLength() < 1e-8)
                    planeNormal = direction.CrossProduct(offset);
                planeNormal = planeNormal.Normalize();
                Element host = doc.GetElement(source.GetHostId());
                RebarBarType barType = doc.GetElement(source.GetTypeId()) as RebarBarType;
                if (host == null || barType == null) throw new InvalidOperationException("Không đọc được host hoặc RebarBarType của thanh.");
                RebarHookType startHook = isFreeForm ? null : GetHook(doc, source, 0);
                RebarHookType endHook = isFreeForm ? null : GetHook(doc, source, 1);
                RebarHookOrientation startOrientation = isFreeForm ? RebarHookOrientation.Left : source.GetHookOrientation(0);
                RebarHookOrientation endOrientation = isFreeForm ? RebarHookOrientation.Right : source.GetHookOrientation(1);

                ElementId replacementId;
                using (Transaction tx = new Transaction(doc, "KStruct - Bẻ Rebar thẳng"))
                {
                    tx.Start();
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            if (isFreeForm)
                            {
                                using (RebarFreeFormAccessor accessor = source.GetFreeFormAccessor())
                                {
                                    if (!accessor.IsUnconstrained())
                                        throw new InvalidOperationException("FreeForm đang bị ràng buộc; hãy giải phóng constraint trước khi bẻ.");
                                    RebarFreeFormValidationResult validation = accessor.SetCurves(
                                        new List<IList<Curve>> { curves.Cast<Curve>().ToList() });
                                    if (validation != RebarFreeFormValidationResult.Success)
                                        throw new InvalidOperationException("Revit từ chối hình dạng FreeForm mới: " + validation + ".");
                                }
                                replacementId = source.Id;
                            }
                            else
                            {
                                Rebar replacement = NativeRebarFactory.CreateFromCurves(doc, style, barType,
                                    startHook, endHook, host, planeNormal, curves, startOrientation,
                                    endOrientation, true, true);
                                if (replacement == null) throw new InvalidOperationException("Revit không tạo được Rebar theo đường bẻ mới.");
                                ApplyLayout(replacement, layout);
                                foreach (int position in layout.ExcludedPositions)
                                    replacement.SetBarIncluded(false, position);
                                CopyTextParameter(source, replacement, BuiltInParameter.NUMBER_PARTITION_PARAM);
                                CopyTextParameter(source, replacement, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                doc.Delete(source.Id);
                                replacementId = replacement.Id;
                            }
                            if (sub.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Revit không chấp nhận thay đổi hình dạng Rebar.");
                        }
                        catch
                        {
                            if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                            throw;
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi hình dạng Rebar.");
                }
                ui.Selection.SetElementIds(new List<ElementId> { replacementId });
                TaskDialog.Show("KStruct - Bẻ thép", isFreeForm
                    ? "Đã cập nhật đường tim của Rebar FreeForm đơn; element và host được giữ nguyên. Kiểm tra lại cover, chiều dài neo và hình dạng trước khi phát hành hồ sơ."
                    : "Đã thay Rebar bằng hình dạng có đoạn bẻ. Lệnh giữ các đoạn cong và móc đang có; kiểm tra lại cover, chiều dài neo và hình dạng trước khi phát hành hồ sơ.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Bẻ thép", ex.Message);
                return Result.Failed;
            }
        }

        private static void CopyTextParameter(Rebar source, Rebar target, BuiltInParameter parameterId)
        {
            Parameter original = source.get_Parameter(parameterId);
            Parameter replacement = target.get_Parameter(parameterId);
            if (original != null && replacement != null && original.StorageType == StorageType.String &&
                !replacement.IsReadOnly && original.HasValue)
                replacement.Set(original.AsString());
        }

        private static RebarHookType GetHook(Document doc, Rebar rebar, int end)
        {
            ElementId id = rebar.GetHookTypeId(end);
            return id == null || id == ElementId.InvalidElementId ? null : doc.GetElement(id) as RebarHookType;
        }

        private static XYZ FindPlaneNormal(IList<Curve> curves, Line selectedLine, XYZ direction)
        {
            Arc arc = curves.OfType<Arc>().FirstOrDefault();
            if (arc != null) return arc.Normal.Normalize();
            XYZ origin = selectedLine.GetEndPoint(0);
            foreach (Curve curve in curves)
            {
                XYZ vector = curve.GetEndPoint(1) - curve.GetEndPoint(0);
                XYZ normal = direction.CrossProduct(vector);
                if (normal.GetLength() > 1e-8) return normal.Normalize();
                normal = direction.CrossProduct(curve.GetEndPoint(0) - origin);
                if (normal.GetLength() > 1e-8) return normal.Normalize();
            }
            return null;
        }

        private static bool IsPlanar(IList<Curve> curves, double tolerance)
        {
            List<XYZ> points = curves.SelectMany(curve => curve.Tessellate()).ToList();
            if (points.Count < 3) return true;
            XYZ origin = points[0];
            XYZ normal = null;
            for (int i = 1; i < points.Count && normal == null; i++)
            for (int j = i + 1; j < points.Count && normal == null; j++)
            {
                XYZ candidate = (points[i] - origin).CrossProduct(points[j] - origin);
                if (candidate.GetLength() > tolerance * tolerance) normal = candidate.Normalize();
            }
            return normal == null || points.All(point => Math.Abs((point - origin).DotProduct(normal)) <= tolerance);
        }

        private static SetLayout ReadLayout(Rebar rebar)
        {
            var layout = new SetLayout
            {
                Rule = rebar.LayoutRule,
                PositionCount = rebar.NumberOfBarPositions,
                IncludeFirstBar = rebar.IncludeFirstBar,
                IncludeLastBar = rebar.IncludeLastBar
            };
            for (int position = 0; position < layout.PositionCount; position++)
                if (!rebar.DoesBarExistAtPosition(position)) layout.ExcludedPositions.Add(position);
            using (RebarShapeDrivenAccessor accessor = rebar.GetShapeDrivenAccessor())
            {
                layout.ArrayLength = accessor.ArrayLength;
                layout.BarsOnNormalSide = accessor.BarsOnNormalSide;
            }
            Parameter spacing = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_BAR_SPACING);
            if (spacing != null && spacing.HasValue) layout.Spacing = spacing.AsDouble();
            return layout;
        }

        private static void ApplyLayout(Rebar rebar, SetLayout layout)
        {
            using (RebarShapeDrivenAccessor accessor = rebar.GetShapeDrivenAccessor())
            {
                switch (layout.Rule)
                {
                    case RebarLayoutRule.Single:
                        accessor.SetLayoutAsSingle();
                        return;
                    case RebarLayoutRule.FixedNumber:
                        accessor.SetLayoutAsFixedNumber(layout.PositionCount, layout.ArrayLength,
                            layout.BarsOnNormalSide, layout.IncludeFirstBar, layout.IncludeLastBar);
                        return;
                    case RebarLayoutRule.MaximumSpacing:
                        accessor.SetLayoutAsMaximumSpacing(rebar.MaxSpacing, layout.ArrayLength,
                            layout.BarsOnNormalSide, layout.IncludeFirstBar, layout.IncludeLastBar);
                        return;
                    case RebarLayoutRule.NumberWithSpacing:
                        if (layout.Spacing <= 0) throw new InvalidOperationException("Không đọc được khoảng cách của Rebar set.");
                        accessor.SetLayoutAsNumberWithSpacing(layout.PositionCount, layout.Spacing,
                            layout.BarsOnNormalSide, layout.IncludeFirstBar, layout.IncludeLastBar);
                        return;
                    case RebarLayoutRule.MinimumClearSpacing:
                        if (layout.Spacing <= 0) throw new InvalidOperationException("Không đọc được khoảng cách thông thủy của Rebar set.");
                        accessor.SetLayoutAsMinimumClearSpacing(layout.Spacing, layout.ArrayLength,
                            layout.BarsOnNormalSide, layout.IncludeFirstBar, layout.IncludeLastBar);
                        return;
                    default:
                        throw new InvalidOperationException("Quy tắc phân bố Rebar set này chưa được hỗ trợ an toàn.");
                }
            }
        }
    }
}
