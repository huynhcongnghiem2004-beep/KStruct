using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace KStruct.Core
{
    public static partial class RebarTagPlacement
    {
        public static XYZ ProjectToView(View view, XYZ point)
        {
            XYZ normal = view.ViewDirection.Normalize();
            return point - normal * (point - view.Origin).DotProduct(normal);
        }

        public static XYZ HeadPosition(View view, FamilySymbol symbol, XYZ anchor, double gapMm, int fallback = 1)
        {
            if (gapMm <= 0 || double.IsNaN(gapMm) || double.IsInfinity(gapMm))
                throw new ArgumentOutOfRangeException(nameof(gapMm));
            int side = ResolveSide(symbol.FamilyName, symbol.Name, fallback);
            return ProjectToView(view, anchor) + view.RightDirection.Normalize() *
                (side * PaperMillimetres(view, gapMm));
        }

        public static XYZ AvoidVerticalCollision(View view, XYZ desiredHead,
            IEnumerable<XYZ> occupiedHeads, double paperSpacingMm = 5.0)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (desiredHead == null) throw new ArgumentNullException(nameof(desiredHead));
            if (occupiedHeads == null) throw new ArgumentNullException(nameof(occupiedHeads));
            XYZ head = ProjectToView(view, desiredHead);
            XYZ right = view.RightDirection.Normalize();
            XYZ up = view.UpDirection.Normalize();
            double u = head.DotProduct(right);
            double spacing = PaperMillimetres(view, paperSpacingMm);
            bool moved;
            do
            {
                moved = false;
                foreach (XYZ occupied in occupiedHeads)
                {
                    if (occupied == null) continue;
                    XYZ other = ProjectToView(view, occupied);
                    if (Math.Abs(other.DotProduct(right) - u) > spacing * 0.5) continue;
                    double delta = head.DotProduct(up) - other.DotProduct(up);
                    if (Math.Abs(delta) < spacing)
                    {
                        head += up * (spacing - delta);
                        moved = true;
                    }
                }
            } while (moved);
            return head;
        }

        public static double PaperMillimetres(View view, double paperMm)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (paperMm <= 0 || double.IsNaN(paperMm) || double.IsInfinity(paperMm))
                throw new ArgumentOutOfRangeException(nameof(paperMm));
            return UnitUtils.ConvertToInternalUnits(paperMm * view.Scale, UnitTypeId.Millimeters);
        }

        public static XYZ AlignedHead(View view, XYZ anchor, double columnMinU, double columnMaxU,
            double paperOffsetMm, int side)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            if (double.IsNaN(columnMinU) || double.IsInfinity(columnMinU) ||
                double.IsNaN(columnMaxU) || double.IsInfinity(columnMaxU) || columnMaxU <= columnMinU)
                throw new ArgumentOutOfRangeException(nameof(columnMaxU));
            XYZ onView = ProjectToView(view, anchor);
            XYZ right = view.RightDirection.Normalize();
            double anchorU = onView.DotProduct(right);
            double targetU = side < 0
                ? columnMinU - PaperMillimetres(view, paperOffsetMm)
                : columnMaxU + PaperMillimetres(view, paperOffsetMm);
            return onView + right * (targetU - anchorU);
        }

        public static void ApplyHorizontalFreeEnd(Document doc, View view, IndependentTag tag,
            Reference reference, XYZ anchor, XYZ head, double finalHorizontalPaperMm = 8.0)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            if (head == null) throw new ArgumentNullException(nameof(head));
            if (!doc.IsModifiable) throw new InvalidOperationException("An active transaction is required.");
            tag.HasLeader = true;
            if (!tag.CanLeaderEndConditionBeAssigned(LeaderEndCondition.Free))
                throw new InvalidOperationException("The selected Rebar Tag does not support a Free End leader.");

            XYZ right = view.RightDirection.Normalize();
            XYZ up = view.UpDirection.Normalize();
            anchor = ProjectToView(view, anchor);
            head = ProjectToView(view, head);
            tag.LeaderEndCondition = LeaderEndCondition.Free;
            tag.SetLeaderEnd(reference, anchor);
            tag.TagHeadPosition = head;
            doc.Regenerate();

            double deltaV = (head - anchor).DotProduct(up);
            if (Math.Abs(deltaV) > 1e-8)
            {
                double deltaU = (head - anchor).DotProduct(right);
                double sign = deltaU < 0 ? -1.0 : 1.0;
                double finalLength = Math.Min(Math.Abs(deltaU) * 0.5,
                    PaperMillimetres(view, finalHorizontalPaperMm));
                XYZ elbow = head - right * (sign * finalLength);
                tag.SetLeaderElbow(reference, elbow);
            }
            tag.SetLeaderEnd(reference, anchor);
            tag.TagHeadPosition = head;
        }

        /// <summary>Requires an open Revit transaction and a valid reference.
        /// Anchor is supplied by the caller at the desired physical bar position.
        /// Gap is paper millimetres and is scaled for the target view.</summary>
        public static IndependentTag CreateHorizontal(Document doc, View view, FamilySymbol symbol,
            Reference reference, XYZ anchor, double gapMm = 15, int fallback = 1)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (symbol == null) throw new ArgumentNullException(nameof(symbol));
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            if (!doc.IsModifiable) throw new InvalidOperationException("An active transaction is required.");
            if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
            anchor = ProjectToView(view, anchor);
            int side = ResolveSide(symbol.FamilyName, symbol.Name, fallback);
            XYZ head = anchor + view.RightDirection.Normalize() * (side * PaperMillimetres(view, gapMm));
            var tag = IndependentTag.Create(doc, symbol.Id, view.Id, reference, true, TagOrientation.Horizontal, anchor);
            if (tag == null) throw new InvalidOperationException("Cannot create Rebar Tag.");
            ApplyHorizontalFreeEnd(doc, view, tag, reference, anchor, head);
            return tag;
        }
    }
}
