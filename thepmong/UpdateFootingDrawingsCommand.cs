using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class UpdateFootingDrawingsCommand : IExternalCommand
    {
        private sealed class FoundationFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance fi && fi.Category != null &&
                fi.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation && fi.Location is LocationPoint;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> footings = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(IsFoundation).Distinct().ToList();
                var hostIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // Explicit selection always takes precedence. Never also update a different
                // footing merely because its drawing happens to be the active view.
                if (footings.Count == 0 && ui.Selection.GetElementIds().Count == 0 &&
                    FootingDrawingStorage.TryRead(ui.ActiveView, out string activeHost,
                        out string activeRole, out double activeMargin))
                    hostIds.Add(activeHost);
                foreach (ElementId selectedId in ui.Selection.GetElementIds())
                {
                    Element selected = doc.GetElement(selectedId);
                    View view = selected as View;
                    if (selected is Viewport viewport) view = doc.GetElement(viewport.ViewId) as View;
                    if (FootingDrawingStorage.TryRead(view, out string viewHost, out string viewRole, out double viewMargin)) hostIds.Add(viewHost);
                }
                foreach (string uniqueId in hostIds)
                    if (doc.GetElement(uniqueId) is FamilyInstance markedFooting && IsFoundation(markedFooting)) footings.Add(markedFooting);
                if (footings.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FoundationFilter(), "Chọn móng có hồ sơ KStruct cần cập nhật");
                    footings = picked.Select(x => doc.GetElement(x)).OfType<FamilyInstance>().Where(IsFoundation).Distinct().ToList();
                }
                if (footings.Count == 0) return Result.Cancelled;

                footings = footings.GroupBy(f => f.Id).Select(group => group.First()).ToList();
                int updated = 0;
                var skipped = new List<string>();
                var warnings = new List<string>();
                using (var tx = new Transaction(doc, "KStruct - Cập nhật hồ sơ móng"))
                {
                    tx.Start();
                    foreach (FamilyInstance footing in footings)
                    {
                        List<View> views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                            .Where(v => !v.IsTemplate && FootingDrawingStorage.TryRead(v, out string host, out string role, out double margin) &&
                                host == footing.UniqueId && role != "Sheet" &&
                                (FootingDrawingStorage.Version(v) < 3 || FootingDrawingDetailState.OwnsView(v, footing))).ToList();
                        if (views.Count == 0)
                        {
                            // Legacy sections created before view metadata used the numeric ElementId in their names.
                            string legacy = "KStruct - MĐ " + footing.Id.Value + " - MC";
                            views = new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<ViewSection>()
                                .Where(v => !v.IsTemplate && v.Name.StartsWith(legacy, StringComparison.OrdinalIgnoreCase)).Cast<View>().ToList();
                        }
                        if (views.Count == 0) { skipped.Add("Móng " + footing.Id.Value + ": không tìm thấy view đã tạo."); continue; }
                        using (var sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                foreach (View view in views)
                                {
                                    double margin = 300;
                                    FootingDrawingStorage.TryRead(view, out string ignoredHost, out string role, out margin);
                                    FootingDrawingProfile pose=FootingDrawingDetailState.Read(view,footing,out IList<string> linked);
                                    if(pose!=null)FootingDrawingBuilder.FollowHostPose(view,footing,pose);
                                    if (view is ViewSection detailAxis && detailAxis.ViewType == ViewType.Detail &&
                                        (role == "Detail-X" || role == "Detail-Y"))
                                    {
                                        Transform axes = FootingDrawingGeometry.Axes(footing);
                                        XYZ expectedRight = (role == "Detail-X" ? axes.BasisX : axes.BasisY).Normalize();
                                        double alignment = Math.Abs(detailAxis.RightDirection.Normalize().DotProduct(expectedRight));
                                        if (alignment < 0.999)
                                            throw new InvalidOperationException("View " + detailAxis.Name +
                                                " dùng trục mặt cắt cũ không khớp " + role +
                                                ". Hãy tạo lại hồ sơ móng để hiệu chỉnh trục X/Y an toàn; không tự xoay View cũ có annotation.");
                                        if (pose != null)
                                            FootingDrawingBuilder.AlignDetailRowOnUpdate(
                                                detailAxis, footing, pose, role, warnings);
                                    }
                                    FootingDrawingBuilder.UpdateCrop(view, footing, margin, role, warnings);
                                }
                                doc.Regenerate();
                                foreach (View view in views)
                                {
                                    FootingDrawingProfile options = FootingDrawingDetailState.Read(view, footing, out IList<string> targetIds);
                                    if (options == null) continue;
                                    if (view is ViewPlan callout && options.HasDetails)
                                    {
                                        var targets = targetIds.Select(id => doc.GetElement(id)).OfType<ViewSection>()
                                            .Where(section => FootingDrawingStorage.TryRead(section, out string targetHost, out string targetRole, out double targetMargin) &&
                                                targetHost == footing.UniqueId && section.ViewType == ViewType.Detail &&
                                                (targetRole.StartsWith("Detail-", StringComparison.Ordinal) || targetRole.StartsWith("Section-", StringComparison.Ordinal))).ToList();
                                        if (targets.Count == 0 || targets.Count != targetIds.Count) throw new InvalidOperationException("Thiếu Detail View liên kết hoặc bộ cũ dùng Section View; hãy tạo lại hồ sơ bằng Detail.");
                                        FootingDrawingDetailLinks.Refresh(doc, footing, callout, targets, warnings);
                                    }
                                    FootingDrawingStorage.TryRead(view, out string tagHost, out string tagRole, out double tagMargin);
                                    if (options.UsePlanDetailComponents && view is ViewPlan plan && (tagRole == "Plan-Top" || tagRole == "Plan-Bot"))
                                    {
                                        FootingPlanDetailComponentBuilder.Refresh(doc, plan, footing, options, tagRole == "Plan-Top", warnings);
                                        FootingPlanRebarAnnotationBuilder.Refresh(doc, plan, footing, tagRole == "Plan-Top", options, warnings);
                                    }
                                    else FootingDrawingRebarMultiTags.Refresh(doc, footing, view, tagRole, options, warnings);
                                    if (view is ViewSection pileSection &&
                                        (tagRole == "Detail-X" || tagRole == "Detail-Y"))
                                        FootingPileSectionDetailBuilder.Refresh(doc, pileSection, footing, options, warnings);
                                    FootingDrawingBeamBreaks.Refresh(doc, footing, view, options, warnings);
                                    FootingDrawingDetailState.Save(view,footing,options,targetIds.Select(id=>doc.GetElement(id)).OfType<ViewSection>());
                                }
                                doc.Regenerate();
                                var viewIds=new HashSet<ElementId>(views.Select(view=>view.Id));
                                foreach(Viewport viewport in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>()
                                    .Where(viewport=>viewIds.Contains(viewport.ViewId)))
                                    FootingDrawingViewportTitles.Center(viewport,warnings);
                                if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận crop mới.");
                                updated += views.Count;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Móng " + footing.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (updated == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Cập nhật hồ sơ móng", "Không có view nào được cập nhật.\n" + string.Join("\n", skipped.Take(10)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không commit được cập nhật.");
                }
                string report = "Đã cập nhật crop/view range của " + updated + " view; khôi phục ký hiệu Detail gốc, gỡ reference phụ của KStruct và làm mới Breakline có mã sở hữu. Làm mới MultiTag của lệnh và căn giữa title viewport; tag/dimension thủ công được giữ nguyên.";
                if (warnings.Count > 0) report += "\nCảnh báo:\n" + string.Join("\n", warnings.Distinct().Take(10));
                if (skipped.Count > 0) report += "\nBỏ qua:\n" + string.Join("\n", skipped.Take(10));
                TaskDialog.Show("KStruct - Cập nhật hồ sơ móng", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.ToString(); TaskDialog.Show("KStruct - Cập nhật hồ sơ móng", ex.Message); return Result.Failed; }
        }
        private static bool IsFoundation(FamilyInstance fi) => fi != null && fi.Category != null &&
            fi.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation && fi.Location is LocationPoint;
    }
}


