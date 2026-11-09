using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    // Controlled Shape Driven rebar creation. RebarShape creation is opt-in for
    // anchor/transition bars; never silently create a Shape for ties/straight bars.
    internal static class V4AutomaticRebar
    {
        internal static Rebar Create(Document doc, RebarStyle style, RebarBarType type,
            Element host, XYZ normal, IList<Curve> curves,
            RebarHookType startHook, RebarHookType endHook,
            ElementId columnId, string group, bool allowNewShape = false)
        {
            if (doc == null || type == null || host == null || normal == null ||
                normal.GetLength() < 1e-6 || curves == null || curves.Count == 0)
                throw new InvalidOperationException("Cột " + columnId.Value +
                    ": thiếu dữ liệu tạo hình học " + group + ".");
            ValidateGeometry(curves, normal, doc.Application.ShortCurveTolerance, columnId, group);
            if ((startHook != null && !type.GetHookPermission(startHook.Id)) ||
                (endHook != null && !type.GetHookPermission(endHook.Id)))
                throw new InvalidOperationException("Cột " + columnId.Value +
                    ": RebarBarType không cho phép Hook cho " + group + ".");

            var orientations = startHook == null && endHook == null
                ? new[] { Tuple.Create(RebarHookOrientation.Right, RebarHookOrientation.Right) }
                : new[] {
                    Tuple.Create(RebarHookOrientation.Right, RebarHookOrientation.Right),
                    Tuple.Create(RebarHookOrientation.Right, RebarHookOrientation.Left),
                    Tuple.Create(RebarHookOrientation.Left, RebarHookOrientation.Right),
                    Tuple.Create(RebarHookOrientation.Left, RebarHookOrientation.Left)
                };

            // Revit 2024 can throw InternalException when no matching shape is
            // available with createNewShape:false (Autodesk REVIT-217050).
            // Always isolate the initial probe; only a validated anchor may retry
            // in a fresh SubTransaction with createNewShape:true.
            string lastError = null;
            foreach (bool createNew in allowNewShape ? new[] { false, true } : new[] { false })
            {
                foreach (var orientation in orientations)
                {
                    using (var attempt = new SubTransaction(doc))
                    {
                        attempt.Start();
                        try
                        {
                            HashSet<int> knownShapeIds = null;
                            if (createNew)
                                knownShapeIds = new HashSet<int>(
                                    new FilteredElementCollector(doc)
                                        .OfClass(typeof(RebarShape))
                                        .Select(shape => shape.Id.Value));

                            Rebar bar = Rebar.CreateFromCurves(
                                doc, style, type, startHook, endHook, host, normal,
                                curves, orientation.Item1, orientation.Item2,
                                useExistingShapeIfPossible: true, createNewShape: createNew);
                            if (bar == null) { attempt.RollBack(); continue; }

                            doc.Regenerate();
                            var shape = doc.GetElement(bar.GetShapeId()) as RebarShape;
                            if (shape == null || shape.RebarStyle != style ||
                                !shape.GetAllowed(type) ||
                                bar.GetHookTypeId(0) != (startHook?.Id ?? ElementId.InvalidElementId) ||
                                bar.GetHookTypeId(1) != (endHook?.Id ?? ElementId.InvalidElementId))
                            {
                                attempt.RollBack();
                                lastError = "Rebar Shape/Hook tạo ra không đúng cấu hình.";
                                continue;
                            }

                            if (createNew && !knownShapeIds.Contains(shape.Id.Value))
                                V4GeneratedShapeOwnership.Stamp(shape, columnId, group);

                            if (attempt.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Revit không commit được thanh thép.");
                            return bar;
                        }
                        catch (Exception ex)
                        {
                            if (attempt.GetStatus() == TransactionStatus.Started)
                                attempt.RollBack();
                            lastError = ex.GetType().Name + ": " + ex.Message;
                        }
                    }
                }
            }

            throw new InvalidOperationException("Cột " + columnId.Value +
                ": không dựng được " + group + " với RebarShape trong project" +
                (allowNewShape ? " hoặc Shape tự sinh có kiểm soát" : "") + ". " +
                "Kiểm tra Curve, RebarBarType, host và thư viện Shape. " +
                "Mọi lần thử thất bại đều rollback." +
                (string.IsNullOrEmpty(lastError) ? "" : " Chi tiết: " + lastError));
        }

        private static void ValidateGeometry(IList<Curve> curves, XYZ normal,
            double shortTolerance, ElementId columnId, string group)
        {
            XYZ n = normal.Normalize();
            XYZ origin = curves[0].GetEndPoint(0);
            double tol = Math.Max(shortTolerance, 1e-6);
            for (int i = 0; i < curves.Count; i++)
            {
                var c = curves[i];
                if (c == null || !c.IsBound || !(c is Line || c is Arc) ||
                    c.Length <= tol)
                    throw new InvalidOperationException("Cột " + columnId.Value +
                        ": đường thanh " + group + " có đoạn không hợp lệ hoặc quá ngắn.");
                XYZ start = c.GetEndPoint(0), end = c.GetEndPoint(1);
                if (Math.Abs((start-origin).DotProduct(n)) > tol ||
                    Math.Abs((end-origin).DotProduct(n)) > tol)
                    throw new InvalidOperationException("Cột " + columnId.Value +
                        ": " + group + " không đồng phẳng với pháp tuyến.");
                if (i > 0 && curves[i-1].GetEndPoint(1).DistanceTo(start) > tol)
                    throw new InvalidOperationException("Cột " + columnId.Value +
                        ": đường thanh " + group + " bị đứt đoạn.");
            }
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
