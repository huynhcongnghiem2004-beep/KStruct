using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    // Paper-space viewport validation, testable without Revit assemblies.
    internal static class V4SheetLayoutGuard
    {
        internal struct Rect
        {
            internal double Left, Bottom, Right, Top;
            internal Rect(double left, double bottom, double right, double top)
            {
                Left = left; Bottom = bottom; Right = right; Top = top;
            }
        }

        internal static void Validate(Rect allowed, IEnumerable<Rect> viewports)
        {
            if (viewports == null) throw new ArgumentNullException(nameof(viewports));
            const double tol = 1e-6;
            var boxes = viewports.ToList();
            if (boxes.Count == 0) throw new InvalidOperationException("Sheet không có viewport.");
            if (!Valid(allowed)) throw new InvalidOperationException("Vùng in Sheet không hợp lệ.");
            foreach (var box in boxes)
            {
                if (!Valid(box))
                    throw new InvalidOperationException("Phạm vi viewport không hợp lệ.");
                if (box.Left < allowed.Left - tol || box.Right > allowed.Right + tol ||
                    box.Bottom < allowed.Bottom - tol || box.Top > allowed.Top + tol)
                    throw new InvalidOperationException(
                        "Viewport vượt vùng in hoặc chồng vào vùng chừa khung tên. " +
                        "Tăng khổ giấy, giảm tỷ lệ theo thiết kế hoặc tách Sheet.");
            }
            for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i]; var b = boxes[j];
                if (a.Left < b.Right - tol && b.Left < a.Right - tol &&
                    a.Bottom < b.Top - tol && b.Bottom < a.Top - tol)
                    throw new InvalidOperationException(
                        "Viewport bị chồng nhau trên Sheet. Không thay đổi hồ sơ cũ.");
            }
        }

        // Non-blocking diagnostic for users who explicitly accept stacked or overflowing
        // viewports. Invalid geometry is still a fatal error, not a layout warning.
        internal static List<string> Inspect(Rect allowed, IEnumerable<Rect> viewports)
        {
            if (viewports == null) throw new ArgumentNullException(nameof(viewports));
            var boxes = viewports.ToList();
            if (boxes.Count == 0) throw new InvalidOperationException("Sheet không có viewport.");
            if (!Valid(allowed)) throw new InvalidOperationException("Vùng in Sheet không hợp lệ.");
            if (boxes.Any(box => !Valid(box)))
                throw new InvalidOperationException("Phạm vi viewport không hợp lệ.");

            const double tol = 1e-6;
            var warnings = new List<string>();
            int outside = boxes.Count(box =>
                box.Left < allowed.Left - tol || box.Right > allowed.Right + tol ||
                box.Bottom < allowed.Bottom - tol || box.Top > allowed.Top + tol);
            int overlaps = 0;
            for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i]; var b = boxes[j];
                if (a.Left < b.Right - tol && b.Left < a.Right - tol &&
                    a.Bottom < b.Top - tol && b.Bottom < a.Top - tol)
                    overlaps++;
            }
            if (outside > 0)
                warnings.Add(outside + " Viewport nằm ngoài vùng in hoặc chạm vùng khung tên.");
            if (overlaps > 0)
                warnings.Add(overlaps + " cặp Viewport đang chồng lên nhau.");
            return warnings;
        }

        private static bool Valid(Rect r) =>
            Finite(r.Left) && Finite(r.Bottom) && Finite(r.Right) && Finite(r.Top) &&
            r.Right > r.Left && r.Top > r.Bottom;

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
