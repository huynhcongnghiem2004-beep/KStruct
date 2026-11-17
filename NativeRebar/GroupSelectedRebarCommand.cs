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
    public sealed class GroupSelectedRebarCommand : IExternalCommand
    {
        private static readonly Guid GroupSchemaId = new Guid("4E5B6F62-B8B3-4A2F-9B06-DDD953D4D4BC");
        private const string GroupFieldName = "GroupNameProperty";
        private const string GroupQuantityParameter = "BS_REBAR_QUANTITY_IN_GROUP";

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Rebar> selected = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (selected.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, "Chọn các Rebar cần đưa vào cùng một nhóm");
                    selected = picked.Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                }
                if (selected.Count == 0) return Result.Cancelled;

                Schema schema = Schema.Lookup(GroupSchemaId);
                Field field = schema?.GetField(GroupFieldName);
                if (schema != null && field == null)
                    throw new InvalidOperationException("Schema nhóm thép đã tồn tại nhưng thiếu trường " + GroupFieldName + ".");
                Dictionary<ElementId, string> previousNames = selected.ToDictionary(r => r.Id, r => ReadGroupName(r, schema, field));
                if (previousNames.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    TaskDialogResult answer = TaskDialog.Show("KStruct - Nhóm Rebar",
                        "Một số thanh đã thuộc nhóm. Gán nhóm mới sẽ chuyển các thanh đã chọn sang cùng một nhóm mới.",
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                    if (answer != TaskDialogResult.Yes) return Result.Cancelled;
                }

                if (schema == null)
                {
                    SchemaBuilder builder = new SchemaBuilder(GroupSchemaId);
                    builder.SetSchemaName("RebarGroup");
                    builder.AddSimpleField(GroupFieldName, typeof(string));
                    schema = builder.Finish();
                    field = schema.GetField(GroupFieldName);
                }

                string groupName = Guid.NewGuid().ToString();
                int totalQuantity = selected.Sum(rebar => Math.Max(0, rebar.Quantity));
                var affectedNames = new HashSet<string>(previousNames.Values.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal);
                using (Transaction tx = new Transaction(doc, "KStruct - Nhóm Rebar"))
                {
                    tx.Start();
                    Entity groupEntity = new Entity(schema);
                    groupEntity.Set(field, groupName);
                    foreach (Rebar rebar in selected)
                    {
                        rebar.SetEntity(groupEntity);
                        SetGroupQuantity(rebar, totalQuantity);
                    }

                    List<Rebar> allBars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().ToList();
                    foreach (string previousName in affectedNames)
                    {
                        int remaining = allBars.Where(bar => !selected.Any(s => s.Id == bar.Id) &&
                                string.Equals(ReadGroupName(bar, schema, field), previousName, StringComparison.Ordinal))
                            .Sum(bar => Math.Max(0, bar.Quantity));
                        foreach (Rebar bar in allBars.Where(bar => !selected.Any(s => s.Id == bar.Id) &&
                                     string.Equals(ReadGroupName(bar, schema, field), previousName, StringComparison.Ordinal)))
                            SetGroupQuantity(bar, remaining);
                    }

                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi nhóm Rebar.");
                }

                TaskDialog.Show("KStruct - Nhóm Rebar", "Đã nhóm " + selected.Count + " Rebar. Tổng số lượng: " + totalQuantity + ".\nMã nhóm: " + groupName);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Nhóm Rebar", ex.Message);
                return Result.Failed;
            }
        }

        private static string ReadGroupName(Rebar rebar, Schema schema, Field field)
        {
            if (schema == null || field == null) return "";
            Entity entity = rebar.GetEntity(schema);
            return entity.IsValid() ? entity.Get<string>(field) ?? "" : "";
        }

        private static void SetGroupQuantity(Rebar rebar, int quantity)
        {
            Parameter parameter = rebar.LookupParameter(GroupQuantityParameter);
            if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Integer)
                parameter.Set(quantity);
        }
    }
}
