using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    // Distributed sets use Multi-Rebar Annotation; single/projected coincident
    // positions use the selected main-bar tag without changing model geometry.
    internal sealed partial class V3DrawingBuilder
    {
        private MultiReferenceAnnotationType FindMultiRebarType(Document doc)
        {
            var chosen = doc.GetElement(Id("CrossMultiRebar")) as MultiReferenceAnnotationType;
            if (chosen != null) return chosen;
            var all = new FilteredElementCollector(doc).OfClass(typeof(MultiReferenceAnnotationType))
                .Cast<MultiReferenceAnnotationType>().ToList();
            return all.FirstOrDefault(t => t.Name.IndexOf("align", StringComparison.OrdinalIgnoreCase) >= 0
                                        || t.Name.IndexOf("aline", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? all.FirstOrDefault();
        }

        private static bool IsStirrupBar(Rebar bar)
        {
            try
            {
                var curves = bar.GetCenterlineCurves(false, true, true,
                    MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                if (curves.Count == 0) return false;
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (var curve in curves)
                    foreach (var point in new[] { curve.GetEndPoint(0), curve.GetEndPoint(1) })
                    {
                        if (point.Z < lo) lo = point.Z;
                        if (point.Z > hi) hi = point.Z;
                    }
                return hi - lo < V3Units.Ft(50);
            }
            catch { return false; }
        }

        private HashSet<ElementId> AnnotateMultiRebar(Document doc, V3SegmentWork work,
            ViewSection view, List<Rebar> bars)
        {
            var covered = new HashSet<ElementId>();
            var type = FindMultiRebarType(doc);
            var configuredMainTag = ActiveTagSymbol(doc, Id("CrossMainTag"));

            var geometry = work.Geometry;
            double scale = view.Scale;
            double tolerance = doc.Application.ShortCurveTolerance;
            XYZ columnCenter = Project(view, geometry.At(0, 0, 0));
            XYZ right = view.RightDirection, up = view.UpDirection;

            foreach (var bar in bars.OrderBy(item => item.Id.Value))
            {
                IList<Curve> first, last;
                var positions = Enumerable.Range(0, bar.NumberOfBarPositions)
                    .Where(bar.DoesBarExistAtPosition).ToList();
                if (positions.Count == 0)
                    throw new InvalidOperationException("Rebar " + bar.Id.Value + " không có vị trí thanh hiện hữu.");
                int firstIndex = positions.First(), lastIndex = positions.Last();
                try
                {
                    first = bar.GetCenterlineCurves(false, true, true,
                        MultiplanarOption.IncludeOnlyPlanarCurves, firstIndex);
                    last = bar.GetCenterlineCurves(false, true, true,
                        MultiplanarOption.IncludeOnlyPlanarCurves, lastIndex);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Không đọc được đường tâm Rebar " + bar.Id.Value + " để tạo MultiTag.", ex);
                }
                if (first.Count == 0 || last.Count == 0)
                    throw new InvalidOperationException("Rebar " + bar.Id.Value + " không có đường tâm để tạo MultiTag.");

                XYZ firstPoint = first[0].GetEndPoint(0), lastPoint = last[0].GetEndPoint(0);
                if (bar.IsRebarShapeDriven())
                {
                    using (var accessor = bar.GetShapeDrivenAccessor())
                    {
                        firstPoint = accessor.GetBarPositionTransform(firstIndex).OfPoint(firstPoint);
                        lastPoint = accessor.GetBarPositionTransform(lastIndex).OfPoint(lastPoint);
                    }
                }
                XYZ a = Project(view, firstPoint);
                XYZ b = Project(view, lastPoint);
                XYZ direction = b - a;
                if (positions.Count == 1 || direction.GetLength() < tolerance || type == null)
                {
                    ElementId tagId = Id("CrossMainTag");
                    if (tagId == ElementId.InvalidElementId && type != null) tagId = type.TagTypeId;
                    var tag = configuredMainTag ?? ActiveTagSymbol(doc, tagId);
                    string error = "Chưa chọn Tag thép dọc cho mặt cắt ngang.";
                    if (tag == null || !TryCrossMainTag(doc, view, tag, bar, a, columnCenter, out error))
                        throw new InvalidOperationException("Không gắn được tag cho Rebar " + bar.Id.Value + ": " + error);
                    covered.Add(bar.Id);
                    continue;
                }
                direction = direction.Normalize();
                XYZ midpoint = (a + b) / 2;
                bool alongUp = Math.Abs(direction.DotProduct(up)) > Math.Abs(direction.DotProduct(right));

                FamilySymbol multiTag = configuredMainTag ?? ActiveTagSymbol(doc, type.TagTypeId);
                V3TagHorizontalSide multiSide = multiTag == null ? V3TagHorizontalSide.Left :
                    V3TagPlacement.Resolve(multiTag.FamilyName, multiTag.Name, V3TagHorizontalSide.Left);
                XYZ sideDirection = right * (multiSide == V3TagHorizontalSide.Right ? 1 : -1);
                XYZ normal, origin;
                double along = (midpoint - columnCenter).DotProduct(alongUp ? up : right);
                if (alongUp)
                {
                    normal = sideDirection;
                    origin = columnCenter + up * along + normal *
                        (geometry.Width / 2 + V3Units.Ft(8 * scale));
                }
                else
                {
                    normal = -up;
                    origin = columnCenter + right * along + normal *
                        (geometry.Depth / 2 + V3Units.Ft(8 * scale));
                }

                try
                {
                    var options = new MultiReferenceAnnotationOptions(type);
                    options.SetElementsToDimension(new List<ElementId> { bar.Id });
                    options.DimensionLineOrigin = origin;
                    options.DimensionLineDirection = direction;
                    options.DimensionPlaneNormal = view.ViewDirection;
                    options.TagHeadPosition = origin + sideDirection * V3Units.Ft(10 * scale);
                    options.TagHasLeader = true;
                    var annotation = MultiReferenceAnnotation.Create(doc, view.Id, options);
                    doc.Regenerate();
                    var child = doc.GetElement(annotation.TagId) as IndependentTag;
                    if (child == null || multiTag == null)
                        throw new InvalidOperationException("Khong tim thay tag thep chu cua Multi-Rebar Annotation.");
                    if (child.GetTypeId() != multiTag.Id) child.ChangeTypeId(multiTag.Id);
                    doc.Regenerate();
                    var refs = child.GetTaggedReferences();
                    if (refs.Count != 1)
                        throw new InvalidOperationException("Khong xac dinh duoc tham chieu leader cua Multi-Rebar Annotation.");
                    ApplyCrossMainLeader(doc, view, child, refs[0], multiTag, origin, columnCenter);
                    covered.Add(bar.Id);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Không tạo được Multi-Rebar Annotation cho Rebar " + bar.Id.Value + ".", ex);
                }
            }
            return covered;
        }

        private static void ApplyCrossMainLeader(Document doc, View view, IndependentTag tag,
            Reference reference, FamilySymbol symbol, XYZ anchor, XYZ center)
        {
            int vertical = V3TagPlacement.ResolveVerticalPosition(symbol.FamilyName, symbol.Name);
            if (vertical == 0) vertical = (anchor - center).DotProduct(view.UpDirection) < 0 ? -1 : 1;
            int side = (int)V3TagPlacement.Resolve(symbol.FamilyName, symbol.Name, V3TagHorizontalSide.Right);
            XYZ elbow = anchor + view.UpDirection.Normalize() * (vertical * V3Units.Ft(8 * view.Scale));
            XYZ head = elbow + view.RightDirection.Normalize() * (side * V3Units.Ft(10 * view.Scale));
            if (!tag.CanLeaderEndConditionBeAssigned(LeaderEndCondition.Free))
                throw new InvalidOperationException("Tag khong ho tro leader Free End.");
            tag.LeaderEndCondition = LeaderEndCondition.Free;
            tag.SetLeaderEnd(reference, anchor);
            tag.TagHeadPosition = head;
            doc.Regenerate();
            tag.SetLeaderEnd(reference, anchor);
            tag.SetLeaderElbow(reference, elbow);
            tag.TagHeadPosition = head;
        }

        private static bool TryCrossMainTag(Document doc, View view, FamilySymbol symbol,
            Rebar bar, XYZ anchor, XYZ center, out string error)
        {
            var references = new List<Reference> { new Reference(bar) };
            try { var sub = bar.GetSubelements().FirstOrDefault(); if (sub != null) references.Add(sub.GetReference()); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { }
            var failures = new List<string>();
            foreach (var reference in references.Where(r => r != null))
            {
                using (var attempt = new SubTransaction(doc))
                {
                    attempt.Start();
                    try
                    {
                        var tag = IndependentTag.Create(doc, symbol.Id, view.Id, reference, true, TagOrientation.Horizontal, anchor);
                        ApplyCrossMainLeader(doc, view, tag, reference, symbol, anchor, center);
                        if (attempt.Commit() == TransactionStatus.Committed) { error = null; return true; }
                        failures.Add("Khong the commit tag.");
                    }
                    catch (Exception ex) { attempt.RollBack(); failures.Add(ex.Message); }
                }
            }
            error = string.Join(" | ", failures);
            return false;
        }
    }
}
