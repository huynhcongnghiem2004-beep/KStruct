using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Serializable]
    public sealed class FootingDrawingProfile
    {
        public string Name { get; set; }
        public string PlanTypeName { get; set; }
        public string SectionTypeName { get; set; } // Legacy XML only; new commands select DetailTypeName.
        public string DetailTypeName { get; set; }
        public string TitleBlockFamily { get; set; }
        public string TitleBlockType { get; set; }
        public string PlanTemplateName { get; set; }
        public string SectionTemplateName { get; set; }
        public string RebarTagFamily { get; set; }
        public string RebarTagType { get; set; }
        public string FoundationTagFamily { get; set; }
        public string FoundationTagType { get; set; }
        public string DimensionTypeName { get; set; }
        public bool ApplyViewTemplatesOnce { get; set; } = true;
        public bool CreateBeamBreakLines { get; set; } = true;
        public string BreakLineFamily { get; set; }
        public string BreakLineType { get; set; }
        public double BreakLinePaddingPaperMm { get; set; } = 1;
        public bool ReverseBreakLineMask { get; set; }
        public bool UseDetailedLayout { get; set; }
        public bool DimensionFourSides { get; set; }
        public double DimensionOffsetPaperMm { get; set; } = 8;
        public double DimensionGapPaperMm { get; set; } = 6;
        public double SheetRightReserveMm { get; set; } = 70;
        public double SheetBottomReserveMm { get; set; } = 35;
        public double SheetMarginMm { get; set; } = 10;
        public double SectionDepthMm { get; set; } = 1200;
        public double SectionXOffsetMm { get; set; }
        public double SectionYOffsetMm { get; set; }
        public bool ReverseSectionX { get; set; }
        public bool ReverseSectionY { get; set; }
        public string SourceViewUniqueId { get; set; }
        public string OutputLayers { get; set; } = "Both";
        public string SheetPrefix { get; set; } = "KSMD-";
        public string SheetNamePattern { get; set; } = "Móng đơn - {Mark}";
        public string ViewNamePattern { get; set; } = "KStruct - MĐ {Id} - {Role}";
        public int PlanScale { get; set; } = 20;
        public int SectionScale { get; set; } = 20;
        public double CropMarginMm { get; set; } = 300;
        public bool CreateCallouts { get; set; } = true;
        public bool CreateSections { get; set; } = true;
        public bool CreateDimensions { get; set; } = true;
        public bool CreateTags { get; set; } = true;
        public bool PlaceOnNewSheet { get; set; } = true;
        public string ExistingSheetUniqueId { get; set; }
    }

    [Serializable]
    public sealed class FootingDrawingProfileFile
    {
        public List<FootingDrawingProfile> Profiles { get; set; } = new List<FootingDrawingProfile>();
    }

    internal static class FootingDrawingProfileStore
    {
        private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KStruct", "FootingDrawingProfiles.xml");

        internal static List<FootingDrawingProfile> Load()
        {
            if (!File.Exists(FilePath)) return new List<FootingDrawingProfile>();
            try
            {
                using (var stream = File.OpenRead(FilePath))
                {
                    var file = (FootingDrawingProfileFile)new XmlSerializer(typeof(FootingDrawingProfileFile)).Deserialize(stream);
                    return (file?.Profiles ?? new List<FootingDrawingProfile>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name)).ToList();
                }
            }
            catch { return new List<FootingDrawingProfile>(); }
        }

        internal static List<FootingDrawingProfile> Import(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var file = (FootingDrawingProfileFile)new XmlSerializer(typeof(FootingDrawingProfileFile)).Deserialize(stream);
                return (file?.Profiles ?? new List<FootingDrawingProfile>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name)).ToList();
            }
        }

        internal static void Export(string path, IEnumerable<FootingDrawingProfile> profiles)
        {
            using (var stream = File.Create(path))
                new XmlSerializer(typeof(FootingDrawingProfileFile)).Serialize(stream, new FootingDrawingProfileFile { Profiles = profiles.Where(x => x != null).ToList() });
        }

        internal static void Save(IEnumerable<FootingDrawingProfile> profiles)
        {
            string directory = Path.GetDirectoryName(FilePath);
            Directory.CreateDirectory(directory);
            string temporary = FilePath + ".tmp";
            using (var stream = File.Create(temporary))
                new XmlSerializer(typeof(FootingDrawingProfileFile)).Serialize(stream, new FootingDrawingProfileFile { Profiles = profiles.Where(x => x != null).ToList() });
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }
    }
}


