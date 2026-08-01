namespace EFCore.AutoSeed.Exceptions;

/// <summary>
/// Thrown when the model contains one or more dependency cycles made entirely of required
/// (non-nullable) foreign keys, so no insertion order can satisfy every constraint. Break each
/// cycle by making one of its foreign keys nullable, or exclude one of its entities from seeding.
/// </summary>
public sealed class UnresolvableCycleException : AutoSeedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnresolvableCycleException"/> class.
    /// </summary>
    /// <param name="cycles">
    /// Every unresolvable cycle found, each as the full names of the entity types that form it in
    /// actual foreign key order. Entity types that are merely blocked downstream of a cycle, but
    /// are not themselves part of one, are not included.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="cycles"/> is <see langword="null"/>.</exception>
    public UnresolvableCycleException(IReadOnlyList<IReadOnlyList<string>> cycles)
        : base(BuildMessage(cycles))
    {
        Cycles = cycles;
    }

    /// <summary>
    /// Every unresolvable cycle found, each as the full names of the entity types that form it in
    /// actual foreign key order.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Cycles { get; }

    private static string BuildMessage(IReadOnlyList<IReadOnlyList<string>> cycles)
    {
        ArgumentNullException.ThrowIfNull(cycles);
        string joinedCycles = string.Join("; ", cycles.Select(cycle => string.Join(" -> ", cycle)));
        string noun = cycles.Count == 1 ? "cycle" : "cycles";
        return $"Cannot determine an insertion order: {cycles.Count} unresolvable {noun} formed " +
               $"entirely of required foreign keys: {joinedCycles}. Make one of the foreign keys " +
               "in each cycle nullable, or exclude one of these entities from seeding.";
    }
}
