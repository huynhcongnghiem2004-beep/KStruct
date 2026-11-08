using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ShowRebarGroupNameCommand : IExternalCommand
    {
        private static readonly Guid GroupSchemaId = new Guid("4E5B6F62-B8B3-4A2F-9B06-DDD953D4D4BC");

        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            try
            {
                Document doc = ui.Document;
                List<Rebar> rebars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().ToList();
                if (rebars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(), "Chọn các Rebar cần xem tên nhóm");
                    rebars = picked.Select(doc.GetElement).OfType<Rebar>().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;

                Schema schema = Schema.Lookup(GroupSchemaId);
                Field groupField = schema?.GetField("GroupNameProperty");
                List<string> rows = rebars.Select(rebar =>
                {
                    string name = "";
                    if (schema != null && groupField != null)
                    {
                        Entity entity = rebar.GetEntity(schema);
                        if (entity.IsValid()) name = entity.Get<string>(groupField) ?? "";
                    }
                    return "Rebar " + rebar.Id.Value + ": " + (string.IsNullOrWhiteSpace(name) ? "(chưa có tên nhóm)" : name);
                }).ToList();
                TaskDialog.Show("KStruct - Tên nhóm thép", string.Join("\n", rows));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tên nhóm thép", ex.Message);
                return Result.Failed;
            }
        }
    }
}
