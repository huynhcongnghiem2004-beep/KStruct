using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class SplitZoneElementCommand : IExternalCommand
    {
        private static readonly Guid CopySchemaId = new Guid("BB2F1B86-7651-4B13-B7B1-2B6C458C6F11");

        private sealed class LinkFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is RevitLinkInstance;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class ZoneFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is DirectShape shape &&
                string.Equals(shape.ApplicationId, SplitZoneCommand.ApplicationKey, StringComparison.Ordinal);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class UseDestinationTypesHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args) => DuplicateTypeAction.UseDestinationTypes;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> selected = ui.Selection.GetElementIds().Select(doc.GetElement).Where(element => element != null).ToList();
                RevitLinkInstance link = selected.OfType<RevitLinkInstance>().FirstOrDefault();
                if (link == null)
                {
                    Reference linkReference = ui.Selection.PickObject(ObjectType.Element, new LinkFilter(), "Chọn Revit Link làm nguồn cấu kiện");
                    link = doc.GetElement(linkReference) as RevitLinkInstance;
                }
                if (link == null) return Result.Cancelled;
                Document sourceDoc = link.GetLinkDocument();
                if (sourceDoc == null) throw new InvalidOperationException("Link chưa được nạp hoặc không thể đọc.");

                List<DirectShape> zones = selected.OfType<DirectShape>()
                    .Where(shape => string.Equals(shape.ApplicationId, SplitZoneCommand.ApplicationKey, StringComparison.Ordinal))
                    .ToList();
                if (zones.Count == 0)
                {
                    IList<Reference> zoneReferences = ui.Selection.PickObjects(ObjectType.Element, new ZoneFilter(),
                        "Chọn một hoặc nhiều khối KStruct Split Zone");
                    zones = zoneReferences.Select(reference => doc.GetElement(reference) as DirectShape)
                        .Where(shape => shape != null).Distinct().ToList();
                }
                if (zones.Count == 0) return Result.Cancelled;

                Transform linkTransform = link.GetTotalTransform();
                HashSet<long> allowedCategories = SplitZoneElementSettings.Load();
                var zoneCopies = new Dictionary<string, List<Element>>();
                foreach (DirectShape zone in zones)
                {
                    BoundingBoxXYZ zoneBounds = ToWorldBounds(zone.get_BoundingBox(null));
                    if (zoneBounds == null) continue;
                    string zoneName = zone.ApplicationDataId ?? zone.Name;
                    if (string.IsNullOrWhiteSpace(zoneName)) zoneName = zone.Id.Value.ToString();
                    List<Element> candidates = new FilteredElementCollector(sourceDoc)
                        .WhereElementIsNotElementType()
                        .Where(element => element.Category != null && element.Category.CategoryType == CategoryType.Model &&
                            !element.ViewSpecific && !(element is RevitLinkInstance) &&
                            (allowedCategories == null || allowedCategories.Contains(element.Category.Id.Value)))
                        .Where(element => Intersects(zoneBounds, element.get_BoundingBox(null), linkTransform))
                        .OrderBy(element => CopyPriority(element))
                        .ThenBy(element => element.Id.Value).ToList();
                    zoneCopies[zoneName] = candidates;
                }
                if (zoneCopies.Count == 0 || zoneCopies.Values.All(list => list.Count == 0))
                {
                    TaskDialog.Show("KStruct - Split Zone Element", "Không tìm thấy cấu kiện trong các zone đã chọn của link.");
                    return Result.Cancelled;
                }

                var previewLines = zoneCopies.Select(pair => pair.Key + ": " + pair.Value.Count + " cấu kiện nguồn").ToList();
                Schema schema = GetCopySchema();
                int oldCopyCount = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                    .Count(element => zoneCopies.Keys.Any(zoneName => HasCopyTag(element, schema, zoneName, link.UniqueId)));
                if (TaskDialog.Show("Xác nhận sao chép Split Zone",
                    "Nguồn: " + link.Name + "\n" + string.Join("\n", previewLines) +
                    "\nBản sao KStruct sẽ được cập nhật: " + oldCopyCount +
                    "\n\nKStruct sao chép nguyên cấu kiện, không cắt cấu kiện tại biên zone. Tiếp tục?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
                    return Result.Cancelled;

                ExternalDefinition zoneDefinition = SplitZoneCommand.EnsureZoneDefinition(doc);
                int copiedSources = 0, copiedElementsCount = 0, failed = 0, removed = 0;
                int mappedParameters = 0, retainedOldCopies = 0;
                var copiedElements = new List<KeyValuePair<Element, string>>();
                using (Transaction tx = new Transaction(doc, "KStruct - Sao chép cấu kiện theo Split Zone"))
                {
                    tx.Start();
                    bool copySameNameParameters = SplitZoneElementSettings.LoadCopySameNameParameters();
                    List<SplitZoneParameterMapping> parameterMappings = SplitZoneElementSettings.LoadParameterMappings();
                    using (var copyOptions = new CopyPasteOptions())
                    {
                        copyOptions.SetDuplicateTypeNamesHandler(new UseDestinationTypesHandler());
                        foreach (KeyValuePair<string, List<Element>> zonePair in zoneCopies)
                        {
                            List<ElementId> oldCopies = new FilteredElementCollector(doc)
                                .WhereElementIsNotElementType()
                                .Where(element => HasCopyTag(element, schema, zonePair.Key, link.UniqueId))
                                .Select(element => element.Id).ToList();
                            if (zonePair.Value.Count == 0)
                            {
                                if (oldCopies.Count > 0)
                                {
                                    doc.Delete(oldCopies);
                                    removed += oldCopies.Count;
                                }
                                continue;
                            }

                            var zoneCreated = new List<KeyValuePair<Element, string>>();
                            var zoneCreatedIds = new List<ElementId>();
                            int zoneCopiedSources = 0, zoneFailed = 0, zoneMappedParameters = 0;
                            using (SubTransaction sub = new SubTransaction(doc))
                            {
                                sub.Start();
                                try
                                {
                                    ICollection<ElementId> createdIds = ElementTransformUtils.CopyElements(
                                        sourceDoc, zonePair.Value.Select(element => element.Id).ToList(), doc, linkTransform, copyOptions);
                                    if (createdIds == null || createdIds.Count == 0)
                                        throw new InvalidOperationException("Revit không tạo phần tử cho zone " + zonePair.Key + ".");
                                    List<Element> unmatchedCopies = createdIds.Select(doc.GetElement).Where(element => element != null).ToList();
                                    foreach (Element sourceElement in zonePair.Value)
                                    {
                                        Element primaryCopy = FindPrimaryCopy(sourceElement, unmatchedCopies, linkTransform);
                                        if (primaryCopy == null)
                                            throw new InvalidOperationException("Không đối chiếu được bản sao cho phần tử nguồn " + sourceElement.Id.Value + ".");
                                        unmatchedCopies.Remove(primaryCopy);
                                        List<SplitZoneParameterMapping> categoryMappings = parameterMappings
                                            .Where(mapping => sourceElement.Category != null && mapping.CategoryId == sourceElement.Category.Id.Value).ToList();
                                        zoneMappedParameters += CopyTypeParameters(sourceElement, ref primaryCopy,
                                            categoryMappings.Where(mapping => mapping.IsType).ToList());
                                        AddCopyTag(primaryCopy, schema, zonePair.Key, link.UniqueId, sourceElement.UniqueId ?? "");
                                        zoneCreated.Add(new KeyValuePair<Element, string>(primaryCopy, zonePair.Key));
                                        zoneCreatedIds.Add(primaryCopy.Id);

                                        if ((copySameNameParameters || categoryMappings.Count > 0) && primaryCopy != null)
                                            zoneMappedParameters += CopyInstanceParameters(sourceElement, primaryCopy,
                                                copySameNameParameters, categoryMappings.Where(mapping => !mapping.IsType).ToList());
                                        zoneCopiedSources++;
                                    }
                                    foreach (Element dependentCopy in unmatchedCopies)
                                    {
                                        AddCopyTag(dependentCopy, schema, zonePair.Key, link.UniqueId, "");
                                        zoneCreated.Add(new KeyValuePair<Element, string>(dependentCopy, zonePair.Key));
                                        zoneCreatedIds.Add(dependentCopy.Id);
                                    }
                                    if (sub.Commit() != TransactionStatus.Committed)
                                        throw new InvalidOperationException("Revit không chấp nhận bản sao cho zone " + zonePair.Key + ".");
                                }
                                catch
                                {
                                    if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                    zoneCreated.Clear();
                                    zoneCreatedIds.Clear();
                                    zoneCopiedSources = 0;
                                    zoneMappedParameters = 0;
                                    zoneFailed = zonePair.Value.Count;
                                }
                            }

                            if (zoneFailed == 0 && zoneCopiedSources == zonePair.Value.Count)
                            {
                                if (oldCopies.Count > 0)
                                {
                                    doc.Delete(oldCopies);
                                    removed += oldCopies.Count;
                                }
                                copiedElements.AddRange(zoneCreated);
                                copiedSources += zoneCopiedSources;
                                copiedElementsCount += zoneCreatedIds.Count;
                                mappedParameters += zoneMappedParameters;
                            }
                            else
                            {
                                if (zoneCreatedIds.Count > 0) doc.Delete(zoneCreatedIds);
                                retainedOldCopies += oldCopies.Count;
                                failed += zoneFailed;
                            }
                        }
                    }                    if (copiedSources == 0 && failed > 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Split Zone Element",
                            "Không sao chép được cấu kiện nào; các bản sao KStruct cũ được giữ nguyên. Kiểm tra quyền chỉnh sửa và loại cấu kiện trong link.");
                        return Result.Cancelled;
                    }

                    CategorySet categories = SplitZoneCommand.GetBoundCategories(doc, zoneDefinition);
                    foreach (KeyValuePair<Element, string> item in copiedElements)
                        if (item.Key.Category != null && item.Key.Category.AllowsBoundParameters)
                            categories.Insert(item.Key.Category);
                    if (categories.Size > 0)
                    {
                        InstanceBinding binding = doc.Application.Create.NewInstanceBinding(categories);
                        bool bound = doc.ParameterBindings.ReInsert(zoneDefinition, binding, GroupTypeId.Data);
                        if (!bound) bound = doc.ParameterBindings.Insert(zoneDefinition, binding, GroupTypeId.Data);
                        if (!bound) throw new InvalidOperationException("Không bind được tham số KStruct_Zone cho các cấu kiện đã sao chép.");
                        foreach (KeyValuePair<Element, string> item in copiedElements)
                        {
                            Parameter parameter = item.Key.get_Parameter(zoneDefinition.GUID);
                            if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                                parameter.Set(item.Value);
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thao tác sao chép Split Zone.");
                }

                TaskDialog.Show("KStruct - Split Zone Element",
                    "Đã sao chép " + copiedSources + " cấu kiện nguồn, tạo " + copiedElementsCount + " phần tử vào " + zones.Count + " zone." +
                    "\nĐã cập nhật/xóa bản sao KStruct cũ: " + removed +
                    "\nKhông sao chép được: " + failed +
                    "\nĐã chép parameter instance cùng tên: " + mappedParameters +
                    "\nBản cũ được giữ ở zone lỗi: " + retainedOldCopies +
                    "\nCác bản sao giữ nguyên toàn bộ hình học của cấu kiện nguồn; cấu kiện giao nhiều zone có thể xuất hiện trong nhiều zone.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Split Zone Element", ex.Message);
                return Result.Failed;
            }
        }

        private static Element FindPrimaryCopy(Element source, IList<Element> copies, Transform linkTransform)
        {
            if (source?.Category == null || copies == null || copies.Count == 0) return null;
            BoundingBoxXYZ sourceBounds = ToWorldBounds(source.get_BoundingBox(null));
            if (sourceBounds == null) return copies.FirstOrDefault(copy => copy.Category?.Id == source.Category.Id);
            XYZ sourceCenter = (sourceBounds.Min + sourceBounds.Max) * 0.5;
            if (linkTransform != null) sourceCenter = linkTransform.OfPoint(sourceCenter);
            Element sourceType = source.Document.GetElement(source.GetTypeId());
            string sourceTypeName = sourceType?.Name ?? source.Name;
            return copies.Where(copy => copy.Category?.Id == source.Category.Id)
                .OrderBy(copy =>
                {
                    Element copyType = copy.Document.GetElement(copy.GetTypeId());
                    return string.Equals(copyType?.Name ?? copy.Name, sourceTypeName, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                })
                .ThenBy(copy =>
                {
                    BoundingBoxXYZ copyBounds = ToWorldBounds(copy.get_BoundingBox(null));
                    return copyBounds == null ? double.MaxValue :
                        ((copyBounds.Min + copyBounds.Max) * 0.5).DistanceTo(sourceCenter);
                }).FirstOrDefault();
        }

        private static int CopyInstanceParameters(Element source, Element target, bool copySameName,
            IList<SplitZoneParameterMapping> explicitMappings)
        {
            var pairs = new List<Tuple<string, string>>();
            if (copySameName)
                foreach (Parameter parameter in source.Parameters.Cast<Parameter>())
                {
                    string name = parameter.Definition?.Name;
                    if (!string.IsNullOrWhiteSpace(name)) pairs.Add(Tuple.Create(name, name));
                }
            foreach (SplitZoneParameterMapping mapping in explicitMappings ?? new List<SplitZoneParameterMapping>())
                pairs.Add(Tuple.Create(mapping.SourceName, mapping.TargetName));

            int copied = 0;
            foreach (Tuple<string, string> pair in pairs.GroupBy(item =>
                (item.Item1 ?? "").ToUpperInvariant() + "\u001f" + (item.Item2 ?? "").ToUpperInvariant())
                .Select(group => group.First()))
            {
                Parameter sourceParameter = source.Parameters.Cast<Parameter>().FirstOrDefault(parameter =>
                    parameter.Definition != null && string.Equals(parameter.Definition.Name, pair.Item1, StringComparison.OrdinalIgnoreCase));
                if (sourceParameter == null || !sourceParameter.HasValue) continue;
                Parameter targetParameter = target.GetParameters(pair.Item2 ?? "").FirstOrDefault(parameter =>
                    parameter.Definition != null && !parameter.IsReadOnly &&
                    parameter.StorageType == sourceParameter.StorageType &&
                    string.Equals(parameter.Definition.GetDataType()?.TypeId,
                        sourceParameter.Definition.GetDataType()?.TypeId, StringComparison.Ordinal));
                if (targetParameter == null) continue;
                bool changed = false;
                try
                {
                    switch (sourceParameter.StorageType)
                    {
                        case StorageType.String: changed = targetParameter.Set(sourceParameter.AsString() ?? string.Empty); break;
                        case StorageType.Integer: changed = targetParameter.Set(sourceParameter.AsInteger()); break;
                        case StorageType.Double: changed = targetParameter.Set(sourceParameter.AsDouble()); break;
                    }
                }
                catch { continue; }
                if (changed) copied++;
            }
            return copied;
        }

        private static int CopyTypeParameters(Element source, ref Element target,
            IList<SplitZoneParameterMapping> mappings)
        {
            if (source == null || target == null || mappings == null || mappings.Count == 0) return 0;
            ElementType sourceType = source.Document.GetElement(source.GetTypeId()) as ElementType;
            ElementType targetType = target.Document.GetElement(target.GetTypeId()) as ElementType;
            if (sourceType == null || targetType == null) return 0;

            var pairs = mappings.GroupBy(item =>
                (item.SourceName ?? "").ToUpperInvariant() + "\u001f" + (item.TargetName ?? "").ToUpperInvariant())
                .Select(group => group.First()).ToList();
            var values = new List<Tuple<StorageType, string, object>>();
            foreach (SplitZoneParameterMapping mapping in pairs)
            {
                Parameter sourceParameter = sourceType.Parameters.Cast<Parameter>().FirstOrDefault(parameter =>
                    parameter.Definition != null && string.Equals(parameter.Definition.Name, mapping.SourceName, StringComparison.OrdinalIgnoreCase));
                Parameter targetParameter = targetType.GetParameters(mapping.TargetName ?? "").FirstOrDefault(parameter =>
                    parameter.Definition != null && !parameter.IsReadOnly && sourceParameter != null &&
                    parameter.StorageType == sourceParameter.StorageType &&
                    string.Equals(parameter.Definition.GetDataType()?.TypeId,
                        sourceParameter.Definition.GetDataType()?.TypeId, StringComparison.Ordinal));
                if (sourceParameter == null || targetParameter == null || !sourceParameter.HasValue) continue;
                try
                {
                    switch (sourceParameter.StorageType)
                    {
                        case StorageType.String: values.Add(Tuple.Create(StorageType.String, mapping.TargetName, (object)(sourceParameter.AsString() ?? string.Empty))); break;
                        case StorageType.Integer: values.Add(Tuple.Create(StorageType.Integer, mapping.TargetName, (object)sourceParameter.AsInteger())); break;
                        case StorageType.Double: values.Add(Tuple.Create(StorageType.Double, mapping.TargetName, (object)sourceParameter.AsDouble())); break;
                    }
                }
                catch { }
            }
            if (values.Count == 0) return 0;

            string suffix = "_KStruct_" + source.Id.Value.ToString(CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string baseName = new string((targetType.Name ?? "Type").Select(character =>
                char.IsLetterOrDigit(character) || character == '_' || character == '-' ? character : '_').ToArray());
            ElementType duplicate = null;
            try
            {
                duplicate = targetType.Duplicate(baseName + suffix) as ElementType;
                if (duplicate == null) return 0;
                int copied = 0;
                foreach (Tuple<StorageType, string, object> value in values)
                {
                    Parameter parameter = duplicate.GetParameters(value.Item2 ?? "").FirstOrDefault(candidate =>
                        candidate.Definition != null && !candidate.IsReadOnly && candidate.StorageType == value.Item1);
                    if (parameter == null) continue;
                    bool changed = false;
                    try
                    {
                        switch (value.Item1)
                        {
                            case StorageType.String: changed = parameter.Set((string)value.Item3); break;
                            case StorageType.Integer: changed = parameter.Set((int)value.Item3); break;
                            case StorageType.Double: changed = parameter.Set((double)value.Item3); break;
                        }
                    }
                    catch { continue; }
                    if (changed) copied++;
                }
                if (copied == 0)
                {
                    target.Document.Delete(duplicate.Id);
                    return 0;
                }
                ElementId originalElementId = target.Id;
                ElementId changedElementId = target.ChangeTypeId(duplicate.Id);
                Element changedTarget = target.Document.GetElement(
                    changedElementId != ElementId.InvalidElementId ? changedElementId : originalElementId);
                if (changedTarget != null) target = changedTarget;
                return copied;
            }
            catch
            {
                if (duplicate != null && target.Document.GetElement(duplicate.Id) != null)
                    target.Document.Delete(duplicate.Id);
                throw;
            }
        }

        private static int CopyPriority(Element element)
        {
            if (element.Category == null) return 100;
            long category = element.Category.Id.Value;
            if (category == (long)BuiltInCategory.OST_Walls || category == (long)BuiltInCategory.OST_Floors ||
                category == (long)BuiltInCategory.OST_StructuralFraming || category == (long)BuiltInCategory.OST_StructuralColumns ||
                category == (long)BuiltInCategory.OST_StructuralFoundation) return 0;
            if (category == (long)BuiltInCategory.OST_Doors || category == (long)BuiltInCategory.OST_Windows) return 1;
            return 2;
        }

        private static bool Intersects(BoundingBoxXYZ zone, BoundingBoxXYZ source, Transform transform)
        {
            if (zone == null || source == null) return false;
            XYZ[] points = new XYZ[]
            {
                new XYZ(source.Min.X, source.Min.Y, source.Min.Z), new XYZ(source.Min.X, source.Min.Y, source.Max.Z),
                new XYZ(source.Min.X, source.Max.Y, source.Min.Z), new XYZ(source.Min.X, source.Max.Y, source.Max.Z),
                new XYZ(source.Max.X, source.Min.Y, source.Min.Z), new XYZ(source.Max.X, source.Min.Y, source.Max.Z),
                new XYZ(source.Max.X, source.Max.Y, source.Min.Z), new XYZ(source.Max.X, source.Max.Y, source.Max.Z)
            }.Select(point => transform.OfPoint((source.Transform ?? Transform.Identity).OfPoint(point))).ToArray();
            double minX = points.Min(point => point.X), maxX = points.Max(point => point.X);
            double minY = points.Min(point => point.Y), maxY = points.Max(point => point.Y);
            double minZ = points.Min(point => point.Z), maxZ = points.Max(point => point.Z);
            BoundingBoxXYZ worldZone = ToWorldBounds(zone);
            if (worldZone == null) return false;
            return worldZone.Min.X <= maxX && worldZone.Max.X >= minX &&
                worldZone.Min.Y <= maxY && worldZone.Max.Y >= minY &&
                worldZone.Min.Z <= maxZ && worldZone.Max.Z >= minZ;
        }

        private static BoundingBoxXYZ ToWorldBounds(BoundingBoxXYZ box)
        {
            if (box == null) return null;
            Transform transform = box.Transform ?? Transform.Identity;
            XYZ[] points = new XYZ[]
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            }.Select(transform.OfPoint).ToArray();
            return new BoundingBoxXYZ
            {
                Min = new XYZ(points.Min(point => point.X), points.Min(point => point.Y), points.Min(point => point.Z)),
                Max = new XYZ(points.Max(point => point.X), points.Max(point => point.Y), points.Max(point => point.Z))
            };
        }

        private static Schema GetCopySchema()
        {
            Schema existing = Schema.Lookup(CopySchemaId);
            if (existing != null) return existing;
            var builder = new SchemaBuilder(CopySchemaId);
            builder.SetSchemaName("KStructSplitZoneLinkedCopy");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("ZoneName", typeof(string));
            builder.AddSimpleField("SourceLinkUniqueId", typeof(string));
            builder.AddSimpleField("SourceElementUniqueId", typeof(string));
            return builder.Finish();
        }

        private static void AddCopyTag(Element element, Schema schema, string zoneName,
            string sourceLinkUniqueId, string sourceElementUniqueId)
        {
            var entity = new Entity(schema);
            entity.Set(schema.GetField("ZoneName"), zoneName);
            entity.Set(schema.GetField("SourceLinkUniqueId"), sourceLinkUniqueId);
            entity.Set(schema.GetField("SourceElementUniqueId"), sourceElementUniqueId ?? "");
            element.SetEntity(entity);
        }

        private static bool HasCopyTag(Element element, Schema schema, string zoneName, string linkUniqueId)
        {
            Entity entity = element.GetEntity(schema);
            if (!entity.IsValid()) return false;
            return string.Equals(entity.Get<string>(schema.GetField("ZoneName")), zoneName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entity.Get<string>(schema.GetField("SourceLinkUniqueId")), linkUniqueId, StringComparison.Ordinal);
        }
    }
}
