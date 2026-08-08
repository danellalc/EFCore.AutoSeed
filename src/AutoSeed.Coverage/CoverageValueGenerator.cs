using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Coverage;

/// <summary>
/// Generates boundary values instead of realistic ones: every enum value in turn, alternating
/// booleans, a string at its empty/one-character/maximum-length boundaries, and every nullable
/// property left null on an entity type's first row and populated on every other row. Stateful and
/// not reentrant, matching the rest of the pipeline's sequential-generation rule.
/// </summary>
public sealed class CoverageValueGenerator
{
    private static readonly DateTime ReferenceNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly Dictionary<IEntityType, int> _nextRowIndexByEntityType = [];

    /// <summary>
    /// Generates the next row's property values for <paramref name="entityType"/>.
    /// </summary>
    /// <param name="entityType">The entity type being generated for.</param>
    /// <param name="random">The random source this row's values derive from.</param>
    /// <returns>The generated values, keyed by property name. A property absent from the result is left at its default.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entityType"/> or <paramref name="random"/> is <see langword="null"/>.</exception>
    public Dictionary<string, object> GenerateRow(IEntityType entityType, SeededRandom random)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(random);

        int rowIndex = _nextRowIndexByEntityType.TryGetValue(entityType, out int nextRowIndex) ? nextRowIndex : 0;
        _nextRowIndexByEntityType[entityType] = rowIndex + 1;

        Dictionary<string, object> values = [];
        foreach (IProperty property in entityType.GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (property.IsForeignKey() || property.IsPrimaryKey() || property.ValueGenerated != ValueGenerated.Never)
            {
                continue;
            }

            if (rowIndex == 0 && property.IsNullable)
            {
                continue;
            }

            int cycleIndex = Math.Max(rowIndex - 1, 0);
            object? value = GenerateValue(property, cycleIndex, random.Derive(property.Name));
            if (value is not null)
            {
                values[property.Name] = value;
            }
        }

        return values;
    }

    /// <summary>
    /// Checks that no entity type in <paramref name="entityTypes"/> declares a required complex
    /// property: <see cref="GenerateRow"/> only walks <see cref="IEntityType.GetProperties"/>, which
    /// never includes a complex property's own properties, so a required one would otherwise reach
    /// the database at its CLR default.
    /// </summary>
    /// <param name="entityTypes">The entity types about to be seeded.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entityTypes"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedPropertyException">A required complex property is present.</exception>
    public static void ValidateRequiredProperties(IEnumerable<IEntityType> entityTypes)
    {
        ArgumentNullException.ThrowIfNull(entityTypes);

        foreach (IEntityType entityType in entityTypes)
        {
            ValidateComplexProperties(entityType, entityType.GetComplexProperties(), path: null);
        }
    }

    private static void ValidateComplexProperties(IEntityType entityType, IEnumerable<IComplexProperty> complexProperties, string? path)
    {
        foreach (IComplexProperty complexProperty in complexProperties)
        {
            string propertyPath = path is null ? complexProperty.Name : $"{path}.{complexProperty.Name}";
            if (!complexProperty.IsNullable)
            {
                throw new UnsupportedPropertyException(
                    entityType.Name,
                    propertyPath,
                    $"is a complex property ({complexProperty.ClrType.Name}); AutoSeed does not generate values for complex " +
                    "properties yet, open an issue describing the shape");
            }

            ValidateComplexProperties(entityType, complexProperty.ComplexType.GetComplexProperties(), propertyPath);
        }
    }

    private static object? GenerateValue(IProperty property, int cycleIndex, SeededRandom random)
    {
        Type clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (clrType == typeof(string))
        {
            return StringBoundaryValue(property, cycleIndex);
        }

        if (clrType.IsEnum)
        {
            Array enumValues = Enum.GetValues(clrType);
            return enumValues.GetValue(cycleIndex % enumValues.Length);
        }

        if (clrType == typeof(bool))
        {
            return cycleIndex % 2 == 0;
        }

        if (clrType == typeof(Guid))
        {
            return SeededGuid(random);
        }

        if (clrType == typeof(DateTime))
        {
            return ReferenceNow.AddDays(-cycleIndex);
        }

        if (clrType == typeof(int))
        {
            return cycleIndex;
        }

        if (clrType == typeof(long))
        {
            return (long)cycleIndex;
        }

        if (clrType == typeof(short))
        {
            return (short)cycleIndex;
        }

        if (clrType == typeof(byte))
        {
            return (byte)(cycleIndex % 256);
        }

        if (clrType == typeof(float))
        {
            return (float)cycleIndex;
        }

        if (clrType == typeof(double))
        {
            return (double)cycleIndex;
        }

        if (clrType == typeof(decimal))
        {
            return (decimal)cycleIndex;
        }

        return null;
    }

    private static string StringBoundaryValue(IProperty property, int cycleIndex)
    {
        int? maxLength = property.GetMaxLength();
        string[] boundaries =
        [
            "",
            "x",
            maxLength is > 0 ? new string('x', maxLength.Value) : "coverage",
        ];

        return boundaries[cycleIndex % boundaries.Length];
    }

    private static Guid SeededGuid(SeededRandom random)
    {
        byte[] bytes = new byte[16];
        for (int index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)random.Next(0, 256);
        }

        return new Guid(bytes);
    }
}
