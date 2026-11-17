using System;
using System.IO;
using System.Text;

namespace SDK_HD.KStruct.Core.V5
{
    /// <summary>Stages two report files and restores any previous versions on ordinary I/O failures.
    /// A power loss between copies cannot be atomic across two different file names.</summary>
    internal static class AuditPairPublisher
    {
        internal static void Write(string jsonPath, string csvPath, Action<TextWriter,TextWriter> generate)
        {
            if (string.IsNullOrWhiteSpace(jsonPath) || string.IsNullOrWhiteSpace(csvPath))
                throw new ArgumentException("Audit report paths must not be empty.");
            if (string.Equals(Path.GetFullPath(jsonPath),Path.GetFullPath(csvPath),
                StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("JSON and CSV must use different paths.");
            if (generate == null) throw new ArgumentNullException(nameof(generate));
            string token = "." + Guid.NewGuid().ToString("N") + ".audit";
            string stagedJson = jsonPath + token + ".tmp";
            string stagedCsv = csvPath + token + ".tmp";
            string backupJson = jsonPath + token + ".bak";
            string backupCsv = csvPath + token + ".bak";
            bool hadJson = File.Exists(jsonPath), hadCsv = File.Exists(csvPath);
            bool promotionAttempted = false, restored = false;
            try
            {
                using (var json = new StreamWriter(
                    new FileStream(stagedJson,FileMode.CreateNew,FileAccess.Write,FileShare.None),
                    new UTF8Encoding(false)))
                using (var csv = new StreamWriter(
                    new FileStream(stagedCsv,FileMode.CreateNew,FileAccess.Write,FileShare.None),
                    new UTF8Encoding(true)))
                    generate(json,csv);

                // Do not modify an existing report until the entire pair is generated.
                if(hadJson) File.Copy(jsonPath,backupJson,false);
                if(hadCsv) File.Copy(csvPath,backupCsv,false);
                promotionAttempted = true;
                File.Copy(stagedJson,jsonPath,true);
                File.Copy(stagedCsv,csvPath,true);
                restored = true;
            }
            catch
            {
                if(promotionAttempted)
                {
                    try
                    {
                        if(hadJson) File.Copy(backupJson,jsonPath,true);
                        else if(File.Exists(jsonPath)) File.Delete(jsonPath);
                        if(hadCsv) File.Copy(backupCsv,csvPath,true);
                        else if(File.Exists(csvPath)) File.Delete(csvPath);
                        restored = true;
                    }
                    catch
                    {
                        // Preserve backups for manual recovery if rollback cannot complete.
                        restored = false;
                    }
                }
                throw;
            }
            finally
            {
                TryDelete(stagedJson);
                TryDelete(stagedCsv);
                if(restored || !promotionAttempted)
                {
                    TryDelete(backupJson);
                    TryDelete(backupCsv);
                }
            }
        }
        private static void TryDelete(string path)
        {
            try { if(File.Exists(path)) File.Delete(path); }
            catch { /* A leftover temporary file cannot corrupt the destination. */ }
        }
    }
}
