using EFCore.AutoSeed.Exceptions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// Fixes up duplicate values for single-property unique keys and unique indexes across an
/// already-generated batch of rows. A colliding value gets a deterministic numeric suffix drawn
/// from <see cref="SeededRandom"/>; composite unique constraints are not handled yet.
/// </summary>
public sealed class UniquenessEnforcer
{
    private const int MaxAttempts = 20;

    /// <summary>
    /// Rewrites duplicate values for every single-property, string-typed unique key or unique
    /// index declared on <paramref name="entityType"/>, in place.
    /// </summary>
    /// <param name="entityType">The entity type <paramref name="rows"/> were generated for.</param>
    /// <param name="rows">The already-generated rows for <paramref name="entityType"/>, in generation order.</param>
    /// <param name="random">The random source deterministic replacement suffixes derive from.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entityType"/>, <paramref name="rows"/> or <paramref name="random"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="UnsatisfiableUniquenessException">
    /// No deterministic candidate could make a colliding value unique.
    /// </exception>
    public void EnsureUnique(IEntityType entityType, IReadOnlyList<Dictionary<string, object>> rows, SeededRandom random)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(random);

        foreach (IProperty property in FindUniqueStringProperties(entityType))
        {
            EnsureUniqueForProperty(entityType, property, rows, random.Derive(property.Name));
        }
    }

    private static IEnumerable<IProperty> FindUniqueStringProperties(IEntityType entityType)
    {
        HashSet<IProperty> properties = [];

        foreach (IKey key in entityType.GetKeys())
        {
            if (key.Properties.Count == 1 && key.Properties[0].ClrType == typeof(string))
            {
                properties.Add(key.Properties[0]);
            }
        }

        foreach (IIndex index in entityType.GetIndexes())
        {
            if (index.IsUnique && index.Properties.Count == 1 && index.Properties[0].ClrType == typeof(string))
            {
                properties.Add(index.Properties[0]);
            }
        }

        return properties;
    }

    private static void EnsureUniqueForProperty(
        IEntityType entityType, IProperty property, IReadOnlyList<Dictionary<string, object>> rows, SeededRandom propertyRandom)
    {
        HashSet<string> seenValues = [];

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            Dictionary<string, object> row = rows[rowIndex];
            if (!row.TryGetValue(property.Name, out object? value) || value is not string text)
            {
                continue;
            }

            if (seenValues.Add(text))
            {
                continue;
            }

            row[property.Name] = MakeUnique(entityType, property, text, seenValues, propertyRandom.Derive(rowIndex));
        }
    }

    private static string MakeUnique(IEntityType entityType, IProperty property, string value, HashSet<string> seenValues, SeededRandom attemptRandom)
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            string candidate = $"{value}-{attemptRandom.Derive(attempt).Next(0, 1_000_000_000)}";
            if (seenValues.Add(candidate))
            {
                return candidate;
            }
        }

        throw new UnsatisfiableUniquenessException(entityType.Name, property.Name);
    }
}
