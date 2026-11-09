namespace ColumnRebarPro.V3
{
    public enum V3TieStyle { Unspecified, Hook135, ClosedLoop }
    public enum V3SpliceStyle { None, AtStoreyJoint }
    public enum V3TieLayout { Unspecified, MaximumSpacing, FixedNumber }

    public sealed class V3RebarConfig
    {
        public int Cx, Cy;
        public double MainDiameter, TieDiameter, Cover;
        // Exact physical bar types in the active RVT, not nominal diameter alone.
        public string MainBarTypeUniqueId = "", TieBarTypeUniqueId = "";
        public double EndZone, EndSpacing, MidSpacing;
        // Revit Shape-Driven Rebar Set (not Autodesk.Revit.DB.Group).
        public bool GroupLongBars = true, GroupTies = true;
        public V3TieLayout TieLayout = V3TieLayout.Unspecified;
        // For Fixed Number: each end zone / the middle zone, including zone endpoints.
        public int EndTieCount, MidTieCount;
        // Optional: off by default because hooked cross-ties need compatible Rebar Shapes.
        public bool CrossTies = false;
        public bool CrossTiesAlternating;
        public bool JointTies = false;
        public double JointBeamHeight, JointSpacing;
        public V3TieStyle TieStyle = V3TieStyle.Unspecified;
        public V3SpliceStyle Splice = V3SpliceStyle.None;
        public string TieHook135UniqueId = "", TieHook90UniqueId = "";
        public string LongitudinalShapeUniqueId = "", AnchorShapeUniqueId = "";
        public string OuterTieShapeUniqueId = "", CrossTieShapeUniqueId = "";
        public double LapLength;
        [System.Xml.Serialization.XmlIgnore]
        public string LegacyConnectionNotice;
        public bool StarterBars = false;
        public double StarterLength;
        public bool StaggerSplices;
        public bool BottleneckRibbed = true;
        public double BottleneckClearanceMm = 2;
        // Geometric detailing default, not a slope mandated by TCVN 5574.
        public double BottleneckSlope = 6;
        // Legacy XML fields retained for compatibility; creation no longer requires approval/reference input.
        public string DesignReference = "";
        public bool DesignValuesConfirmed;
        public bool FoundationAnchorEnabled;
        public double FoundationAnchorLength, FoundationAnchorCover;
        public V3AnchorDirection FoundationAnchorDirection = V3AnchorDirection.Inward;
        public int BarCount { get { return 2 * (Cx + Cy) - 4; } }
        public V3RebarConfig Copy() { return (V3RebarConfig)MemberwiseClone(); }
    }

}
