namespace EFCore.AutoSeed.Exceptions;

/// <summary>
/// Thrown when <see cref="Pipeline.UniquenessEnforcer"/> could not find a unique value for a
/// property after exhausting every deterministic candidate it is willing to try.
/// </summary>
public sealed class UnsatisfiableUniquenessException : AutoSeedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsatisfiableUniquenessException"/> class.
    /// </summary>
    /// <param name="entityTypeName">The full name of the entity type that owns the property.</param>
    /// <param name="propertyName">The name of the property that could not be made unique.</param>
    public UnsatisfiableUniquenessException(string entityTypeName, string propertyName)
        : base(BuildMessage(entityTypeName, propertyName))
    {
        EntityTypeName = entityTypeName;
        PropertyName = propertyName;
    }

    /// <summary>
    /// The full name of the entity type that owns the property.
    /// </summary>
    public string EntityTypeName { get; }

    /// <summary>
    /// The name of the property that could not be made unique.
    /// </summary>
    public string PropertyName { get; }

    private static string BuildMessage(string entityTypeName, string propertyName) =>
        $"Could not generate a unique value for {entityTypeName}.{propertyName}: every deterministic " +
        "candidate was already taken. Reduce the row count or relax the uniqueness constraint.";
}
