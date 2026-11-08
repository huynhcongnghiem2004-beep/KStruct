namespace SDK_HD.KStruct.FootingRebar
{
    /// <summary>
    /// Thông số mặc định theo TCVN 5574:2018 và TCVN 198:1997 cho móng đơn &amp; móng cọc.
    /// </summary>
    public static class FootingDefaults
    {
        public static CoverSettings Cover(bool hasLeanConcrete = false) =>
            new CoverSettings(bottom: hasLeanConcrete ? 50 : 70, top: 50, side: 50, hasLeanConcrete: hasLeanConcrete);

        public static BottomLayerSettings Bottom(bool isPileCap = false) => new BottomLayerSettings(
            new BarLayerSettings(true, isPileCap ? 14 : 12, isPileCap ? 150 : 200, SpacingMode.EvenWithinMaxSpacing, isPileCap),
            new BarLayerSettings(true, isPileCap ? 14 : 12, isPileCap ? 150 : 200, SpacingMode.EvenWithinMaxSpacing, isPileCap));

        public static TopLayerSettings Top(bool isPileCap = false) => new TopLayerSettings(
            isPileCap,
            new BarLayerSettings(isPileCap, isPileCap ? 12 : 10, 200, SpacingMode.EvenWithinMaxSpacing, isPileCap),
            new BarLayerSettings(isPileCap, isPileCap ? 12 : 10, 200, SpacingMode.EvenWithinMaxSpacing, isPileCap));

        public static SideTiesSettings SideTies(double height) =>
            new SideTiesSettings(height > 700, 10, 200, true);

        public static FootingRebarFullSettings CreateDefaultFull(FootingModel model)
        {
            bool isPileCap = model.FootingType == FootingType.PileCap;
            return new FootingRebarFullSettings(
                model.FootingType,
                Cover(false),
                Bottom(isPileCap),
                Top(isPileCap),
                SideTies(model.Height));
        }
    }
}
