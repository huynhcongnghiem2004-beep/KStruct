using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using SDK_HD.KStruct.Core.Storage;

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
        public string RebarMultiTagTypeName { get; set; }
        public string RebarMultiTagTypeUniqueId { get; set; }
        public string RebarTagFamily { get; set; } // Legacy independent-tag fields.
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
        // Pose recorded with each generated view, for moving existing Detail views without replacing their annotations.
        public bool HostPoseRecorded { get; set; }
        public double HostPoseX { get; set; }
        public double HostPoseY { get; set; }
        public double HostPoseZ { get; set; }
        public double HostPoseAngle { get; set; }
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
        public double CropMarginMm { get; set; } = 6;
        public bool CreateCallouts { get; set; } = true;
        public bool CreateSections { get; set; } = true; // Legacy switch, used when per-axis options are absent.
        public bool? CreateDetailA { get; set; }
        public bool? CreateDetailB { get; set; }
        [XmlIgnore] public bool WantsDetailA => CreateDetailA ?? CreateSections;
        [XmlIgnore] public bool WantsDetailB => CreateDetailB ?? CreateSections;
        [XmlIgnore] public bool HasDetails => WantsDetailA || WantsDetailB;
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

        // Shared DTD policy applies to both imported and local profile XML.
        private static List<FootingDrawingProfile> ReadProfiles(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = SafeXmlReader.Create(stream, 4 * 1024 * 1024))
            {
                var file = (FootingDrawingProfileFile)new XmlSerializer(typeof(FootingDrawingProfileFile)).Deserialize(reader);
                return (file?.Profiles ?? new List<FootingDrawingProfile>())
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name)).ToList();
            }
        }

        internal static List<FootingDrawingProfile> Load()
        {
            if (!File.Exists(FilePath)) return new List<FootingDrawingProfile>();
            try { return ReadProfiles(FilePath); }
            catch { return new List<FootingDrawingProfile>(); } // Preserve the existing fallback for broken local presets.
        }

        internal static List<FootingDrawingProfile> Import(string path) => ReadProfiles(path);

        private static void WriteProfiles(string path, IEnumerable<FootingDrawingProfile> profiles)
        {
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            var data = new FootingDrawingProfileFile { Profiles = profiles.Where(x => x != null).ToList() };
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temporary))
                    new XmlSerializer(typeof(FootingDrawingProfileFile)).Serialize(stream, data);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        internal static void Export(string path, IEnumerable<FootingDrawingProfile> profiles) =>
            WriteProfiles(path, profiles);

        internal static void Save(IEnumerable<FootingDrawingProfile> profiles) =>
            WriteProfiles(FilePath, profiles);
    }
}


