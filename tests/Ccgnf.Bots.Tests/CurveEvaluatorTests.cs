namespace Ccgnf.Bots.Tests;

public class CurveEvaluatorTests
{
    [Fact]
    public void EmptyCurveReturnsZero()
    {
        var curve = new ResponseCurve(Array.Empty<Keyframe>());
        Assert.Equal(0f, CurveEvaluator.Evaluate(curve, 0.5f));
    }

    [Fact]
    public void SingleKeyframeReturnsClampedValue()
    {
        var curve = new ResponseCurve(new[] { new Keyframe(0f, 0.7f) });
        Assert.Equal(0.7f, CurveEvaluator.Evaluate(curve, 0.3f));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1f, 1f)]
    public void LinearCurveInterpolates(float input, float expected)
    {
        var curve = ResponseCurve.Linear(0f, 1f);
        Assert.Equal(expected, CurveEvaluator.Evaluate(curve, input), precision: 5);
    }

    [Fact]
    public void InputClampedToUnitRange()
    {
        var curve = ResponseCurve.Linear(0f, 1f);
        Assert.Equal(0f, CurveEvaluator.Evaluate(curve, -0.5f));
        Assert.Equal(1f, CurveEvaluator.Evaluate(curve, 2f));
    }

    [Fact]
    public void OutputClampedToUnitRange()
    {
        // Extreme tangents can drive Hermite output out of [0, 1]; evaluator clamps.
        var curve = new ResponseCurve(new[]
        {
            new Keyframe(0f, 0.5f, 0f, 10f),
            new Keyframe(1f, 0.5f, -10f, 0f),
        });
        for (float t = 0; t <= 1; t += 0.1f)
        {
            var y = CurveEvaluator.Evaluate(curve, t);
            Assert.InRange(y, 0f, 1f);
        }
    }

    [Fact]
    public void MultiSegmentCurvePicksCorrectSegment()
    {
        // (0,0) → (0.5, 0.2) → (1.0, 1.0). Piecewise linear.
        var curve = new ResponseCurve(new[]
        {
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 0.2f),
            new Keyframe(1f, 1f),
        });
        Assert.Equal(0.1f, CurveEvaluator.Evaluate(curve, 0.25f), precision: 5);   // first segment mid
        Assert.Equal(0.6f, CurveEvaluator.Evaluate(curve, 0.75f), precision: 5);   // second segment mid
    }

    [Fact]
    public void ConstantCurveIsFlat()
    {
        var curve = ResponseCurve.Constant(0.42f);
        for (float t = 0; t <= 1; t += 0.2f)
            Assert.Equal(0.42f, CurveEvaluator.Evaluate(curve, t), precision: 5);
    }

    [Fact]
    public void BellPresetPeaksAtHalf()
    {
        var curve = ResponseCurve.FromPreset(CurvePreset.Bell);
        var edge = CurveEvaluator.Evaluate(curve, 0f);
        var peak = CurveEvaluator.Evaluate(curve, 0.5f);
        var far = CurveEvaluator.Evaluate(curve, 1f);
        Assert.True(peak > edge);
        Assert.True(peak > far);
        Assert.Equal(0f, edge, precision: 4);
        Assert.Equal(1f, peak, precision: 4);
        Assert.Equal(0f, far, precision: 4);
    }

    [Fact]
    public void ThresholdPresetFlipsAtHalf()
    {
        var curve = ResponseCurve.FromPreset(CurvePreset.Threshold);
        Assert.True(CurveEvaluator.Evaluate(curve, 0.4f) < 0.5f);
        Assert.True(CurveEvaluator.Evaluate(curve, 0.6f) > 0.5f);
    }
}
