using System;
using System.Collections.Generic;
using System.Linq;

namespace SDK_HD.KStruct.Core.V5
{
    /// <summary>Read-only ownership inventory; does not claim objects without persisted metadata.</summary>
    internal static class AuditOwnershipPolicy
    {
        internal static string Classify(bool persistedMarker, string hostUniqueId, string elementUniqueId)
        {
            if (!persistedMarker) return "unverified";
            if (string.IsNullOrWhiteSpace(hostUniqueId) ||
                string.IsNullOrWhiteSpace(elementUniqueId)) return "invalid-marker";
            if (string.Equals(hostUniqueId,elementUniqueId,StringComparison.Ordinal))
                return "invalid-self-host";
            return "marked-owned";
        }
        internal static bool MatchesHost(string ownedHostUniqueId, string expectedHostUniqueId) =>
            !string.IsNullOrWhiteSpace(ownedHostUniqueId) &&
            string.Equals(ownedHostUniqueId,expectedHostUniqueId,StringComparison.Ordinal);
        internal static string NormalizeRebarRole(string role)
        {
            if(string.IsNullOrWhiteSpace(role))return "unknown";
            var s=role.Trim().ToLowerInvariant();
            if(s.StartsWith("top-"))return "top";
            if(s.StartsWith("bottom-"))return "bottom";
            if(s.Contains("tie")||s.Contains("stirrup")||s.Contains("đai"))return "tie";
            return "other";
        }
        internal static string ErrorKey(string category,long id,string code) =>
            (category??"element")+"#"+id+":"+(code??"unknown");
    }
}
