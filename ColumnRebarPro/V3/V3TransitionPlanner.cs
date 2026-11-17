using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    internal static class V3TransitionPlanner
    {
        internal static XYZ InwardDirection(XYZ point, V3Geometry geometry, double halfWidth, double halfDepth)
        {
            XYZ offset = new XYZ(point.X - geometry.Center.X, point.Y - geometry.Center.Y, 0);
            double x = offset.DotProduct(geometry.X);
            double y = offset.DotProduct(geometry.Y);
            double tolerance = V3Units.Ft(15);
            bool onXEdge = System.Math.Abs(System.Math.Abs(x) - halfWidth) < tolerance;
            bool onYEdge = System.Math.Abs(System.Math.Abs(y) - halfDepth) < tolerance;

            if (onXEdge && onYEdge)
            {
                XYZ diagonal = -System.Math.Sign(x) * geometry.X - System.Math.Sign(y) * geometry.Y;
                return diagonal.GetLength() > 1e-4 ? diagonal.Normalize() : -geometry.Y;
            }
            if (onYEdge) return -System.Math.Sign(y) * geometry.Y;
            if (onXEdge) return -System.Math.Sign(x) * geometry.X;

            XYZ inward = -x * geometry.X - y * geometry.Y;
            return inward.GetLength() > 1e-4 ? inward.Normalize() : -geometry.Y;
        }

    }
}
