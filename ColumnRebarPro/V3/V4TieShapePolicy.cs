using System;

namespace ColumnRebarPro.V3
{
    // Revit 2024: when the project's "Include hooks in Rebar Shape definition"
    // is disabled, the hook angles on a Rebar instance need not match the shape
    // defaults. Hook compatibility is still validated when creating Rebar.
    internal static class V4TieShapePolicy
    {
        internal static bool Matches(int segments, int shapeStartAngle, int shapeEndAngle,
            bool shapeDefinesHooks, bool outerTie, bool hookedOuterTie, bool alternatingCrossTie)
        {
            if (outerTie && segments < 4) return false;
            if (!outerTie && segments < 1) return false;
            if (!shapeDefinesHooks) return true;

            int expectedStart = outerTie ? (hookedOuterTie ? 135 : 0) : 135;
            int expectedEnd = outerTie ? (hookedOuterTie ? 135 : 0) :
                (alternatingCrossTie ? 90 : 135);
            return shapeStartAngle == expectedStart && shapeEndAngle == expectedEnd;
        }
    }
}
