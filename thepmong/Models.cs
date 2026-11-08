using System;

namespace SDK_HD.KStruct.FootingRebar
{
    /// <summary>
    /// Phân loại móng theo giải pháp kết cấu.
    /// </summary>
    public enum FootingType
    {
        /// <summary>Móng đơn / móng nông (Spread Footing).</summary>
        SpreadFooting,
        /// <summary>Móng cọc / đài cọc (Pile Cap).</summary>
        PileCap
    }

    /// <summary>
    /// Mô hình hình học móng chữ nhật. Toàn bộ đơn vị: mm.
    /// Gốc tọa độ cục bộ: góc dưới (min) của hộp bao móng.
    /// </summary>
    public sealed class FootingModel
    {
        public double LengthX { get; }
        public double WidthY { get; }
        public double Height { get; }
        public FootingType FootingType { get; }
        public string Name { get; }

        public FootingModel(double lengthX, double widthY, double height,
                            FootingType footingType = FootingType.SpreadFooting,
                            string name = "")
        {
            if (!IsPositiveFinite(lengthX) || !IsPositiveFinite(widthY) || !IsPositiveFinite(height))
                throw new ArgumentException("Kích thước móng phải lớn hơn 0.");
            LengthX = lengthX;
            WidthY = widthY;
            Height = height;
            FootingType = footingType;
            Name = name ?? string.Empty;
        }

        private static bool IsPositiveFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }

    /// <summary>
    /// Thiết lập chiều dày lớp bê tông bảo vệ theo TCVN 5574:2018 (mm).
    /// </summary>
    public sealed class CoverSettings
    {
        public double Bottom { get; }
        public double Top { get; }
        public double Side { get; }
        /// <summary>Có lớp bê tông lót (TCVN 5574:2018 quy định c_min=35mm khi có lót, 70mm khi không có lót).</summary>
        public bool HasLeanConcrete { get; }

        public CoverSettings(double bottom, double top, double side, bool hasLeanConcrete = true)
        {
            if (!IsNonNegativeFinite(bottom) || !IsNonNegativeFinite(top) || !IsNonNegativeFinite(side))
                throw new ArgumentException("Lớp bảo vệ không được âm.");
            Bottom = bottom;
            Top = top;
            Side = side;
            HasLeanConcrete = hasLeanConcrete;
        }

        private static bool IsNonNegativeFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }

    public enum SpacingMode
    {
        /// <summary>Khoảng cách nhập là TỐI ĐA: chia đều, khoảng cách thực tế &lt;= giá trị nhập (mặc định).</summary>
        EvenWithinMaxSpacing,
        /// <summary>Đặt đúng khoảng cách nhập từ mép, phần dư dồn về cuối.</summary>
        FixedFromEdge
    }

    /// <summary>
    /// Kiểu bẻ móc neo của thanh thép ở hai đầu theo TCVN 5574:2018.
    /// </summary>
    public enum HookTypeOption
    {
        /// <summary>Thanh thẳng không móc.</summary>
        None,
        /// <summary>Móc vuông 90° bẻ lên (chữ L / U hướng lên).</summary>
        HookUp,
        /// <summary>Móc vuông 90° bẻ xuống (chữ L / U hướng chúc xuống).</summary>
        HookDown
    }

    /// <summary>
    /// Thiết lập cho một hướng/lớp cốt thép.
    /// </summary>
    public sealed class BarLayerSettings
    {
        public bool Enabled { get; }
        public double Diameter { get; }
        public double Spacing { get; }
        public SpacingMode Mode { get; }
        public bool HasHook { get; }
        public HookTypeOption HookType { get; }
        public double HookLength { get; }

        public BarLayerSettings(bool enabled, double diameter, double spacing,
                                SpacingMode mode = SpacingMode.EvenWithinMaxSpacing,
                                bool hasHook = false, double hookLength = 0,
                                HookTypeOption hookType = HookTypeOption.None)
        {
            if (enabled && (!IsPositiveFinite(diameter) || !IsPositiveFinite(spacing)))
                throw new ArgumentException("Đường kính và khoảng cách phải lớn hơn 0.");
            if (!Enum.IsDefined(typeof(SpacingMode), mode))
                throw new ArgumentException("Chế độ chia khoảng cách không hợp lệ.");
            Enabled = enabled;
            Diameter = diameter;
            Spacing = spacing;
            Mode = mode;
            HookLength = hookLength >= 0 ? hookLength : 0;

            if (hookType != HookTypeOption.None)
            {
                HookType = hookType;
                HasHook = true;
            }
            else if (hasHook)
            {
                HookType = HookTypeOption.HookUp;
                HasHook = true;
            }
            else
            {
                HookType = HookTypeOption.None;
                HasHook = false;
            }
        }

        private static bool IsPositiveFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }

    /// <summary>Lớp thép đáy: hai phương, phương X nằm dưới, phương Y nằm trên.</summary>
    public sealed class BottomLayerSettings
    {
        public BarLayerSettings AlongX { get; }
        public BarLayerSettings AlongY { get; }

        public BottomLayerSettings(BarLayerSettings alongX, BarLayerSettings alongY)
        {
            AlongX = alongX ?? throw new ArgumentNullException(nameof(alongX));
            AlongY = alongY ?? throw new ArgumentNullException(nameof(alongY));
        }
    }

    /// <summary>Lớp thép đỉnh (thường dùng cho đài cọc, hoặc móng chịu tải nhổ/uốn ngược).</summary>
    public sealed class TopLayerSettings
    {
        public bool Enabled { get; }
        public BarLayerSettings AlongX { get; }
        public BarLayerSettings AlongY { get; }

        public TopLayerSettings(bool enabled, BarLayerSettings alongX, BarLayerSettings alongY)
        {
            Enabled = enabled;
            AlongX = alongX ?? throw new ArgumentNullException(nameof(alongX));
            AlongY = alongY ?? throw new ArgumentNullException(nameof(alongY));
        }
    }

    /// <summary>
    /// Cốt thép đai cấu tạo hông đài theo TCVN 198:1997 (khi H > 700 mm).
    /// </summary>
    public sealed class SideTiesSettings
    {
        public bool Enabled { get; }
        public double Diameter { get; }
        public double Spacing { get; }
        public bool HasCStirrups { get; }

        public SideTiesSettings(bool enabled, double diameter = 10, double spacing = 200, bool hasCStirrups = true)
        {
            Enabled = enabled;
            Diameter = diameter > 0 ? diameter : 10;
            Spacing = spacing > 0 ? spacing : 200;
            HasCStirrups = hasCStirrups;
        }
    }

    /// <summary>
    /// Tổng hợp cấu hình cốt thép móng/đài cọc theo TCVN 5574:2018 &amp; TCVN 198:1997.
    /// </summary>
    public sealed class FootingRebarFullSettings
    {
        public FootingType FootingType { get; }
        public CoverSettings Cover { get; }
        public BottomLayerSettings Bottom { get; }
        public TopLayerSettings Top { get; }
        public SideTiesSettings SideTies { get; }

        public FootingRebarFullSettings(
            FootingType footingType,
            CoverSettings cover,
            BottomLayerSettings bottom,
            TopLayerSettings top,
            SideTiesSettings sideTies)
        {
            FootingType = footingType;
            Cover = cover ?? throw new ArgumentNullException(nameof(cover));
            Bottom = bottom ?? throw new ArgumentNullException(nameof(bottom));
            Top = top ?? throw new ArgumentNullException(nameof(top));
            SideTies = sideTies ?? throw new ArgumentNullException(nameof(sideTies));
        }
    }
}
