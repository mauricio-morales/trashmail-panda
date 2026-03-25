using TrashMailPanda.Shared.Labels;
using Xunit;

namespace TrashMailPanda.Tests.Unit;

[Trait("Category", "Unit")]
public class LabelThresholdsTests
{
    // ── TryGetThreshold ───────────────────────────────────────────────────────

    [Fact]
    public void TryGetThreshold_Archive30d_Returns30()
    {
        var found = LabelThresholds.TryGetThreshold(LabelThresholds.Archive30d, out var days);

        Assert.True(found);
        Assert.Equal(30, days);
    }

    [Fact]
    public void TryGetThreshold_Archive1y_Returns365()
    {
        var found = LabelThresholds.TryGetThreshold(LabelThresholds.Archive1y, out var days);

        Assert.True(found);
        Assert.Equal(365, days);
    }

    [Fact]
    public void TryGetThreshold_Archive5y_Returns1825()
    {
        var found = LabelThresholds.TryGetThreshold(LabelThresholds.Archive5y, out var days);

        Assert.True(found);
        Assert.Equal(1825, days);
    }

    [Theory]
    [InlineData("Archive")]
    [InlineData("Delete")]
    [InlineData("Keep")]
    [InlineData("Spam")]
    [InlineData("")]
    [InlineData("unknown label")]
    public void TryGetThreshold_NonTimeBoundedLabel_ReturnsFalse(string label)
    {
        var found = LabelThresholds.TryGetThreshold(label, out var days);

        Assert.False(found);
        Assert.Equal(0, days);
    }

    [Theory]
    [InlineData("archive for 30d")]   // all-lower case
    [InlineData("ARCHIVE FOR 30D")]   // all-upper case
    [InlineData("Archive For 30D")]   // mixed case
    public void TryGetThreshold_IsCaseInsensitive(string label)
    {
        var found = LabelThresholds.TryGetThreshold(label, out var days);

        Assert.True(found);
        Assert.Equal(30, days);
    }

    // ── IsTimeBounded ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(LabelThresholds.Archive30d)]
    [InlineData(LabelThresholds.Archive1y)]
    [InlineData(LabelThresholds.Archive5y)]
    public void IsTimeBounded_TimeBoundedLabel_ReturnsTrue(string label)
    {
        Assert.True(LabelThresholds.IsTimeBounded(label));
    }

    [Theory]
    [InlineData("Archive")]
    [InlineData("Delete")]
    [InlineData("Keep")]
    [InlineData("Spam")]
    [InlineData("unknown")]
    public void IsTimeBounded_NonTimeBoundedLabel_ReturnsFalse(string label)
    {
        Assert.False(LabelThresholds.IsTimeBounded(label));
    }

    // ── Constants ─────────────────────────────────────────────────────────────

    [Fact]
    public void TimeBoundedLabels_ContainsAllThreeLabels()
    {
        Assert.Contains(LabelThresholds.Archive30d, LabelThresholds.TimeBoundedLabels);
        Assert.Contains(LabelThresholds.Archive1y, LabelThresholds.TimeBoundedLabels);
        Assert.Contains(LabelThresholds.Archive5y, LabelThresholds.TimeBoundedLabels);
        Assert.Equal(3, LabelThresholds.TimeBoundedLabels.Count);
    }
}
