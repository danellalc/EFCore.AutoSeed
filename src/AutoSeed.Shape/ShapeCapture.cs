namespace EFCore.AutoSeed.Shape;

/// <summary>
/// The row counts captured from a real database, one per seedable entity type. Contains no data
/// row, ever: only counts read from the database engine's own maintained statistics.
/// </summary>
/// <param name="Version">The schema version of this capture, for forward compatibility as more statistics are added.</param>
/// <param name="Tables">The captured row count for every seedable entity type in the model, keyed by <c>IEntityType.Name</c>.</param>
public sealed record ShapeCapture(int Version, IReadOnlyList<TableShape> Tables);

/// <summary>
/// The captured row count for one entity type.
/// </summary>
/// <param name="EntityTypeName"><c>IEntityType.Name</c> of the captured entity type.</param>
/// <param name="RowCount">The approximate row count reported by the database engine's own statistics.</param>
public sealed record TableShape(string EntityTypeName, long RowCount);
