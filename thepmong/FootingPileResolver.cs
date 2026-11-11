using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // Authoritative set of modeled piles associated with one physical cap.
    // All plan tags and section detail graphics must start from this same set.
    internal static class FootingPileResolver
    {
        internal static IList<FamilyInstance> Resolve(Document doc, FamilyInstance cap, string filter, IList<string> warnings)
        {
            if (doc == null || cap == null) throw new ArgumentNullException(doc == null ? "doc" : "cap");
            BoundingBoxXYZ local = FootingDrawingGeometry.LocalBounds(cap);
            Transform transform = local.Transform;
            double tolerance = UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters);
            double capBottom = transform.OfPoint(new XYZ(0, 0, local.Min.Z)).Z;
            var matches = new List<FamilyInstance>();
            foreach (FamilyInstance pile in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(p => p.Id.Value != cap.Id.Value).OrderBy(p => p.Id.Value))
            {
                if (!MatchesPileFamily(pile, filter)) continue;
                var location = pile.Location as LocationPoint;
                if (location == null)
                {
                    warnings?.Add("Cọc " + pile.Id.Value + ": thiếu LocationPoint; không đưa vào hồ sơ.");
                    continue;
                }
                XYZ center = transform.Inverse.OfPoint(location.Point);
                // Test all axes together after reading the real pile extents.
                BoundingBoxXYZ extent = pile.get_BoundingBox(null);
                if (extent == null)
                {
                    warnings?.Add("Cọc " + pile.Id.Value + ": không có BoundingBox; không đưa vào hồ sơ.");
                    continue;
                }
                // Only piles at or beneath this cap; a nearby foundation on another level
                // must not be mistaken for a member of the pile group.
                if (!FootingPileIdentityPolicy.InCap(center.X, center.Y, extent.Min.Z, extent.Max.Z,
                    local.Min.X, local.Max.X, local.Min.Y, local.Max.Y, capBottom, tolerance))
                    continue;
                matches.Add(pile);
            }
            return matches;
        }

        internal static bool MatchesPileFamily(FamilyInstance pile, string filter) =>
            FootingPileIdentityPolicy.Matches(pile.Symbol?.FamilyName, pile.Name, filter);
    }
}
