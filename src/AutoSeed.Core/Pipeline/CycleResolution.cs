using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// The result of <see cref="CycleResolver.Resolve"/>: a full insertion order, plus every foreign
/// key that had to be deferred to a second pass to make that order possible.
/// </summary>
/// <param name="Order">The full insertion order.</param>
/// <param name="DeferredEdges">
/// Every foreign key excluded from ordering because it was the nullable link that broke a cycle.
/// Insert dependents with this foreign key left null, then update it once the principal exists.
/// </param>
public sealed record CycleResolution(IReadOnlyList<IEntityType> Order, IReadOnlyList<GraphEdge> DeferredEdges);
