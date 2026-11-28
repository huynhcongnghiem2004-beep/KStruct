using System;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace KStruct.Core
{
    /// <summary>Shared naming and view-relative placement rules for Revit rebar tags.</summary>
    public static class RebarTagPlacement
    {
        private static int? Read(string name, bool vertical)
        {
            var tokens = Regex.Split(name ?? "", @"[^\p{L}\p{N}]+");
            Func<string,bool> has = token => tokens.Any(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));
            if (vertical)
            {
                if (has("TOP")) return 1;
                if (has("BOT")) return -1;
                if (has("MID")) return 0;
            }
            else
            {
                if (has("P")) return 1;
                if (has("T")) return -1;
            }
            return null;
        }

        public static int ResolveSide(string familyName, string typeName, int fallback = 1)
            => Read(typeName, false) ?? Read(familyName, false) ?? (fallback < 0 ? -1 : 1);

        public static int ResolveVertical(string familyName, string typeName)
            => Read(typeName, true) ?? Read(familyName, true) ?? 0;

        public static double SelectPosition(string familyName, string typeName, double a, double b)
        {
            int v = ResolveVertical(familyName, typeName);
            return v > 0 ? Math.Max(a,b) : v < 0 ? Math.Min(a,b) : (a+b)/2;
        }

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
