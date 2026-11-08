using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace SDK_HD.KStruct.FootingRebar
{
    [Serializable]
    public sealed class FootingBarPreference
    {
        public bool Enabled { get; set; }
        public double Diameter { get; set; } = 12;
        public double Spacing { get; set; } = 200;
        public SpacingMode Mode { get; set; }
        public HookTypeOption HookType { get; set; }
        public double HookLength { get; set; }

        internal static FootingBarPreference From(BarLayerSettings s) => new FootingBarPreference
        { Enabled = s.Enabled, Diameter = s.Diameter, Spacing = s.Spacing, Mode = s.Mode, HookType = s.HookType, HookLength = s.HookLength };

        internal BarLayerSettings ToSettings()
        {
            if (!Finite(Diameter) || !Finite(Spacing) || !Finite(HookLength) || Diameter <= 0 || Spacing <= 0 || HookLength < 0 ||
                !Enum.IsDefined(typeof(SpacingMode), Mode) || !Enum.IsDefined(typeof(HookTypeOption), HookType))
                throw new InvalidDataException("Thông số thép đã lưu không hợp lệ.");
            return new BarLayerSettings(Enabled, Diameter, Spacing, Mode, HookType != HookTypeOption.None, HookLength, HookType);
        }
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }

    [Serializable]
    public sealed class FootingRebarPreference
    {
        public int Version { get; set; } = 1;
        public FootingType FootingType { get; set; }
        public double CoverBottom { get; set; }
        public double CoverTop { get; set; }
        public double CoverSide { get; set; }
        public bool HasLeanConcrete { get; set; }
        public FootingBarPreference BottomX { get; set; }
        public FootingBarPreference BottomY { get; set; }
        public bool TopEnabled { get; set; }
        public FootingBarPreference TopX { get; set; }
        public FootingBarPreference TopY { get; set; }
        public bool SideTiesEnabled { get; set; }
        public double TieDiameter { get; set; } = 10;
        public double TieSpacing { get; set; } = 200;
        public bool HasCStirrups { get; set; } = true;

        internal static FootingRebarPreference From(FootingRebarFullSettings s) => new FootingRebarPreference
        {
            FootingType = s.FootingType, CoverBottom = s.Cover.Bottom, CoverTop = s.Cover.Top, CoverSide = s.Cover.Side,
            HasLeanConcrete = s.Cover.HasLeanConcrete, BottomX = FootingBarPreference.From(s.Bottom.AlongX), BottomY = FootingBarPreference.From(s.Bottom.AlongY),
            TopEnabled = s.Top.Enabled, TopX = FootingBarPreference.From(s.Top.AlongX), TopY = FootingBarPreference.From(s.Top.AlongY),
            SideTiesEnabled = s.SideTies.Enabled, TieDiameter = s.SideTies.Diameter, TieSpacing = s.SideTies.Spacing, HasCStirrups = s.SideTies.HasCStirrups
        };

        internal FootingRebarFullSettings ToSettings()
        {
            if (Version != 1 || !Enum.IsDefined(typeof(FootingType), FootingType) || BottomX == null || BottomY == null || TopX == null || TopY == null ||
                !Positive(TieDiameter) || !Positive(TieSpacing)) throw new InvalidDataException("Phiên bản/cấu hình thép đã lưu không được hỗ trợ.");
            if (TopX.Enabled != TopEnabled || TopY.Enabled != TopEnabled)
                throw new InvalidDataException("Cấu hình lớp thép trên đã lưu không đồng nhất.");
            if (!BottomX.Enabled && !BottomY.Enabled) throw new InvalidDataException("Cấu hình đã lưu không có thép đáy.");
            return new FootingRebarFullSettings(FootingType, new CoverSettings(CoverBottom, CoverTop, CoverSide, HasLeanConcrete),
                new BottomLayerSettings(BottomX.ToSettings(), BottomY.ToSettings()),
                new TopLayerSettings(TopEnabled, TopX.ToSettings(), TopY.ToSettings()),
                new SideTiesSettings(SideTiesEnabled, TieDiameter, TieSpacing, HasCStirrups));
        }
        private static bool Positive(double x) => !double.IsNaN(x) && !double.IsInfinity(x) && x > 0;

        internal IEnumerable<double> EnabledDiameters()
        {
            if (BottomX.Enabled) yield return BottomX.Diameter;
            if (BottomY.Enabled) yield return BottomY.Diameter;
            if (TopEnabled) { yield return TopX.Diameter; yield return TopY.Diameter; }
            if (SideTiesEnabled) yield return TieDiameter;
        }
    }

    [Serializable]
    public sealed class FootingRebarSession
    {
        public bool Remember { get; set; } = true;
        public FootingRebarPreference Preference { get; set; }
    }

    internal static class FootingRebarPreferenceStore
    {
        internal static string Serialize(FootingRebarPreference preference)
        {
            preference.ToSettings();
            using (var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture))
            { new XmlSerializer(typeof(FootingRebarPreference)).Serialize(writer, preference); return writer.ToString(); }
        }
        internal static FootingRebarPreference Deserialize(string xml)
        {
            using (var source = new StringReader(xml ?? string.Empty))
            using (var reader = XmlReader.Create(source, ReaderSettings()))
            {
                var result = (FootingRebarPreference)new XmlSerializer(typeof(FootingRebarPreference)).Deserialize(reader);
                result.ToSettings(); return result;
            }
        }
        private static XmlReaderSettings ReaderSettings() => new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };
        internal static string PathFor(string projectKey, FootingType kind, string directory = null)
        {
            if (string.IsNullOrWhiteSpace(projectKey)) throw new ArgumentException("Thiếu mã project.");
            string key;
            using (var hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(projectKey + "|" + kind))).Replace("-", "");
            return Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KStruct", "FootingRebarSessions"), key + ".xml");
        }
        internal static FootingRebarSession Load(string projectKey, FootingType kind, string directory = null)
        {
            string path = PathFor(projectKey, kind, directory);
            if (!File.Exists(path)) return null;
            using (var reader = XmlReader.Create(path, ReaderSettings()))
            {
                var result = (FootingRebarSession)new XmlSerializer(typeof(FootingRebarSession)).Deserialize(reader);
                if (result.Remember && result.Preference != null)
                {
                    result.Preference.ToSettings();
                    if (result.Preference.FootingType != kind) throw new InvalidDataException("Cấu hình đã lưu thuộc loại móng khác.");
                }
                return result;
            }
        }
        internal static void Save(string projectKey, FootingType kind, FootingRebarSession session, string directory = null)
        {
            if (session.Remember)
            {
                if (session.Preference == null || session.Preference.FootingType != kind) throw new InvalidDataException("Thiếu cấu hình đúng loại móng.");
                session.Preference.ToSettings();
            }
            var persisted = new FootingRebarSession { Remember = session.Remember, Preference = session.Remember ? session.Preference : null };
            string path = PathFor(projectKey, kind, directory);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temporary)) new XmlSerializer(typeof(FootingRebarSession)).Serialize(stream, persisted);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal static class FootingRebarOwnership
    {
        internal const string ToolId = "KStruct.FootingRebarCommand";
        internal static bool IsOwned(string actualElement, string actualHost, string storedElement, string storedHost, string tool, int version) =>
            version == 1 && tool == ToolId && !string.IsNullOrWhiteSpace(actualElement) && !string.IsNullOrWhiteSpace(actualHost) &&
            actualElement == storedElement && actualHost == storedHost;
    }
}
