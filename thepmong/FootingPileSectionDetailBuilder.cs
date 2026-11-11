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
            if (!profile.PileSymbolOriginConfirmed)
                throw new InvalidOperationException("Chưa xác nhận gốc Family BS_FM_Detail Cọc 2D nằm trên tim hình học cọc. Hãy kiểm tra Reference Planes trong Family Editor và đánh dấu xác nhận.");
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
            double insertionTol = UnitUtils.ConvertToInternalUnits(0.1, UnitTypeId.Millimeters);
            // No manual XY offsets. The family insertion axis MUST coincide with the
            // actual pile center line in the section. A shifted RFA origin is a family defect.


            var piles = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(p => p.Id.Value != cap.Id.Value && IsPile(p, profile.PileFamilyFilter))
                .OrderBy(p => p.Id.Value).ToList();
            var targets = new List<Tuple<FamilyInstance, XYZ, double>>();
            foreach (FamilyInstance pile in piles)
            {
                if (Math.Abs(pile.GetTransform().BasisZ.Normalize().DotProduct(XYZ.BasisZ)) < 0.999999)
                    throw new InvalidOperationException("Cọc " + pile.Id.Value +
                        " bị nghiêng; KStruct chưa thể dùng ký hiệu Cọc 2D thẳng đứng.");
                List<XYZ> vertices;
                try { vertices = FootingDrawingGeometry.WorldVertices(pile); }
                catch (InvalidOperationException ex)
                {
                    warnings.Add(view.Name + ": bỏ qua cọc " + pile.Id.Value + " vì " + ex.Message);
                    continue;
                }
                if (vertices.Count == 0)
                    throw new InvalidOperationException("Cọc " + pile.Id.Value + " không có Solid, không thể xác định tim.");
                // Use actual solid vertices rather than axis-aligned model bounding boxes,
                // so rotated pile families are correctly tested against the section plane.
                List<XYZ> localVertices = vertices.Select(axis.Inverse.OfPoint).ToList();
                double centerX = (localVertices.Min(v => v.X) + localVertices.Max(v => v.X)) * 0.5;
                double centerY = (localVertices.Min(v => v.Y) + localVertices.Max(v => v.Y)) * 0.5;
                if (centerX < xmin - tol || centerX > xmax + tol ||
                    centerY < ymin - tol || centerY > ymax + tol)
                    continue;
                double pileBottom = vertices.Min(p => p.Z), pileTop = vertices.Max(p => p.Z);
                if (pileBottom > capBottom + tol || pileTop < capBottom - tol)
                    continue;
                double low = vertices.Min(p => (p - origin).DotProduct(normal));
                double high = vertices.Max(p => (p - origin).DotProduct(normal));
                if (low > tol || high < -tol) continue;
                // A full-width centerline symbol must NEVER represent an eccentric cut.
                XYZ pileCenter = axis.OfPoint(new XYZ(centerX, centerY, 0));
                double centerDistanceMm = UnitUtils.ConvertFromInternalUnits(
                    Math.Abs((pileCenter - origin).DotProduct(normal)), UnitTypeId.Millimeters);
                if (!FootingPileSectionPolicy.IsCentered(centerDistanceMm))
                {
                    warnings.Add(view.Name + ": cọc " + pile.Id.Value +
                        " giao mặt cắt nhưng tuyến cắt lệch tim " +
                        centerDistanceMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                        " mm; không đặt Cọc 2D toàn tiết diện.");
                    continue;
                }
                // Reject unverified off-center placement origins rather than presenting a
                // graphical center which may not match the true model pile axis.
                LocationPoint actualLocation = pile.Location as LocationPoint;
                if (actualLocation == null)
                    throw new InvalidOperationException("Cọc " + pile.Id.Value +
                        " không có LocationPoint để xác nhận tim cọc.");
                double originDiff = new XYZ(actualLocation.Point.X - pileCenter.X,
                    actualLocation.Point.Y - pileCenter.Y, 0).GetLength();
                if (originDiff > tol)
                    throw new InvalidOperationException("Cọc " + pile.Id.Value +
                        " có gốc Family lệch tim Solid > 1 mm. Cần xác minh Family cọc trước khi đặt ký hiệu.");
                // Anchor at the real pile-head elevation, along actual centerline.
                XYZ pileHead = new XYZ(pileCenter.X, pileCenter.Y, pileTop);
                XYZ point = pileHead - normal * (pileHead - origin).DotProduct(normal);
                double diameter = FootingPileSectionDiameter.ReadPileDiameter(doc, pile, profile.PileDiameterParameterName);
                targets.Add(Tuple.Create(pile, point, diameter));
            }
            if (targets.Count == 0)
                warnings.Add(view.Name + ": không tìm thấy cọc thật cắt qua tuyến Detail. Không đặt ký hiệu Cọc 2D suy đoán.");

            var kept = new HashSet<long>();
            foreach (var target in targets)
            {
                FamilyInstance pile = target.Item1;
                XYZ point = target.Item2;
                double diameter = target.Item3;
                FamilySymbol diameterSymbol = FootingPileSectionDiameter.ResolveSymbol(doc, symbol, diameter);
                if (!diameterSymbol.IsActive) { diameterSymbol.Activate(); doc.Regenerate(); }
                var matches = existing.Where(x => FootingDrawingDetailState.SourceId(x) == pile.UniqueId).ToList();
                FamilyInstance placed = matches.FirstOrDefault();
                if (placed == null)
                {
                    placed = doc.Create.NewFamilyInstance(point, diameterSymbol, view);
                    if (placed == null) throw new InvalidOperationException("Không đặt được Family Cọc 2D: " + pile.Id.Value);
                    FootingDrawingDetailState.Mark(placed, cap, view, Kind, pile.UniqueId);
                }
                else
                {
                    if (placed.Symbol.Id.Value != diameterSymbol.Id.Value)
                        placed.Symbol = diameterSymbol;
                    XYZ prior = (placed.Location as LocationPoint)?.Point ??
                        throw new InvalidOperationException("Family Cọc 2D không có điểm đặt: " + placed.Id.Value);
                    if ((point - prior).GetLength() > insertionTol)
                        ElementTransformUtils.MoveElement(doc, placed.Id, point - prior);
                }
                // Validate the insertion point itself, not just the user's selected type.
                doc.Regenerate();
                XYZ actualPoint = (placed.Location as LocationPoint)?.Point ??
                    throw new InvalidOperationException("Ký hiệu Cọc 2D không còn điểm đặt.");
                if ((actualPoint - point).GetLength() > insertionTol)
                    throw new InvalidOperationException("Tim Family Cọc 2D không trùng tim cọc " + pile.Id.Value +
                        " sau khi cập nhật.");
                FootingPileSectionDiameter.Verify(placed, diameter);
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

        private static bool IsPile(FamilyInstance fi, string filter)
        {
            string family = fi.Symbol?.FamilyName ?? "";
            string type = fi.Name ?? "";
            string combined = (family + " " + type).ToLowerInvariant();
            // Pile-cap families can include "pile"/"cọc" in their names; never classify
            // them as individual piles for this Detail Item placement.
            if (new[] { "pile cap", "pilecap", "đài", "dai coc", "đế cọc" }
                .Any(x => combined.Contains(x))) return false;
            string[] tokens = (string.IsNullOrWhiteSpace(filter) ? "cọc,coc,pile" : filter)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
            if (tokens.Length == 0)
                throw new InvalidOperationException("Danh sách nhận diện Family cọc đang trống.");
            return tokens.Any(token =>
                family.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
