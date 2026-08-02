using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.SchemaTests;

internal static class SchemaTestSupport
{
    internal static void AssertReferentialIntegrityHolds(DbContext context)
    {
        ILookup<IEntityType, EntityEntry> entriesByEntityType = context.ChangeTracker.Entries().ToLookup(entry => entry.Metadata);

        foreach (IEntityType entityType in context.Model.GetEntityTypes())
        {
            foreach (IForeignKey foreignKey in entityType.GetForeignKeys())
            {
                HashSet<string> principalKeys = [.. entriesByEntityType[foreignKey.PrincipalEntityType]
                    .Select(entry => FormatKey(entry, foreignKey.PrincipalKey.Properties))];

                foreach (EntityEntry dependent in entriesByEntityType[entityType])
                {
                    IReadOnlyList<object?> foreignKeyValues =
                        [.. foreignKey.Properties.Select(property => dependent.Property(property.Name).CurrentValue)];

                    if (foreignKeyValues.Any(value => value is null))
                    {
                        if (foreignKey.IsRequired)
                        {
                            throw new Xunit.Sdk.XunitException(
                                $"Required foreign key {entityType.ClrType.Name}.{foreignKey.Properties[0].Name} is null.");
                        }

                        continue;
                    }

                    if (!principalKeys.Contains(FormatKey(foreignKeyValues)))
                    {
                        throw new Xunit.Sdk.XunitException(
                            $"Foreign key {entityType.ClrType.Name}.{foreignKey.Properties[0].Name} " +
                            $"points at a row that does not exist in {foreignKey.PrincipalEntityType.ClrType.Name}.");
                    }
                }
            }
        }
    }

    private static string FormatKey(EntityEntry entry, IReadOnlyList<IProperty> properties) =>
        FormatKey([.. properties.Select(property => entry.Property(property.Name).CurrentValue)]);

    private static string FormatKey(IReadOnlyList<object?> values)
    {
        StringBuilder builder = new();
        foreach (object? value in values)
        {
            string text = value?.ToString() ?? string.Empty;
            builder.Append(text.Length).Append(':').Append(text);
        }

        return builder.ToString();
    }

    internal static string UniqueDatabaseName() => Guid.NewGuid().ToString("N");
}
