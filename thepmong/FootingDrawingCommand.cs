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
    public sealed class FootingDrawingCommand : IExternalCommand
    {
        private sealed class FoundationFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                FamilyInstance instance = element as FamilyInstance;
                return instance != null && instance.Category != null &&
                    instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation &&
                    instance.Location is LocationPoint && instance.get_BoundingBox(null) != null;
            }
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
                if (footings.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FoundationFilter(), "Chọn móng đơn hoặc đài cọc cần triển khai bản vẽ");
                    footings = picked.Select(x => doc.GetElement(x)).OfType<FamilyInstance>().Where(IsFoundation).Distinct().ToList();
                }
                if (footings.Count == 0) return Result.Cancelled;

                FootingDrawingSettingsWindow window;
                do
                {
                    window = new FootingDrawingSettingsWindow(doc, data.Application.MainWindowHandle, ui.ActiveView as ViewPlan, footings);
                    if (window.ShowDialog() == true && window.Configuration != null) break;
                    if (!window.RequestSelection) return Result.Cancelled;
                    IList<Reference> repicked = ui.Selection.PickObjects(ObjectType.Element, new FoundationFilter(), "Chọn móng cần tạo hồ sơ");
                    footings = repicked.Select(x => doc.GetElement(x)).OfType<FamilyInstance>().Where(IsFoundation).Distinct().ToList();
                } while (true);
                FootingDrawingConfiguration config = window.Configuration;
                footings = config.SelectedFootings.ToList();
                ViewPlan parent = config.SourcePlan;
                if (config.Profile.CreateCallouts && parent == null)
                {
                    TaskDialog.Show("KStruct - Hồ sơ móng", "Hãy chạy lệnh từ mặt bằng Revit để tạo callout.");
                    return Result.Cancelled;
                }
                if (!config.Profile.PlaceOnNewSheet && footings.Count > 1)
                {
                    TaskDialog.Show("KStruct - Hồ sơ móng", "Khi dùng sheet có sẵn, hãy chọn một móng mỗi lần để tránh chồng viewport.");
                    return Result.Cancelled;
                }
                if (config.Profile.OutputLayers == "None" && !config.Profile.CreateSections)
                {
                    TaskDialog.Show("KStruct - Hồ sơ móng", "Cần bật callout mặt bằng hoặc mặt cắt.");
                    return Result.Cancelled;
                }

                HashSet<string> viewNames = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                HashSet<string> sheetNumbers = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Select(x => x.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var createdSheets = new List<ElementId>();
                var skipped = new List<string>();
                var warnings = new List<string>();
                using (var tx = new Transaction(doc, "KStruct - Tạo hồ sơ móng"))
                {
                    tx.Start();
                    foreach (FamilyInstance footing in footings)
                    {
                        var priorNames = new HashSet<string>(viewNames, StringComparer.OrdinalIgnoreCase);
                        var priorNumbers = new HashSet<string>(sheetNumbers, StringComparer.OrdinalIgnoreCase);
                        using (var sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                ViewSheet sheet = FootingDrawingBuilder.CreateForFooting(doc, parent, footing, config,
                                    viewNames, sheetNumbers, createdSheets.Count, warnings);
                                if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận hồ sơ móng.");
                                if (!createdSheets.Contains(sheet.Id)) createdSheets.Add(sheet.Id);
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                viewNames = priorNames; sheetNumbers = priorNumbers;
                                skipped.Add("Móng " + footing.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (createdSheets.Count == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Hồ sơ móng", "Không tạo được hồ sơ nào.\n" + string.Join("\n", skipped.Take(10)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không commit được hồ sơ móng.");
                }
                ui.Selection.SetElementIds(createdSheets);
                string report = "Đã tạo hồ sơ cho " + (footings.Count - skipped.Count) + "/" + footings.Count + " móng.";
                if (skipped.Count > 0) report += "\nBỏ qua:\n" + string.Join("\n", skipped.Take(10));
                if (warnings.Count > 0) report += "\nCần kiểm tra annotation/template (" + warnings.Count + "):\n" + string.Join("\n", warnings.Distinct().Take(10));
                TaskDialog.Show("KStruct - Hồ sơ móng", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Hồ sơ móng", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsFoundation(FamilyInstance instance) => instance != null && instance.Category != null &&
            instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation && instance.Location is LocationPoint;
    }
}

