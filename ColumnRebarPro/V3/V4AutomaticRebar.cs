using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    // Automatic Shape Driven Rebar using ONLY existing RebarShapes in the project.
    // Revit chooses a compatible Shape from the supplied design curves and Hook types.
    // Never create a new Shape and never silently convert to FreeForm.
    internal static class V4AutomaticRebar
    {
        internal static Rebar Create(Document doc, RebarStyle style, RebarBarType type,
            Element host, XYZ normal, IList<Curve> curves,
            RebarHookType startHook, RebarHookType endHook,
            ElementId columnId, string group)
        {
            if (doc == null || type == null || host == null || normal == null ||
                normal.GetLength() < 1e-6 || curves == null || curves.Count == 0)
                throw new InvalidOperationException("Cột " + columnId.Value +
                    ": thiếu dữ liệu tạo hình học " + group + ".");
            if ((startHook != null && !type.GetHookPermission(startHook.Id)) ||
                (endHook != null && !type.GetHookPermission(endHook.Id)))
                throw new InvalidOperationException("Cột " + columnId.Value +
                    ": RebarBarType không cho phép loại Hook được chọn cho " + group + ".");

            var orientations = startHook == null && endHook == null
                ? new[] { Tuple.Create(RebarHookOrientation.Right, RebarHookOrientation.Right) }
                : new[] {
                    Tuple.Create(RebarHookOrientation.Right, RebarHookOrientation.Right),
                    Tuple.Create(RebarHookOrientation.Right, RebarHookOrientation.Left),
                    Tuple.Create(RebarHookOrientation.Left, RebarHookOrientation.Right),
                    Tuple.Create(RebarHookOrientation.Left, RebarHookOrientation.Left)
                };
            string lastError = null;
            foreach (var orientation in orientations)
            {
                using (var attempt = new SubTransaction(doc))
                {
                    attempt.Start();
                    try
                    {
                        Rebar bar = Rebar.CreateFromCurves(
                            doc, style, type, startHook, endHook, host, normal,
                            curves, orientation.Item1, orientation.Item2,
                            useExistingShapeIfPossible: true, createNewShape: false);
                        if (bar == null) { attempt.RollBack(); continue; }
                        doc.Regenerate();
                        var shape = doc.GetElement(bar.GetShapeId()) as RebarShape;
                        if (shape == null || shape.RebarStyle != style || !shape.GetAllowed(type) ||
                            bar.GetHookTypeId(0) != (startHook?.Id ?? ElementId.InvalidElementId) ||
                            bar.GetHookTypeId(1) != (endHook?.Id ?? ElementId.InvalidElementId))
                        {
                            attempt.RollBack();
                            continue;
                        }
                        if (attempt.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Revit không commit được thanh thép.");
                        return bar;
                    }
                    catch (Exception ex)
                    {
                        if (attempt.GetStatus() == TransactionStatus.Started) attempt.RollBack();
                        lastError = ex.Message;
                    }
                }
            }
            throw new InvalidOperationException("Cột " + columnId.Value +
                ": Revit không khớp được RebarShape hiện có cho " + group +
                " từ đường thanh và Hook trong project. Không tạo Shape mới. " +
                "Hãy kiểm tra thư viện RebarShape của project, RebarBarType và " +
                "Include hooks in Rebar Shape definition." +
                (string.IsNullOrEmpty(lastError) ? "" : " Chi tiết: " + lastError));
        }

        internal static IList<Curve> Lines(params XYZ[] points)
        {
            if (points == null || points.Length < 2)
                throw new ArgumentException("Cần ít nhất hai điểm dựng thép.");
            var curves = new List<Curve>();
            for (int i = 1; i < points.Length; i++)
                curves.Add(Line.CreateBound(points[i - 1], points[i]));
            return curves;
        }
    }
}
