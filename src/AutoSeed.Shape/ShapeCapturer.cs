using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Shape;

/// <summary>
/// Captures a <see cref="ShapeCapture"/> for every seedable entity type in a model.
/// </summary>
public sealed class ShapeCapturer
{
    private const int CurrentVersion = 1;

    private readonly IShapeCaptureProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShapeCapturer"/> class.
    /// </summary>
    /// <param name="provider">The provider used to read each entity type's row count.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <see langword="null"/>.</exception>
    public ShapeCapturer(IShapeCaptureProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <summary>
    /// Reads <paramref name="context"/>'s model and captures a row count for every seedable entity type.
    /// </summary>
    /// <param name="context">The context to capture from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The captured row counts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public async Task<ShapeCapture> CaptureAsync(DbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ModelReadResult read = new ModelReader().Read(context.Model);
        List<TableShape> tables = new(read.EntityTypes.Count);

        foreach (IEntityType entityType in read.EntityTypes)
        {
            long rowCount = await _provider.GetRowCountAsync(context, entityType, cancellationToken).ConfigureAwait(false);
            tables.Add(new TableShape(entityType.Name, rowCount));
        }

        return new ShapeCapture(CurrentVersion, tables);
    }
}
