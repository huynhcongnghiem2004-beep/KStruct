using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SDK_HD.KStruct.NativeRebar
{
    [Serializable]
    public sealed class FootingRebarProfile
    {
        public string Name { get; set; }
        public string TypeXName { get; set; }
        public string TypeYName { get; set; }
        public double CoverBottomMm { get; set; }
        public double CoverTopMm { get; set; }
        public double CoverSideMm { get; set; }
        public double SpacingXmm { get; set; }
        public double SpacingYmm { get; set; }
        public bool Bottom { get; set; }
        public bool Middle { get; set; }
        public bool Top { get; set; }
        public bool SideVertical { get; set; }
        public bool SideHorizontal { get; set; }
        public string OStirrupHookTypeName { get; set; }
        public bool SideCStirrups { get; set; }
        public bool CStirrupHookX { get; set; }
        public bool CStirrupHookY { get; set; }
        public double HorizontalMaxSpacingMm { get; set; }
        // Retained so older XML presets still deserialize; new presets use HorizontalMaxSpacingMm.
        public int HorizontalLayerQuantity { get; set; }
        public double CStirrupLegXmm { get; set; }
        public double CStirrupLegYmm { get; set; }
        public bool ColumnDowels { get; set; }
        public double DowelAnchorageMm { get; set; }
        public double DowelProjectionMm { get; set; }
        public string BottomHookXName { get; set; }
        public string BottomHookYName { get; set; }
        public string MiddleHookXName { get; set; }
        public string MiddleHookYName { get; set; }
        public string TopHookXName { get; set; }
        public string TopHookYName { get; set; }
        public double BottomHookXLengthMm { get; set; }
        public double BottomHookYLengthMm { get; set; }
        public double MiddleHookXLengthMm { get; set; }
        public double MiddleHookYLengthMm { get; set; }
        public double TopHookXLengthMm { get; set; }
        public double TopHookYLengthMm { get; set; }
    }

    [Serializable]
    public sealed class FootingRebarProfileFile
    {
        public List<FootingRebarProfile> Profiles { get; set; } = new List<FootingRebarProfile>();
    }

    internal static class FootingRebarProfileStore
    {
        internal static List<FootingRebarProfile> Load()
        {
            string path = GetPath();
            if (!File.Exists(path)) return new List<FootingRebarProfile>();
            try { return Read(path); }
            catch { return new List<FootingRebarProfile>(); }
        }

        internal static List<FootingRebarProfile> Import(string path) => Read(path);

        internal static void Export(string path, IEnumerable<FootingRebarProfile> profiles) => Write(path, profiles);

        internal static void Save(IEnumerable<FootingRebarProfile> profiles)
        {
            string path = GetPath();
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            Write(path, profiles);
        }

        private static List<FootingRebarProfile> Read(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                var saved = (FootingRebarProfileFile)new XmlSerializer(typeof(FootingRebarProfileFile)).Deserialize(stream);
                return (saved?.Profiles ?? new List<FootingRebarProfile>())
                    .Where(profile => profile != null && !string.IsNullOrWhiteSpace(profile.Name))
                    .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }

        private static void Write(string path, IEnumerable<FootingRebarProfile> profiles)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            using (FileStream stream = File.Create(temporary))
                new XmlSerializer(typeof(FootingRebarProfileFile)).Serialize(stream,
                    new FootingRebarProfileFile { Profiles = profiles.Where(profile => profile != null).ToList() });
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        private static string GetPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KStruct", "FootingRebarProfiles.xml");
        }
    }
}
