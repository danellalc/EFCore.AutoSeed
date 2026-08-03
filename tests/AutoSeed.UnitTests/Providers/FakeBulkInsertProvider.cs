using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Providers;

internal sealed class FakeBulkInsertProvider : EFCore.AutoSeed.Providers.IBulkInsertProvider
{
    private readonly Dictionary<string, List<IReadOnlyDictionary<string, object>>> _insertedByEntityTypeName = [];

    public bool AnyInsertsHappened => _insertedByEntityTypeName.Count > 0;

    public IReadOnlyList<IReadOnlyDictionary<string, object>> Inserted(string entityTypeShortName) =>
        _insertedByEntityTypeName.Single(entry => entry.Key.EndsWith(entityTypeShortName, StringComparison.Ordinal)).Value;

    public Task InsertAsync(
        DbContext context, IEntityType entityType, IReadOnlyList<IReadOnlyDictionary<string, object>> rows, CancellationToken cancellationToken)
    {
        _insertedByEntityTypeName[entityType.Name] = [.. rows];
        return Task.CompletedTask;
    }
}
