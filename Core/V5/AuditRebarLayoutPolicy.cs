using System;
namespace SDK_HD.KStruct.Core.V5
{
    /// <summary>Layout rule safeguards shared by read-only Audit V2 and its regression tests.</summary>
    internal static class AuditRebarLayoutPolicy
    {
        internal static bool HasArrayLength(string layoutRule) =>
            !string.IsNullOrWhiteSpace(layoutRule) &&
            !string.Equals(layoutRule,"Single",StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(layoutRule,"unavailable",StringComparison.OrdinalIgnoreCase);
        internal static bool IsConsistentSingle(string layoutRule,int numberOfPositions,int existingPositions) =>
            string.Equals(layoutRule,"Single",StringComparison.OrdinalIgnoreCase) &&
            numberOfPositions == 1 && existingPositions == 1;
        internal static bool IsValidArrayLength(double feet) =>
            AuditV2Policy.IsFinite(feet) && feet >= 0;
    }
}
