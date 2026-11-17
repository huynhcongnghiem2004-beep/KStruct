using System;
using System.Collections.Generic;

namespace ColumnRebarPro.V3
{
    // Millimetres only, shared by Revit generation and WPF preview.
    internal sealed class V3TieBand
    {
        internal double Start, End, MaximumSpacing;
        internal int Count;
        internal bool FixedNumber;
        internal double Position(int index) => Count == 1 ? Start : Start + (End - Start) * index / (Count - 1);
    }

    internal static class V3TieLayoutPlan
    {
        internal static List<V3TieBand> Create(double height, double cover, double diameter,
            double endZone, double endSpacing, double midSpacing, bool fixedNumber,
            int endCount, int midCount, bool joint, double jointHeight, double jointSpacing)
        {
            double[] values = { height, cover, diameter, endZone, endSpacing, midSpacing,
                jointHeight, jointSpacing };
            foreach (double value in values)
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new InvalidOperationException("Thông số vùng đai phải là số hữu hạn.");
            double low = cover + diameter / 2, high = height - low;
            if (cover <= 0 || diameter <= 0 || high <= low || endZone < 0 ||
                endZone > (high - low) / 2 || endSpacing <= 0 || midSpacing <= 0)
                throw new InvalidOperationException("Vùng đai đầu không được chồng nhau; kiểm tra chiều cao, cover và bước đai.");
            if (fixedNumber && (endCount < 2 || midCount < 2 || endCount > 1002 || midCount > 1002))
                throw new InvalidOperationException("Số vị trí đai mỗi vùng phải từ 2 đến 1002.");
            if (joint && (jointHeight <= 0 || jointSpacing <= 0))
                throw new InvalidOperationException("Nhập chiều cao và bước đai vùng nút dầm sàn.");

            var bands = new List<V3TieBand>();
            Add(bands, low, low + endZone, endSpacing, fixedNumber, endCount);
            Add(bands, low + endZone, high - endZone, midSpacing, fixedNumber, midCount);
            Add(bands, high - endZone, high, endSpacing, fixedNumber, endCount);
            // Region above the column: user-specified, supported by starter bars.
            if (joint) Add(bands, high, high + jointHeight, jointSpacing, false, 0);
            return bands;
        }

        private static void Add(List<V3TieBand> bands, double start, double end,
            double spacing, bool fixedNumber, int fixedCount)
        {
            double length = end - start;
            if (length <= 1e-7) return;
            double requested = fixedNumber ? fixedCount : Math.Ceiling(length / spacing - 1e-9) + 1;
            if (requested < 2 || requested > 1002)
                throw new InvalidOperationException("Vùng đai cần từ 2 đến 1002 vị trí; kiểm tra bước đai.");
            int count = (int)requested;
            double step = length / (count - 1);
            // A boundary tie belongs to the preceding band; never create it twice.
            if (bands.Count > 0 && Math.Abs(bands[bands.Count - 1].End - start) < 1e-7)
            {
                start += step;
                count--;
            }
            bands.Add(new V3TieBand { Start = start, End = end, Count = count,
                MaximumSpacing = spacing, FixedNumber = fixedNumber });
        }
    }
}
