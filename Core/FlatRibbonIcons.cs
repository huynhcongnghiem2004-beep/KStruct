using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace SDK_HD.KStruct
{
    /// <summary>
    /// Loads the pre-rendered, compact PNG used by each KStruct ribbon command.
    /// The PNGs are embedded in the add-in so Revit never depends on an external folder.
    /// </summary>
    internal static class FlatRibbonIcons
    {
        private static readonly Dictionary<string, BitmapImage> Cache =
            new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();
        private static readonly string[] ResourceNames = Assembly.GetManifestResourceNames();

        internal static BitmapImage Create(string commandId, string family, int pixels)
        {
            if (pixels != 16 && pixels != 32)
                throw new ArgumentOutOfRangeException(nameof(pixels), "Ribbon PNGs must be 16px or 32px.");

            string commandFile = (commandId ?? string.Empty) + "_" + pixels + ".png";
            BitmapImage image = Load(commandFile);
            if (image != null) return image;

            string familyFile = (family ?? "RebarColumnV3") + "_" + pixels + ".png";
            image = Load(familyFile);
            if (image != null) return image;

            throw new InvalidOperationException(
                "Không tìm thấy PNG icon KStruct nhúng: " + commandFile +
                " (fallback: " + familyFile + "). Hãy chạy Resources\\Generate-KStructIcons.ps1 rồi build lại.");
        }

        private static BitmapImage Load(string fileName)
        {
            string resource = ResourceNames.FirstOrDefault(name =>
                name.EndsWith(".Resources.Ribbon." + fileName, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".Resources." + fileName, StringComparison.OrdinalIgnoreCase));
            if (resource == null) return null;

            lock (Cache)
            {
                if (Cache.TryGetValue(resource, out BitmapImage cached)) return cached;
                using (Stream stream = Assembly.GetManifestResourceStream(resource))
                {
                    if (stream == null) return null;
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    Cache.Add(resource, image);
                    return image;
                }
            }
        }
    }
}
