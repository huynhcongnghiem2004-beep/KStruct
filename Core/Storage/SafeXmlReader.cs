using System;
using System.IO;
using System.Xml;

namespace SDK_HD.KStruct.Core.Storage
{
    // Central XML policy for persisted KStruct settings; this class does not
    // serialize data or change existing preference schemas.
    internal static class SafeXmlReader
    {
        private static XmlReaderSettings Settings(int maxCharacters)
        {
            if (maxCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = maxCharacters
            };
        }

        internal static XmlReader Create(string path, int maxCharacters) =>
            XmlReader.Create(path, Settings(maxCharacters));

        internal static XmlReader Create(Stream input, int maxCharacters) =>
            XmlReader.Create(input, Settings(maxCharacters));

        internal static XmlReader Create(TextReader input, int maxCharacters) =>
            XmlReader.Create(input, Settings(maxCharacters));
    }
}
