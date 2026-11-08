using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class CheckAnalyticalAssociationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                AnalyticalToPhysicalAssociationManager manager = AnalyticalToPhysicalAssociationManager.GetAnalyticalToPhysicalAssociationManager(doc);
                if (manager == null) throw new InvalidOperationException("Revit không khởi tạo được AnalyticalToPhysicalAssociationManager.");
                List<Element> physical = ui.Selection.GetElementIds().Select(doc.GetElement).Where(CreateAnalyticalModelsCommand.IsSupportedPhysical).ToList();
                if (physical.Count == 0)
                    physical = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                        .Where(CreateAnalyticalModelsCommand.IsSupportedPhysical).ToList();

                int associatedCount = 0;
                var missing = new List<string>();
                foreach (Element element in physical)
                {
                    bool has = manager.HasAssociation(element.Id);
                    if (has) associatedCount++;
                    else missing.Add((element.Category?.Name ?? "Cấu kiện") + " " + element.Id.Value + " — chưa liên kết Analytical.");
                }
                int analyticalMembers = new FilteredElementCollector(doc).OfClass(typeof(AnalyticalMember)).WhereElementIsNotElementType().GetElementCount();
                int analyticalPanels = new FilteredElementCollector(doc).OfClass(typeof(AnalyticalPanel)).WhereElementIsNotElementType().GetElementCount();
                string report = "Cấu kiện vật lý được rà: " + physical.Count + "\nĐã có association: " + associatedCount +
                    "\nChưa có association: " + missing.Count + "\nAnalytical Member trong project: " + analyticalMembers +
                    "\nAnalytical Panel trong project: " + analyticalPanels;
                if (missing.Count > 0) report += "\n\nThiếu liên kết:\n" + string.Join("\n", missing.Take(16));
                TaskDialog.Show("KStruct - Kiểm tra Analytical", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Kiểm tra Analytical", ex.Message);
                return Result.Failed;
            }
        }
    }
}
