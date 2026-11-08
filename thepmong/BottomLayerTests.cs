using System.Linq;
using SDK_HD.KStruct.FootingRebar;
using Xunit;

namespace KStruct.Tests
{
    public class SpacingCalculatorTests
    {
        [Fact]
        public void Even_exact_division()
        {
            SpacingCalculator.Compute(1000, 200, SpacingMode.EvenWithinMaxSpacing, out int n, out double a);
            Assert.Equal(6, n);
            Assert.Equal(200, a, 6);
        }

        [Fact]
        public void Even_never_exceeds_max_spacing()
        {
            SpacingCalculator.Compute(1000, 300, SpacingMode.EvenWithinMaxSpacing, out int n, out double a);
            Assert.Equal(5, n);
            Assert.Equal(250, a, 6);
        }

        [Fact]
        public void Fixed_keeps_spacing_and_drops_remainder()
        {
            SpacingCalculator.Compute(1000, 300, SpacingMode.FixedFromEdge, out int n, out double a);
            Assert.Equal(4, n);
            Assert.Equal(300, a, 6);
        }

        [Fact]
        public void Zero_span_gives_single_bar()
        {
            SpacingCalculator.Compute(0, 200, SpacingMode.EvenWithinMaxSpacing, out int n, out _);
            Assert.Equal(1, n);
        }
    }

    public class BottomLayerBuilderTests
    {
        // Móng 1800 x 1200 x 450, cover đáy 50, cover cạnh 50, D12 a200 hai phương
        private static RebarPlan Build()
        {
            var f = new FootingModel(1800, 1200, 450);
            var c = new CoverSettings(50, 50, 50);
            var s = new BottomLayerSettings(
                new BarLayerSettings(true, 12, 200),
                new BarLayerSettings(true, 12, 200));
            return new BottomLayerBuilder().Build(f, c, s);
        }

        [Fact]
        public void Counts_and_spacing()
        {
            var plan = Build();
            var x = plan.Sets.Single(p => p.Direction == BarDirection.AlongX);
            var y = plan.Sets.Single(p => p.Direction == BarDirection.AlongY);
            Assert.Equal(7, x.Count);                 // span 1088 / 200 -> 6 khoảng
            Assert.Equal(181.333, x.Step.Y, 3);
            Assert.Equal(10, y.Count);                // span 1688 / 200 -> 9 khoảng
            Assert.Equal(187.556, y.Step.X, 3);
        }

        [Fact]
        public void Layers_are_stacked_without_overlap()
        {
            var plan = Build();
            var x = plan.Sets.Single(p => p.Direction == BarDirection.AlongX);
            var y = plan.Sets.Single(p => p.Direction == BarDirection.AlongY);
            Assert.Equal(56, x.FirstStart.Z, 6);      // 50 + 12/2
            Assert.Equal(68, y.FirstStart.Z, 6);      // 50 + 12 + 12/2
        }

        [Fact]
        public void All_bars_stay_inside_footing()
        {
            var plan = Build();
            foreach (var seg in plan.Sets.SelectMany(p => p.Bars()))
                foreach (var pt in new[] { seg.Start, seg.End })
                {
                    Assert.InRange(pt.X, 50, 1750);
                    Assert.InRange(pt.Y, 50, 1150);
                    Assert.InRange(pt.Z, 50, 400);
                }
        }

        [Fact]
        public void Layers_taller_than_footing_throw()
        {
            var f = new FootingModel(1800, 1200, 60);
            var c = new CoverSettings(50, 50, 50);
            var s = new BottomLayerSettings(new BarLayerSettings(true, 12, 200), new BarLayerSettings(true, 12, 200));
            Assert.Throws<System.ArgumentException>(() => new BottomLayerBuilder().Build(f, c, s));
        }
    }

    public class RulesTests
    {
        [Fact]
        public void Small_cover_is_flagged()
        {
            var f = new FootingModel(1800, 1200, 450);
            var c = new CoverSettings(35, 50, 50);
            var s = new BottomLayerSettings(new BarLayerSettings(true, 12, 200), new BarLayerSettings(true, 12, 200));
            var issues = new Tcvn5574FootingRules().Validate(f, c, s);
            Assert.Contains(issues, i => i.Code == "COVER_BOTTOM_MIN");
            Assert.DoesNotContain(issues, i => i.Code == "COVER_BOTTOM_ABS");
        }
    }
}
