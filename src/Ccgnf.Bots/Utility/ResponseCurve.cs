namespace Ccgnf.Bots.Utility;

/// <summary>
/// One keyframe in a <see cref="ResponseCurve"/>. <see cref="Time"/> is
/// the normalised x in [0, 1]; <see cref="Value"/> is the normalised y
/// in [0, 1]. Tangent slopes (optional) control the Hermite segment
/// shape — when both out-tangent of the left knot and in-tangent of the
/// right knot are zero, the segment degenerates to linear interpolation
/// (the common case).
/// </summary>
public readonly record struct Keyframe(
    float Time,
    float Value,
    float InTangent = 0f,
    float OutTangent = 0f);

/// <summary>
/// Piecewise Hermite curve used to shape a consideration's input
/// (a normalised float in [0, 1]) into an output (a normalised float in
/// [0, 1]). Mirrors Unity's AnimationCurve / Godot's Curve editor.
/// <para>
/// Ported from <c>reference-code/utilityAI/CurveEvaluator.cs</c>; lives
/// in a separate type so curve data can be authored as JSON and shared
/// across tools (editor, benchmark harness, runtime).
/// </para>
/// </summary>
public sealed class ResponseCurve
{
    public IReadOnlyList<Keyframe> Keys { get; }

    public ResponseCurve(IReadOnlyList<Keyframe> keys)
    {
        Keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    /// <summary>Curve of one constant value. Useful as a "flat" default.</summary>
    public static ResponseCurve Constant(float value) =>
        new(new[] { new Keyframe(0f, value), new Keyframe(1f, value) });

    /// <summary>Linear ramp from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public static ResponseCurve Linear(float start, float end) =>
        new(new[] { new Keyframe(0f, start), new Keyframe(1f, end) });

    /// <summary>
    /// The named presets the web editor exposes (§"Authoring model —
    /// not raw knobs" in 10.2-long-term-ai-plan.md). Authors pick a
    /// preset by name; advanced mode exposes the raw keyframes.
    /// </summary>
    public static ResponseCurve FromPreset(CurvePreset preset) => preset switch
    {
        CurvePreset.Linear => Linear(0f, 1f),
        CurvePreset.EaseIn => new(new[]
        {
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(1f, 1f, 2f, 0f),
        }),
        CurvePreset.EaseOut => new(new[]
        {
            new Keyframe(0f, 0f, 0f, 2f),
            new Keyframe(1f, 1f, 0f, 0f),
        }),
        CurvePreset.Threshold => new(new[]
        {
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 0f),
            new Keyframe(0.5001f, 1f),
            new Keyframe(1f, 1f),
        }),
        CurvePreset.Bell => new(new[]
        {
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0f),
        }),
        _ => Linear(0f, 1f),
    };
}

public enum CurvePreset
{
    Linear,
    EaseIn,
    EaseOut,
    Threshold,
    Bell,
}
