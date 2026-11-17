using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreatePlansForLevelsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .WhereElementIsNotElementType().Cast<Level>().OrderBy(level => level.Elevation).ToList();
                if (levels.Count == 0) throw new InvalidOperationException("Project chưa có Level.");

                ViewFamilyType planType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().FirstOrDefault(type => type.ViewFamily == ViewFamily.FloorPlan)
                    ?? new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .FirstOrDefault(type => type.ViewFamily == ViewFamily.StructuralPlan);
                if (planType == null) throw new InvalidOperationException("Project không có ViewFamilyType mặt bằng.");

                HashSet<ElementId> levelsWithPlan = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>().Where(view => !view.IsTemplate && view.GenLevel != null)
                    .Select(view => view.GenLevel.Id).ToHashSet();
                HashSet<string> names = new FilteredElementCollector(doc).OfClass(typeof(View))
                    .Cast<View>().Select(view => view.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int created = 0;
                var skipped = new List<string>();

                using (Transaction transaction = new Transaction(doc, "KStruct - Tạo mặt bằng cho Level"))
                {
                    transaction.Start();
                    foreach (Level level in levels)
                    {
                        if (levelsWithPlan.Contains(level.Id)) continue;
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                ViewPlan view = ViewPlan.Create(doc, planType.Id, level.Id);
                                string baseName = level.Name + " - Mặt bằng";
                                view.Name = UniqueName(baseName, names);
                                names.Add(view.Name);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận mặt bằng cho Level " + level.Name + ".");
                                created++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add(level.Name + ": " + ex.Message);
                            }
                        }
                    }
                    if (created == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Mặt bằng Level", "Không có Level mới cần tạo mặt bằng.");
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các mặt bằng mới.");
                }

                string report = "Đã tạo " + created + " mặt bằng cho Level chưa có plan view.";
                if (skipped.Count > 0) report += "\nKhông tạo được " + skipped.Count + " Level; xem thông báo lỗi trong kết quả lệnh.";
                TaskDialog.Show("KStruct - Mặt bằng Level", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Mặt bằng Level", ex.Message);
                return Result.Failed;
            }
        }

        private static string UniqueName(string baseName, HashSet<string> existing)
        {
            if (!existing.Contains(baseName)) return baseName;
            int index = 2;
            string name;
            do { name = baseName + " (" + index++ + ")"; } while (existing.Contains(name));
            return name;
        }
    }
}