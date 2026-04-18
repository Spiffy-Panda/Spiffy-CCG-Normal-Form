using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ccgnf.Interpreter;

/// <summary>
/// Drives the event loop and owns the two deterministic inputs that the
/// interpreter reads from: a seeded RNG (for Shuffle / RandomChoose) and the
/// host input queue (for Choice and Target). Ownership here — rather than in
/// a scattered collection of globals — is what makes the "same seed + same
/// inputs => same state" invariant provable (GrammarSpec §8.4).
/// </summary>
public sealed class Scheduler
{
    private readonly ILogger<Scheduler> _log;

    public Random Rng { get; }
    public IHostInputQueue Inputs { get; }

    public int Seed { get; }

    public Scheduler(int seed, IHostInputQueue inputs, ILogger<Scheduler>? log = null)
    {
        Seed = seed;
        Rng = new Random(seed);
        Inputs = inputs;
        _log = log ?? NullLogger<Scheduler>.Instance;
    }

    public int NextInt(int maxExclusive) => Rng.Next(maxExclusive);

    /// <summary>Fisher–Yates in-place shuffle, seeded by <see cref="Rng"/>.</summary>
    public void ShuffleInPlace<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        _log.LogDebug("Shuffled list of {Count} elements", list.Count);
    }
}
