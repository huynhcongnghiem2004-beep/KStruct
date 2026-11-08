using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.FootingRebar
{
    /// <summary>Revit lưu feet nội bộ; lõi phần lõi dùng mm.</summary>
    public static class UnitConv
    {
        public static double ToFeet(double mm) =>
            UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

        public static double ToMm(double feet) =>
            UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
    }
}
