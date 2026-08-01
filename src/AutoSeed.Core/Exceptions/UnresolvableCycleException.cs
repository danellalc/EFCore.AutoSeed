namespace EFCore.AutoSeed.Exceptions;

/// <summary>
/// Thrown when the model contains a dependency cycle among required (non-nullable) foreign keys,
/// so no insertion order can satisfy every constraint. Break the cycle by making one of the
/// foreign keys involved nullable, or exclude one of the entities from seeding.
/// </summary>
public sealed class UnresolvableCycleException : AutoSeedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnresolvableCycleException"/> class.
    /// </summary>
    /// <param name="entityTypeNames">The full names of the entity types that form the cycle, in dependency order.</param>
    public UnresolvableCycleException(IReadOnlyList<string> entityTypeNames)
        : base(BuildMessage(entityTypeNames))
    {
        EntityTypeNames = entityTypeNames;
    }

    /// <summary>
    /// The full names of the entity types that form the cycle, in dependency order.
    /// </summary>
    public IReadOnlyList<string> EntityTypeNames { get; }

    private static string BuildMessage(IReadOnlyList<string> entityTypeNames)
    {
        string cycle = string.Join(" -> ", entityTypeNames);
        return $"Cannot determine an insertion order for {cycle}: this cycle is formed entirely " +
               "of required foreign keys, so no entity in it can be inserted first. Make one of " +
               "the foreign keys in this cycle nullable, or exclude one of these entities from seeding.";
    }
}
