using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class SplitStraightBeamCommand : IExternalCommand
    {
        private sealed class BeamFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                element?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                FamilyInstance beam = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .FirstOrDefault(e => e.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming);
                if (beam == null)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new BeamFilter(), "Chọn dầm thẳng cần chia");
                    beam = doc.GetElement(picked) as FamilyInstance;
                }
                if (beam == null) return Result.Cancelled;
                Line axis = (beam.Location as LocationCurve)?.Curve as Line;
                if (axis == null) throw new InvalidOperationException("Chỉ hỗ trợ dầm Structural Framing có trục thẳng.");
                if (new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().Any(r => r.GetHostId() == beam.Id))
                    throw new InvalidOperationException("Dầm đang host Rebar. Hãy chuyển hoặc xóa Rebar trước khi chia dầm để tránh làm mất liên kết host.");

                XYZ start = axis.GetEndPoint(0), end = axis.GetEndPoint(1);
                XYZ vector = end - start;
                double length = vector.GetLength();
                if (length <= doc.Application.ShortCurveTolerance * 2)
                    throw new InvalidOperationException("Dầm quá ngắn để chia.");
                XYZ direction = vector.Normalize();
                XYZ pickedPoint = ui.Selection.PickPoint("Chọn điểm chia trên trục dầm");
                double station = (pickedPoint - start).DotProduct(direction);
                XYZ split = start + direction.Multiply(station);
                double offAxis = pickedPoint.DistanceTo(split);
                double pickTolerance = UnitUtils.ConvertToInternalUnits(200, UnitTypeId.Millimeters);
                if (offAxis > pickTolerance)
                    throw new InvalidOperationException("Điểm chọn lệch trục dầm quá 200 mm.");
                double endTolerance = Math.Max(doc.Application.ShortCurveTolerance * 2,
                    UnitUtils.ConvertToInternalUnits(100, UnitTypeId.Millimeters));
                if (station <= endTolerance || length - station <= endTolerance)
                    throw new InvalidOperationException("Điểm chia phải cách hai đầu dầm ít nhất 100 mm.");

                bool wasPinned = beam.Pinned;
                FamilyInstance newBeam = null;
                using (Transaction tx = new Transaction(doc, "KStruct - Chia dầm thẳng"))
                {
                    tx.Start();
                    if (wasPinned) beam.Pinned = false;
                    ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElement(doc, beam.Id, XYZ.Zero);
                    newBeam = copiedIds.Select(doc.GetElement).OfType<FamilyInstance>()
                        .FirstOrDefault(candidate => candidate.Id != beam.Id &&
                            candidate.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming);
                    if (newBeam == null) throw new InvalidOperationException("Không tạo được bản sao dầm.");
                    if (newBeam.Pinned) newBeam.Pinned = false;

                    StructuralFramingUtils.DisallowJoinAtEnd(beam, 1);
                    StructuralFramingUtils.DisallowJoinAtEnd(newBeam, 0);
                    (beam.Location as LocationCurve).Curve = Line.CreateBound(start, split);
                    (newBeam.Location as LocationCurve).Curve = Line.CreateBound(split, end);
                    if (wasPinned)
                    {
                        beam.Pinned = true;
                        newBeam.Pinned = true;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận chia dầm.");
                }
                TaskDialog.Show("KStruct - Chia dầm",
                    "Đã chia dầm " + beam.Id.Value + " thành hai đoạn. Dầm mới: " + newBeam.Id.Value + ".");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Chia dầm", ex.Message);
                return Result.Failed;
            }
        }
    }
}
