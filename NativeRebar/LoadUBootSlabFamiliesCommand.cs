using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class LoadUBootSlabFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                string folder = Path.Combine(Path.GetDirectoryName(typeof(LoadUBootSlabFamiliesCommand).Assembly.Location),
                    "Resources", "SlabModules", "UBoot");
                if (!Directory.Exists(folder))
                    throw new DirectoryNotFoundException("Không tìm thấy tài nguyên family U-Boot đi kèm KStruct: " + folder);
                string[] files = Directory.GetFiles(folder, "BS_UBoot_*_Solid.rfa", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).ToArray();
                if (files.Length == 0) throw new FileNotFoundException("Không có family U-Boot trong thư mục tài nguyên KStruct.", folder);

                int loaded = 0, alreadyLoaded = 0;
                var details = new System.Collections.Generic.List<string>();
                foreach (string path in files)
                {
                    string requestedName = Path.GetFileNameWithoutExtension(path);
                    bool exists = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                        .Any(symbol => string.Equals(symbol.FamilyName, requestedName, StringComparison.OrdinalIgnoreCase));
                    if (exists)
                    {
                        alreadyLoaded++;
                        continue;
                    }
                    Family family;
                    if (!doc.LoadFamily(path, out family) || family == null)
                        throw new InvalidOperationException("Revit không nạp được family " + Path.GetFileName(path) + ".");
                    loaded++;
                    details.Add(family.Name + ": " + family.GetFamilySymbolIds().Count + " type");
                }

                string report = "Đã nạp " + loaded + " family U-Boot vào project; đã có sẵn: " + alreadyLoaded + ".";
                if (details.Count > 0) report += "\n" + string.Join("\n", details);
                report += "\nSau đó dùng lệnh KStruct ‘Bố trí module sàn’ để chọn type và bước lưới.";
                TaskDialog.Show("KStruct - Family U-Boot", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Family U-Boot", ex.Message);
                return Result.Failed;
            }
        }
    }
}
