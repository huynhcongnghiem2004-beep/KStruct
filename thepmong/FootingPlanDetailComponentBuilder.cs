using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // One view-specific Detail Item for each footing plan. Never edits shared FamilySymbol type parameters.
    internal static class FootingPlanDetailComponentBuilder
    {
        internal const string Kind = "FootingPlanDetail2D";

        internal static FamilySymbol Resolve(Document doc, FootingDrawingProfile profile, bool top)
        {
            if (!profile.UsePlanDetailComponents) return null;
            string uid = top ? profile.PlanTopDetailTypeUniqueId : profile.PlanBotDetailTypeUniqueId;
            if (string.IsNullOrWhiteSpace(uid))
                throw new InvalidOperationException("Chưa chọn Detail Item Type cho mặt bằng " + (top ? "TOP" : "BOT") + ".");
            var symbol = doc.GetElement(uid) as FamilySymbol;
            if (symbol == null || symbol.Category == null ||
                symbol.Category.Id.Value != (long)BuiltInCategory.OST_DetailComponents)
                throw new InvalidOperationException("Detail Item đã chọn không còn trong project: " + uid);
            return symbol;
        }

        internal static void Refresh(Document doc, ViewPlan view, FamilyInstance footing, FootingDrawingProfile profile,
            bool top, IList<string> warnings)
        {
            if (!profile.UsePlanDetailComponents) return;
            FamilySymbol symbol = Resolve(doc, profile, top);
            if (!symbol.IsActive) symbol.Activate();
            doc.Regenerate();
            if (view.GetCategoryHidden(new ElementId(BuiltInCategory.OST_DetailComponents)))
                throw new InvalidOperationException("View Template đang ẩn Detail Items; hãy bật category trong template.");
            // Hide only in this generated plan. Do not modify the model or section views.
            var bars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(r => r.GetHostId() == footing.Id && r.CanBeHidden(view))
                .Select(r => r.Id).ToList();
            var newlyHidden = bars.Where(id => !doc.GetElement(id).IsHidden(view)).ToList();
            if (newlyHidden.Count > 0) view.HideElements(newlyHidden);

            BoundingBoxXYZ local = FootingDrawingGeometry.LocalBounds(footing);
            Transform frame = local.Transform;
            XYZ center = frame.OfPoint((local.Min + local.Max) * 0.5);
            // Project to the view plane so placement does not depend on footing elevation.
            XYZ point = center - view.ViewDirection * (center - view.Origin).DotProduct(view.ViewDirection);
            XYZ axis = frame.BasisX - view.ViewDirection * frame.BasisX.DotProduct(view.ViewDirection);
            if (axis.GetLength() < 1e-8) throw new InvalidOperationException("Không xác định được hướng móng trên mặt bằng.");
            axis = axis.Normalize();
            double angle = Math.Atan2(axis.DotProduct(view.UpDirection), axis.DotProduct(view.RightDirection));
            var owned = FootingDrawingDetailState.Annotations(view, footing, Kind).OfType<FamilyInstance>().ToList();
            FamilyInstance placed = owned.FirstOrDefault(x => x.Symbol.Id == symbol.Id);
            if (placed == null)
            {
                if (owned.Count > 0) FootingDrawingDetailState.Clear(view, footing, Kind);
                placed = doc.Create.NewFamilyInstance(point, symbol, view);
                if (placed == null) throw new InvalidOperationException("Revit không thể đặt Detail Item vào View.");
                FootingDrawingDetailState.Mark(placed, footing, view, Kind, top ? "TOP" : "BOT");
            }
            else
            {
                // Never delete and recreate a matching type: preserve its ElementId and references.
                XYZ oldPoint = (placed.Location as LocationPoint)?.Point ??
                    throw new InvalidOperationException("Detail Component không có LocationPoint.");
                if ((point - oldPoint).GetLength() > 1e-8)
                    ElementTransformUtils.MoveElement(doc, placed.Id, point - oldPoint);
            }
            // Compute the current direction from the placed instance. A generic Family parameter
            // named KStruct_PlanAngle is not required and rotation does not accumulate on Update.
            doc.Regenerate();
            XYZ current = placed.GetTransform().BasisX;
            current = current - view.ViewDirection * current.DotProduct(view.ViewDirection);
            if (current.GetLength() < 1e-8) throw new InvalidOperationException("Detail Item không có trục X hợp lệ.");
            current = current.Normalize();
            double deltaAngle = Math.Atan2(view.ViewDirection.DotProduct(current.CrossProduct(axis)),
                Math.Max(-1.0, Math.Min(1.0, current.DotProduct(axis))));
            if (Math.Abs(deltaAngle) > 1e-9)
                ElementTransformUtils.RotateElement(doc, placed.Id, Line.CreateUnbound(point, view.ViewDirection), deltaAngle);
            // An older build may have written duplicates. Keep one owned instance per plan.
            if (owned.Count > 1)
            {
                foreach (FamilyInstance extra in owned.Where(x => x.Id != placed.Id))
                {
                    var removed = doc.Delete(extra.Id);
                    if (removed.Any(id => id != extra.Id))
                        throw new InvalidOperationException("Xóa Detail Item trùng làm ảnh hưởng đối tượng khác; đã hủy.");
                }
            }
            // Re-apply the user's confirmed mapping at creation AND on every update.
            // A failed readback throws and the owning footing SubTransaction rolls back.
            FootingPlanDetailComponentSizing.Apply(doc, placed, local, profile);
        }
    }

}
