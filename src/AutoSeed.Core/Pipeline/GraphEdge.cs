using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// A directed dependency between two entity types: <see cref="Principal"/> must be inserted
/// before <see cref="Dependent"/> can reference it.
/// </summary>
/// <param name="Principal">The entity type being referenced. Must be inserted first.</param>
/// <param name="Dependent">The entity type that declares the foreign key.</param>
/// <param name="ForeignKey">The foreign key this dependency comes from.</param>
public sealed record GraphEdge(IEntityType Principal, IEntityType Dependent, IForeignKey ForeignKey);
