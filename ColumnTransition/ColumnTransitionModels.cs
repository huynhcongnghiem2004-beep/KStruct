using System;
using Autodesk.Revit.DB;

namespace ColumnTransition
{
    internal sealed class TransitionColumnSegment
    {
        public FamilyInstance Column;
        public ElementId Id;
        public string Level;
        public string Mark;
        public double BottomMm;
        public double TopMm;
        public double WidthMm;
        public double DepthMm;

        public override string ToString()
        {
            return Level + "  |  " + Math.Round(WidthMm) + " × " + Math.Round(DepthMm) + " mm";
        }
    }

    internal sealed class TransitionRebarConfig
    {
        public int BarsX = 4;
        public int BarsY = 4;
        public double MainDiameter = 20;
        public double TieDiameter = 8;
        public double Cover = 30;
        public double TieSpacing = 150;
        public double LapLength = 800;
        public double TransitionLength = 600;

        public TransitionRebarConfig Copy()
        {
            return (TransitionRebarConfig)MemberwiseClone();
        }
    }
}
