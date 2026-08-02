namespace EFCore.AutoSeed.Exceptions;

/// <summary>
/// Thrown when <see cref="Pipeline.Persistence"/> cannot seed an entity type: it has no public
/// parameterless constructor, or one of its required principals has no generated rows to
/// reference.
/// </summary>
public sealed class UnsupportedEntityTypeException : AutoSeedException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedEntityTypeException"/> class.
    /// </summary>
    /// <param name="entityTypeName">The full name of the entity type that could not be seeded.</param>
    /// <param name="reason">Why <paramref name="entityTypeName"/> could not be seeded.</param>
    public UnsupportedEntityTypeException(string entityTypeName, string reason)
        : base($"Cannot seed '{entityTypeName}': {reason}.")
    {
        EntityTypeName = entityTypeName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedEntityTypeException"/> class with an underlying cause.
    /// </summary>
    /// <param name="entityTypeName">The full name of the entity type that could not be seeded.</param>
    /// <param name="reason">Why <paramref name="entityTypeName"/> could not be seeded.</param>
    /// <param name="innerException">The exception that caused this failure.</param>
    public UnsupportedEntityTypeException(string entityTypeName, string reason, Exception innerException)
        : base($"Cannot seed '{entityTypeName}': {reason}.", innerException)
    {
        EntityTypeName = entityTypeName;
    }

    /// <summary>
    /// The full name of the entity type that could not be seeded.
    /// </summary>
    public string EntityTypeName { get; }
}
