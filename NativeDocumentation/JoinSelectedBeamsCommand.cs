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
    public sealed class JoinSelectedBeamsCommand : IExternalCommand
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
                List<FamilyInstance> beams = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>()
                    .Where(e => e.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming)
                    .Distinct().ToList();
                if (beams.Count < 2)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new BeamFilter(),
                        "Chọn ít nhất hai dầm Structural Framing có phần hình học giao nhau");
                    beams = picked.Select(r => doc.GetElement(r)).OfType<FamilyInstance>()
                        .Where(e => e.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming)
                        .Distinct().ToList();
                }
                if (beams.Count < 2)
                {
                    TaskDialog.Show("KStruct - Ghép dầm", "Cần chọn ít nhất hai dầm.");
                    return Result.Cancelled;
                }

                int joined = 0;
                int alreadyJoined = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Ghép hình học dầm"))
                {
                    tx.Start();
                    for (int i = 0; i < beams.Count; i++)
                    {
                        for (int j = i + 1; j < beams.Count; j++)
                        {
                            FamilyInstance a = beams[i], b = beams[j];
                            if (JoinGeometryUtils.AreElementsJoined(doc, a, b))
                            {
                                alreadyJoined++;
                                continue;
                            }
                            using (SubTransaction sub = new SubTransaction(doc))
                            {
                                sub.Start();
                                try
                                {
                                    JoinGeometryUtils.JoinGeometry(doc, a, b);
                                    if (sub.Commit() == TransactionStatus.Committed) joined++;
                                    else throw new InvalidOperationException("Revit không hoàn tất join.");
                                }
                                catch (Exception ex)
                                {
                                    if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                    skipped.Add(a.Id.Value + "–" + b.Id.Value + ": " + ex.Message);
                                }
                            }
                        }
                    }
                    if (joined == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Ghép dầm",
                            alreadyJoined > 0
                                ? "Không có cặp dầm giao nhau mới để ghép. Đã có " + alreadyJoined + " cặp được join."
                                : "Không ghép được cặp dầm nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các join.");
                }
                string report = "Đã ghép hình học " + joined + " cặp dầm.";
                if (alreadyJoined > 0) report += "\nĐã có sẵn: " + alreadyJoined + " cặp.";
                if (skipped.Count > 0) report += "\nKhông ghép được: " + skipped.Count + " cặp.\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Ghép dầm", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Ghép dầm", ex.Message);
                return Result.Failed;
            }
        }
    }
}
