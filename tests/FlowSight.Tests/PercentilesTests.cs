using FlowSight.Core.Statistics;

namespace FlowSight.Tests;

public class PercentilesTests
{
    private static readonly double[] OneToTen = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    [Fact]
    public void Percentile_IsInclusiveLinear()
    {
        Assert.Equal(5.5, Percentiles.Median(OneToTen));
        Assert.Equal(9.1, Percentiles.Percentile(OneToTen, 0.9)!.Value, 6);
        Assert.Equal(7, Percentiles.Percentile([7], 0.9));
        Assert.Null(Percentiles.Median([]));
    }

    [Fact]
    public void Percentile_DoesNotRequireSortedInput()
    {
        Assert.Equal(5.5, Percentiles.Median([10, 1, 9, 2, 8, 3, 7, 4, 6, 5]));
    }

    [Fact]
    public void KaplanMeierMedian_UsesCensoredSpans()
    {
        Assert.Equal(3, Percentiles.KaplanMeierMedian([(1, true), (2, true), (3, true), (4, false), (4, false)]));
        Assert.Equal(2, Percentiles.KaplanMeierMedian([(1, true), (2, true), (3, true)]));
    }

    [Fact]
    public void KaplanMeierMedian_NullWhenNeverBelowHalf()
    {
        Assert.Null(Percentiles.KaplanMeierMedian([(1, true), (5, false), (5, false), (5, false)]));
        Assert.Null(Percentiles.KaplanMeierMedian([]));
    }
}
