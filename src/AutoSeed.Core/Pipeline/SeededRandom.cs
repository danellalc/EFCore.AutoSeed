namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// A random number source whose output depends only on a root seed and an explicit derivation
/// path, never on call order. Deriving the same path twice, from anywhere, always yields a
/// <see cref="SeededRandom"/> that produces the same sequence of values.
/// </summary>
public sealed class SeededRandom
{
    private readonly ulong _state;
    private readonly Random _random;

    private SeededRandom(ulong state)
    {
        _state = state;
        Seed = unchecked((int)state);
        _random = new Random(Seed);
    }

    /// <summary>
    /// The derived seed for this instance's path, as a plain <see cref="int"/>. Use this to seed
    /// an external deterministic generator (for example Bogus's <c>Randomizer</c>) so that its
    /// output is governed by the same hierarchical, positional derivation as everything else.
    /// </summary>
    public int Seed { get; }

    /// <summary>
    /// Creates the root <see cref="SeededRandom"/> for a seeding run.
    /// </summary>
    /// <param name="rootSeed">The seed supplied by the caller of the seeding operation.</param>
    /// <returns>A <see cref="SeededRandom"/> from which every other one is derived.</returns>
    public static SeededRandom FromRootSeed(long rootSeed) =>
        new(StableHash.Combine(StableHash.OffsetBasis, rootSeed));

    /// <summary>
    /// Derives a child <see cref="SeededRandom"/> scoped to a named path segment, such as an
    /// entity type name or a property name.
    /// </summary>
    /// <param name="segment">The path segment identifying the child scope.</param>
    /// <returns>A <see cref="SeededRandom"/> that depends only on this instance's path and <paramref name="segment"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="segment"/> is <see langword="null"/>.</exception>
    public SeededRandom Derive(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return new SeededRandom(StableHash.Combine(_state, segment));
    }

    /// <summary>
    /// Derives a child <see cref="SeededRandom"/> scoped to a positional index, such as a row index.
    /// </summary>
    /// <param name="index">The path segment identifying the child scope.</param>
    /// <returns>A <see cref="SeededRandom"/> that depends only on this instance's path and <paramref name="index"/>.</returns>
    public SeededRandom Derive(int index) => new(StableHash.Combine(_state, index));

    /// <summary>
    /// Returns a random integer in the range [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).
    /// </summary>
    /// <param name="minInclusive">The inclusive lower bound.</param>
    /// <param name="maxExclusive">The exclusive upper bound.</param>
    /// <returns>A pseudo-random integer within the requested range.</returns>
    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    /// <summary>
    /// Returns a random floating-point number in the range [0.0, 1.0).
    /// </summary>
    /// <returns>A pseudo-random number in the range [0.0, 1.0).</returns>
    public double NextDouble() => _random.NextDouble();

    /// <summary>
    /// Returns a random boolean value.
    /// </summary>
    /// <returns><see langword="true"/> or <see langword="false"/>, each with equal probability.</returns>
    public bool NextBoolean() => _random.Next(2) == 1;
}
