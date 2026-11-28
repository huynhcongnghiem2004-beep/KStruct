using System;
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
            return ProjectToView(view, anchor) + view.RightDirection.Normalize() * (side * gapMm / 304.8);
        }

        /// <summary>Requires an open Revit transaction and a valid reference.
        /// Anchor is supplied by the caller at the desired physical bar position.
        /// Gap is model millimetres, not paper millimetres.</summary>
        public static IndependentTag CreateHorizontal(Document doc, View view, FamilySymbol symbol,
            Reference reference, XYZ anchor, double gapMm = 200, int fallback = 1)
        {
            if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
            anchor = ProjectToView(view, anchor);
            XYZ head = HeadPosition(view, symbol, anchor, gapMm, fallback);
            var tag = IndependentTag.Create(doc, symbol.Id, view.Id, reference, true, TagOrientation.Horizontal, anchor);
            tag.LeaderEndCondition = LeaderEndCondition.Free;
            tag.SetLeaderEnd(reference, anchor);
            tag.TagHeadPosition = head;
            doc.Regenerate();
            tag.SetLeaderEnd(reference, anchor);
            tag.SetLeaderElbow(reference, (anchor + head) / 2);
            tag.TagHeadPosition = head;
            return tag;
        }
    }
}
