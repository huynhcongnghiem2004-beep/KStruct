using System;
using System.IO;
using System.Xml.Serialization;

namespace ColumnRebarPro.V3
{
    internal static class V3LegacySplice
    {
        internal static bool NeedsLength(V3RebarConfig c) => c != null &&
            c.Splice == V3SpliceStyle.AtStoreyJoint &&
            (double.IsNaN(c.StarterLength) || double.IsInfinity(c.StarterLength) || c.StarterLength <= 0);

        internal static V3RebarConfig Normalize(V3RebarConfig c)
        {
            if (c == null || c.Splice != V3SpliceStyle.AtStoreyJoint) return c;
            c.StarterBars = true;
            if (NeedsLength(c))
                c.LegacyConnectionNotice = "Cấu hình nối cũ: nhập chiều dài chờ tính từ đỉnh cột dưới. Chiều dài nối cũ không được tự đổi thành chiều dài chờ.";
            else
            {
                c.Splice = V3SpliceStyle.None;
                c.LegacyConnectionNotice = "Đã chuyển cấu hình nối cũ sang nhấn cổ chai; giữ nguyên chiều dài chờ đã nhập.";
            }
            return c;
        }

        // Shared by the last-used XML file and Revit ExtensibleStorage XML.
        internal static V3RebarConfig Read(TextReader reader) =>
            Normalize((V3RebarConfig)new XmlSerializer(typeof(V3RebarConfig)).Deserialize(reader));

        internal static void ValidateForCreation(V3RebarConfig c)
        {
            Normalize(c);
            if (NeedsLength(c)) throw new InvalidOperationException(c.LegacyConnectionNotice);
            if (c.Splice != V3SpliceStyle.None)
                throw new InvalidOperationException("Chế độ nối không được nhận diện; chọn lại cấu hình thép chờ.");
        }
    }
}
