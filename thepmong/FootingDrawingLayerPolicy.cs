using System;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // Rules for generated plan views and per-layer reinforcement notes.
    // IsPileCap is resolved from the host in Auto mode, or explicitly overridden by the user.
    internal static class FootingDrawingLayerPolicy
    {
        internal static bool IsPileCap(string mode, bool detected)
        {
            if (string.Equals(mode, "PileCap", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(mode, "Isolated", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(mode) && !string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Loại móng không hợp lệ: " + mode);
            return detected;
        }

        internal static bool CreatePlan(bool pileCap, bool top) => !top || pileCap;

        internal static bool CreateNote(bool masterEnabled, bool topEnabled, bool botEnabled, bool top, bool pileCap)
        {
            return masterEnabled && CreatePlan(pileCap, top) && (top ? topEnabled : botEnabled);
        }
    }
}
