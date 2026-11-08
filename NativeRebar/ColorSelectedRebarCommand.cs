using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WinForms = System.Windows.Forms;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ColorSelectedRebarCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Rebar> rebars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (rebars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(
                        ObjectType.Element, new RebarFilter(), "Chọn các thanh Rebar cần tô màu trong view hiện tại");
                    rebars = picked.Select(r => doc.GetElement(r)).OfType<Rebar>().Distinct().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;

                var picker = new WinForms.ColorDialog
                {
                    AllowFullOpen = true,
                    FullOpen = true,
                    AnyColor = true,
                    Color = System.Drawing.Color.FromArgb(40, 120, 220)
                };
                if (picker.ShowDialog() != WinForms.DialogResult.OK) return Result.Cancelled;
                var revitColor = new Autodesk.Revit.DB.Color(picker.Color.R, picker.Color.G, picker.Color.B);
                View view = doc.ActiveView;

                using (Transaction tx = new Transaction(doc, "KStruct - Tô màu Rebar"))
                {
                    tx.Start();
                    foreach (Rebar rebar in rebars)
                    {
                        OverrideGraphicSettings current = view.GetElementOverrides(rebar.Id);
                        OverrideGraphicSettings updated = current.SetProjectionLineColor(revitColor);
                        view.SetElementOverrides(rebar.Id, updated);
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận màu override cho Rebar.");
                }
                ui.RefreshActiveView();
                TaskDialog.Show("KStruct - Tô màu Rebar",
                    "Đã đổi màu đường nét cho " + rebars.Count + " Rebar trong view hiện tại. Các override khác được giữ nguyên.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tô màu Rebar", ex.Message);
                return Result.Failed;
            }
        }
    }
}
