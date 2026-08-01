namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// An entity type present in the model that <see cref="ModelReader"/> excluded from seeding.
/// </summary>
/// <param name="EntityTypeName">The full name of the excluded entity type.</param>
/// <param name="Reason">Why the entity type was excluded.</param>
public sealed record SkippedEntityType(string EntityTypeName, string Reason);
