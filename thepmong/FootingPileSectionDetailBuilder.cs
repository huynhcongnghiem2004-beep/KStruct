using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // Draw one owned, view-specific symbol for each physical pile whose footprint intersects
    // the actual Detail-X/Detail-Y cutting plane. Never invent pile positions from spacing.
    internal static class FootingPileSectionDetailBuilder
    {
        internal const string Kind = "FootingPileSection2D";

        internal static void Refresh(Document doc, ViewSection view, FamilyInstance cap,
            FootingDrawingProfile profile, IList<string> warnings)
        {
            var existing = FootingDrawingDetailState.Annotations(view, cap, Kind).OfType<FamilyInstance>().ToList();
            bool pileCap = FootingDrawingLayerPolicy.IsPileCap(profile.FootingKindMode,
                FootingDrawingBuilder.IsPileCapForPolicy(cap));
            if (!profile.CreatePileSectionSymbols || !pileCap)
            {
                if (existing.Count != 0) FootingDrawingDetailState.Clear(view, cap, Kind);
                return;
            }
            if (view.ViewType != ViewType.Detail)
                throw new InvalidOperationException("Ký hiệu Cọc 2D chỉ áp dụng cho View Detail-X/Detail-Y.");
            var symbol = doc.GetElement(profile.PileSectionDetailTypeUniqueId) as FamilySymbol;
            if (symbol == null || symbol.Category?.Id.Value != (long)BuiltInCategory.OST_DetailComponents)
                throw new InvalidOperationException("Chưa chọn Type Detail Item Cọc 2D hợp lệ trong project.");
            if (symbol.Family.FamilyPlacementType != FamilyPlacementType.ViewBased)
                throw new InvalidOperationException("Family Cọc 2D không phải Detail Item đặt theo điểm (ViewBased). Không thể gán đúng vị trí bằng chế độ này.");
            if (view.GetCategoryHidden(new ElementId(BuiltInCategory.OST_DetailComponents)))
                throw new InvalidOperationException("View Detail đang ẩn category Detail Items.");
            if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

            BoundingBoxXYZ capBox = FootingDrawingGeometry.LocalBounds(cap);
            Transform axis = capBox.Transform;
            double xmin = capBox.Min.X, xmax = capBox.Max.X;
            double ymin = capBox.Min.Y, ymax = capBox.Max.Y;
            double capBottom = capBox.Transform.OfPoint(new XYZ(0, 0, capBox.Min.Z)).Z;
            // Revit's view origin and normal define the actual cutting plane, including
            // user-configured offsets and reverse directions. Depth alone is not a cut.
            XYZ normal = view.ViewDirection.Normalize();
            XYZ right = view.RightDirection.Normalize();
            XYZ up = view.UpDirection.Normalize();
            XYZ origin = view.Origin;
            double tol = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
            double shiftX = UnitUtils.ConvertToInternalUnits(profile.PileSymbolOffsetXmm, UnitTypeId.Millimeters);
            double shiftY = UnitUtils.ConvertToInternalUnits(profile.PileSymbolOffsetYmm, UnitTypeId.Millimeters);
            if (double.IsNaN(shiftX) || double.IsInfinity(shiftX) ||
                double.IsNaN(shiftY) || double.IsInfinity(shiftY))
                throw new InvalidOperationException("Offset ký hiệu Cọc 2D phải là số hữu hạn.");

            var piles = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(p => p.Id.Value != cap.Id.Value && IsPile(p))
                .OrderBy(p => p.Id.Value).ToList();
            var targets = new List<Tuple<FamilyInstance, XYZ>>();
            foreach (FamilyInstance pile in piles)
            {
                BoundingBoxXYZ box = pile.get_BoundingBox(null);
                if (box == null) continue;
                XYZ middle = (box.Min + box.Max) * 0.5;
                XYZ local = axis.Inverse.OfPoint(middle);
                // Only piles with plan footprint inside this cap can be represented.
                if (local.X < xmin - tol || local.X > xmax + tol ||
                    local.Y < ymin - tol || local.Y > ymax + tol)
                    continue;
                if (box.Min.Z > capBottom + tol || box.Max.Z < capBottom - tol)
                    continue;
                // Test intersection of the exact section cutting plane with the pile
                // horizontal bounding footprint. A pile merely in view depth is excluded.
                var corners = new[] {
                    new XYZ(box.Min.X, box.Min.Y, middle.Z),
                    new XYZ(box.Min.X, box.Max.Y, middle.Z),
                    new XYZ(box.Max.X, box.Min.Y, middle.Z),
                    new XYZ(box.Max.X, box.Max.Y, middle.Z) };
                double low = corners.Min(p => (p - origin).DotProduct(normal));
                double high = corners.Max(p => (p - origin).DotProduct(normal));
                if (low > tol || high < -tol) continue;

                // The graphic's anchor is at the physical pile head, projected onto the
                // section plane; optional offsets calibrate the RFA's insertion origin.
                XYZ pileHead = new XYZ(middle.X, middle.Y, box.Max.Z);
                XYZ cutPoint = pileHead - normal * (pileHead - origin).DotProduct(normal);
                XYZ point = cutPoint + right * shiftX + up * shiftY;
                targets.Add(Tuple.Create(pile, point));
            }
            if (targets.Count == 0)
                warnings.Add(view.Name + ": không tìm thấy cọc thật cắt qua tuyến Detail. Không đặt ký hiệu Cọc 2D suy đoán.");

            var kept = new HashSet<long>();
            foreach (var target in targets)
            {
                FamilyInstance pile = target.Item1;
                XYZ point = target.Item2;
                var matches = existing.Where(x => FootingDrawingDetailState.SourceId(x) == pile.UniqueId).ToList();
                FamilyInstance placed = matches.FirstOrDefault(x => x.Symbol.Id.Value == symbol.Id.Value);
                if (placed == null)
                {
                    placed = doc.Create.NewFamilyInstance(point, symbol, view);
                    if (placed == null) throw new InvalidOperationException("Không đặt được Family Cọc 2D: " + pile.Id.Value);
                    FootingDrawingDetailState.Mark(placed, cap, view, Kind, pile.UniqueId);
                }
                else
                {
                    XYZ prior = (placed.Location as LocationPoint)?.Point ??
                        throw new InvalidOperationException("Family Cọc 2D không có điểm đặt: " + placed.Id.Value);
                    if ((point - prior).GetLength() > tol)
                        ElementTransformUtils.MoveElement(doc, placed.Id, point - prior);
                }
                kept.Add(placed.Id.Value);
            }
            // Delete only KStruct-owned extra symbols, including piles moved away from the cut.
            foreach (var outdated in existing.Where(x => !kept.Contains(x.Id.Value)))
            {
                var deleted = doc.Delete(outdated.Id);
                if (deleted.Any(id => id.Value != outdated.Id.Value))
                    throw new InvalidOperationException("Xóa ký hiệu cọc cũ ảnh hưởng đối tượng khác; đã hủy cập nhật.");
            }
        }

        private static bool IsPile(FamilyInstance fi)
        {
            string family = fi.Symbol?.FamilyName ?? "";
            string type = fi.Name ?? "";
            return new[] { "cọc", "coc", "pile" }.Any(token =>
                family.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
