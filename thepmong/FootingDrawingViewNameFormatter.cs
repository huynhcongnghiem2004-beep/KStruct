namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingViewNameFormatter
    {
        internal const string DefaultPattern = "{Role} - {Mark}";

        internal static string PatternForEditing(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern) ||
                pattern == "KStruct - MĐ {Id} - {Role}" ||
                pattern == "KStruct - ĐC {Id} - {Role}")
                return DefaultPattern;

            return pattern;
        }

        internal static string Format(string pattern, string role, string mark, string elementId)
        {
            string actualPattern = string.IsNullOrWhiteSpace(pattern) ? DefaultPattern : pattern;
            string markOrId = string.IsNullOrWhiteSpace(mark) ? elementId : mark.Trim();
            return actualPattern
                .Replace("{Mark}", markOrId)
                .Replace("{Id}", elementId)
                .Replace("{Role}", DisplayRole(role));
        }

        private static string DisplayRole(string role)
        {
            switch (role)
            {
                case "Plan-Top": return "MẶT BẰNG THÉP LỚP TRÊN";
                case "Plan-Bot": return "MẶT BẰNG THÉP LỚP DƯỚI";
                case "Detail-X": return "MẶT CẮT A-A";
                case "Detail-Y": return "MẶT CẮT B-B";
                default: return role ?? "";
            }
        }
    }
}
