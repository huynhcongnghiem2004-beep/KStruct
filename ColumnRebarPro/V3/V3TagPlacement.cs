namespace ColumnRebarPro.V3
{
    internal enum V3TagHorizontalSide { Left = -1, Right = 1 }

    // Compatibility adapter for existing column drawing callers.
    internal static class V3TagPlacement
    {
        internal static V3TagHorizontalSide Resolve(string familyName, string typeName, V3TagHorizontalSide fallback)
            => (V3TagHorizontalSide)KStruct.Core.RebarTagPlacement.ResolveSide(familyName, typeName, (int)fallback);

        internal static int ResolveVerticalPosition(string familyName, string typeName)
            => KStruct.Core.RebarTagPlacement.ResolveVertical(familyName, typeName);

        internal static bool IsUpperTag(string familyName, string typeName)
            => ResolveVerticalPosition(familyName, typeName) == 1;
    }
}
