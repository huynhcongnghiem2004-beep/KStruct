using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class TagBeamsInViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = doc.ActiveView;
            try
            {
                if (view.IsTemplate || view.ViewType == ViewType.Schedule || view.ViewType == ViewType.DrawingSheet ||
                    view.ViewType == ViewType.ProjectBrowser || view.ViewType == ViewType.SystemBrowser ||
                    view.ViewType == ViewType.ThreeD)
                    throw new InvalidOperationException("Mở mặt bằng, mặt đứng hoặc mặt cắt để tự gắn tag dầm.");

                HashSet<ElementId> selected = new HashSet<ElementId>(ui.Selection.GetElementIds());
                List<FamilyInstance> beams = (selected.Count > 0
                        ? selected.Select(doc.GetElement).OfType<FamilyInstance>()
                        : new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_StructuralFraming)
                            .WhereElementIsNotElementType().OfType<FamilyInstance>())
                    .Where(IsBeam).OrderBy(beam => beam.Id.Value).ToList();
                if (beams.Count == 0)
                {
                    TaskDialog.Show("KStruct - Tag Beam", selected.Count > 0
                        ? "Chọn ít nhất một dầm Structural Framing."
                        : "Không tìm thấy dầm Structural Framing trong view hiện tại.");
                    return Result.Cancelled;
                }

                HashSet<ElementId> alreadyTagged = new HashSet<ElementId>();
                foreach (IndependentTag tag in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                {
                    try
                    {
                        foreach (ElementId id in tag.GetTaggedLocalElementIds()) alreadyTagged.Add(id);
                    }
                    catch { }
                }
                List<FamilyInstance> pending = beams.Where(beam => !alreadyTagged.Contains(beam.Id)).ToList();
                if (pending.Count == 0)
                {
                    TaskDialog.Show("KStruct - Tag Beam", "Các dầm được chọn đã có tag trong view hiện tại.");
                    return Result.Succeeded;
                }

                int created = 0, failed = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Tự gắn tag dầm trong view"))
                {
                    tx.Start();
                    for (int i = 0; i < pending.Count; i++)
                    {
                        FamilyInstance beam = pending[i];
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                Curve axis = (beam.Location as LocationCurve)?.Curve;
                                if (axis == null || axis.IsBound == false)
                                    throw new InvalidOperationException("Dầm không có trục hình học hữu hạn.");

                                XYZ anchor = ProjectToView(axis.Evaluate(0.5, true), view);
                                XYZ tangent = axis.ComputeDerivatives(0.5, true).BasisX;
                                XYZ projectedTangent = tangent - view.ViewDirection.Multiply(tangent.DotProduct(view.ViewDirection));
                                if (projectedTangent.GetLength() < 1e-8) projectedTangent = view.RightDirection;
                                projectedTangent = projectedTangent.Normalize();
                                XYZ side = view.ViewDirection.CrossProduct(projectedTangent).Normalize();
                                double band = (i / 2) % 4;
                                double offset = UnitUtils.ConvertToInternalUnits(250 + band * 180, UnitTypeId.Millimeters);
                                if (i % 2 == 1) offset = -offset;
                                XYZ head = anchor + side.Multiply(offset);

                                Reference reference = new Reference(beam);
                                IndependentTag tag = IndependentTag.Create(doc, view.Id, reference, true,
                                    TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, head);
                                if (tag == null) throw new InvalidOperationException("Revit không tạo được tag cho dầm.");
                                tag.LeaderEndCondition = LeaderEndCondition.Free;
                                tag.TagHeadPosition = head;
                                tag.SetLeaderEnd(reference, anchor);
                                if (sub.Commit() == TransactionStatus.Committed) created++;
                                else throw new InvalidOperationException("Revit không chấp nhận tag.");
                            }
                            catch
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                failed++;
                            }
                        }
                    }
                    if (created == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Tag Beam",
                            "Không tạo được tag. Kiểm tra dự án có Beam Tag family đã nạp và cho phép leader không.");
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các tag dầm.");
                }

                string report = "Đã gắn tag cho " + created + "/" + pending.Count + " dầm trong view.";
                if (failed > 0) report += " Bỏ qua " + failed + " dầm không tạo được tag.";
                TaskDialog.Show("KStruct - Tag Beam", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tag Beam", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsBeam(FamilyInstance instance) => instance?.Category?.Id.Value ==
            (long)BuiltInCategory.OST_StructuralFraming;

        private static XYZ ProjectToView(XYZ point, View view)
        {
            XYZ relative = point - view.Origin;
            return view.Origin + view.RightDirection.Multiply(relative.DotProduct(view.RightDirection)) +
                view.UpDirection.Multiply(relative.DotProduct(view.UpDirection));
        }
    }
}
