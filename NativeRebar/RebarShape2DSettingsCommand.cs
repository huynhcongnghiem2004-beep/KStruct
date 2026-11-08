using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class RebarShape2DSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                var textTypes = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                    .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (textTypes.Count == 0) throw new InvalidOperationException("Project chưa có TextNoteType.");
                var tagTypes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RebarTags)
                    .WhereElementIsElementType().Cast<ElementType>()
                    .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                var options = DrawRebarShape2DCommand.ShowOptions(data.Application.MainWindowHandle, textTypes,
                    tagTypes, RebarShapeDrawingSettings.Load(doc), "Lưu thiết lập");
                if (options == null) return Result.Cancelled;
                using (Transaction tx = new Transaction(doc, "KStruct - Cài đặt hình thép 2D"))
                {
                    tx.Start();
                    RebarShapeDrawingSettings.Save(doc, options);
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không lưu được cài đặt hình thép.");
                }
                TaskDialog.Show("KStruct - Cài đặt hình thép 2D", "Đã lưu thiết lập trong project hiện tại.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cài đặt hình thép 2D", ex.Message);
                return Result.Failed;
            }
        }
    }
}
