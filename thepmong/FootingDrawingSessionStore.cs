using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Serializable]
    public sealed class FootingDrawingSession
    {
        public bool Remember { get; set; } = true;
        public FootingDrawingProfile Profile { get; set; }
    }

    // Per-project preferences, separate from named presets and Revit model storage.
    internal static class FootingDrawingSessionStore
    {
        internal static string PathFor(string projectKey, string directory = null)
        {
            if (string.IsNullOrWhiteSpace(projectKey)) throw new ArgumentException("Thiếu mã project.", nameof(projectKey));
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(projectKey))).Replace("-", "");
            return Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KStruct", "FootingDrawingSessions"), hash + ".xml");
        }

        internal static FootingDrawingSession Load(string projectKey, string directory = null)
        {
            string path = PathFor(projectKey, directory);
            if (!File.Exists(path)) return null;
            using (var stream = File.OpenRead(path))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 4 * 1024 * 1024
            }))
                return (FootingDrawingSession)new XmlSerializer(typeof(FootingDrawingSession)).Deserialize(reader);
        }

        internal static void Save(string projectKey, FootingDrawingSession session, string directory = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            string path = PathFor(projectKey, directory);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temporary))
                    new XmlSerializer(typeof(FootingDrawingSession)).Serialize(stream, session);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
