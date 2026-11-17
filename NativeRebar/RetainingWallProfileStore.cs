using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace SDK_HD.KStruct.NativeRebar
{
    [Serializable]
    public sealed class RetainingWallProfile
    {
        public string Name { get; set; }
        public string VerticalA { get; set; }
        public string VerticalB { get; set; }
        public string HorizontalA { get; set; }
        public string HorizontalB { get; set; }
        public string FootingType { get; set; }
        public string FootingTypeAlong { get; set; }
        public string FootingTypeCross { get; set; }
        public string ConstructiveType { get; set; }
        public string ConstructiveHook { get; set; }
        public bool DrawConstructive { get; set; }
        public bool DrawAdditional { get; set; }
        public int AdditionalSide { get; set; }
        public string TopHook { get; set; }
        public string BottomHook { get; set; }
        public bool ReplaceExisting { get; set; }
        public double CoverMm { get; set; }
        public double CoverTopBottomMm { get; set; }
        public double CoverFacesMm { get; set; }
        public double CoverStartEndMm { get; set; }
        public double TopOffsetMm { get; set; }
        public double BottomOffsetMm { get; set; }
        public double VerticalSpacingMm { get; set; }
        public double HorizontalSpacingMm { get; set; }
        public double BaseAnchorMm { get; set; }
        public double FootingSpacingMm { get; set; }
        public double FootingSpacingAlongMm { get; set; }
        public double FootingSpacingCrossMm { get; set; }
        public double ConstructiveSpacingMm { get; set; }
        public double AdditionalHeightMm { get; set; }
        public double TopHookLengthMm { get; set; }
        public double BottomHookLengthMm { get; set; }
        public double HorizontalOffsetStartMm { get; set; }
        public double HorizontalOffsetEndMm { get; set; }
        public bool PickSpanByPoints { get; set; }
    }

    [Serializable]
    public sealed class RetainingWallProfileFile
    {
        public List<RetainingWallProfile> Profiles { get; set; } = new List<RetainingWallProfile>();
    }

    internal static class RetainingWallProfileStore
    {
        internal static List<RetainingWallProfile> Load()
        {
            string path = GetPath();
            if (!File.Exists(path)) return new List<RetainingWallProfile>();
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    var saved = (RetainingWallProfileFile)new XmlSerializer(typeof(RetainingWallProfileFile)).Deserialize(stream);
                    return saved?.Profiles ?? new List<RetainingWallProfile>();
                }
            }
            catch { return new List<RetainingWallProfile>(); }
        }

        internal static void Save(IEnumerable<RetainingWallProfile> profiles)
        {
            string path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (FileStream stream = File.Create(temporary))
                new XmlSerializer(typeof(RetainingWallProfileFile)).Serialize(stream, new RetainingWallProfileFile { Profiles = profiles.ToList() });
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        private static string GetPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KStruct", "RetainingWallProfiles.xml");
        }
    }
}
