using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VolumeCurveTests
{
    [Fact]
    public void HitTestSegment_FindsCurveBetweenPoints()
    {
        var curve = new VolumeCurve(
            [
                new VolumeCurvePoint(0, 0),
                new VolumeCurvePoint(0.5, -6),
                new VolumeCurvePoint(1, 0),
            ],
            [FadeShape.Linear, FadeShape.Linear]);
        Assert.Equal(0, curve.HitTestSegment(0.25, -3f, dbRadius: 1.5f));
        Assert.Equal(1, curve.HitTestSegment(0.75, -3f, dbRadius: 1.5f));
        Assert.Equal(-1, curve.HitTestSegment(0.25, 20f, dbRadius: 1.5f));
    }

    [Fact]
    public void SetSegmentGainDb_LiftsBothEnds()
    {
        var curve = VolumeCurve.Flat(0);
        Assert.True(curve.SetSegmentGainDb(0, -3f, -3f));
        Assert.Equal(-3f, curve.Points[0].GainDb);
        Assert.Equal(-3f, curve.Points[1].GainDb);
    }

    [Fact]
    public void RestoreShiftedFrom_AppliesDeltaWithoutAccumulating()
    {
        var start = new VolumeCurve(
            [
                new VolumeCurvePoint(0, 0),
                new VolumeCurvePoint(0.5, -6),
                new VolumeCurvePoint(1, 3),
            ],
            [FadeShape.Linear, FadeShape.Linear]);
        var live = start.Clone();
        live.RestoreShiftedFrom(start, 2f);
        Assert.Equal(2f, live.Points[0].GainDb);
        Assert.Equal(-4f, live.Points[1].GainDb);
        Assert.Equal(5f, live.Points[2].GainDb);
        live.RestoreShiftedFrom(start, -1f);
        Assert.Equal(-1f, live.Points[0].GainDb);
        Assert.Equal(-7f, live.Points[1].GainDb);
        Assert.Equal(2f, live.Points[2].GainDb);
    }

    [Fact]
    public void RemovePointsBetween_ClearsInteriorForBrushRedraw()
    {
        var curve = new VolumeCurve(
            [
                new VolumeCurvePoint(0, 0),
                new VolumeCurvePoint(0.2, 0),
                new VolumeCurvePoint(0.4, 0),
                new VolumeCurvePoint(0.6, -12),
                new VolumeCurvePoint(1, 0),
            ],
            [FadeShape.Linear, FadeShape.Linear, FadeShape.Linear, FadeShape.Linear]);
        Assert.Equal(2, curve.RemovePointsBetween(0.15, 0.55));
        Assert.Equal(3, curve.Points.Count);
        Assert.Equal(0d, curve.Points[0].T);
        Assert.Equal(0.6, curve.Points[1].T, 5);
        Assert.Equal(1d, curve.Points[2].T);
        Assert.Equal(0, curve.RemovePointsBetween(0.6, 0.6));
    }
}
