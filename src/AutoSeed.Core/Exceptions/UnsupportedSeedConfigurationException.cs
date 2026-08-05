namespace EFCore.AutoSeed.Exceptions;

/// <summary>
/// Thrown when the <c>configure</c> callback passed to a seeding method excludes or pins the row
/// count of an entity type that has a required foreign key. Only an entity type with no required
/// principal (one <see cref="Pipeline.GenerationPlan"/> would otherwise give <c>scale</c> rows
/// directly) can be excluded or have its row count pinned: any other entity type's row count is
/// derived from its principal's, so a value fixed independently of that principal would corrupt
/// every dependent's own cardinality.
/// </summary>
public sealed class UnsupportedSeedConfigurationException : AutoSeedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedSeedConfigurationException"/> class.
    /// </summary>
    /// <param name="entityTypeName">The full name of the entity type that cannot be configured this way.</param>
    /// <param name="principalEntityTypeName">The full name of the required principal that determines <paramref name="entityTypeName"/>'s row count.</param>
    public UnsupportedSeedConfigurationException(string entityTypeName, string principalEntityTypeName)
        : base(
            $"Cannot exclude or pin the row count of '{entityTypeName}': it has a required foreign key to " +
            $"'{principalEntityTypeName}', so its row count is derived from that principal's and cannot be set " +
            "independently. Only an entity type with no required foreign key supports Exclude() or HasRowCount().")
    {
        EntityTypeName = entityTypeName;
        PrincipalEntityTypeName = principalEntityTypeName;
    }

    /// <summary>
    /// The full name of the entity type that cannot be configured this way.
    /// </summary>
    public string EntityTypeName { get; }

    /// <summary>
    /// The full name of the required principal that determines <see cref="EntityTypeName"/>'s row count.
    /// </summary>
    public string PrincipalEntityTypeName { get; }
}
