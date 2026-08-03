using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Pipeline;

/// <summary>
/// Detects a dependent whose entire primary key is a single required foreign key: an
/// extension-table or shared-primary-key one-to-one, like an <c>OfficeAssignment</c> keyed by
/// <c>InstructorId</c>, which is also its foreign key to <c>Instructor</c>. Such a dependent can
/// have at most one row per principal row; the primary key would collide otherwise.
/// </summary>
public static class SharedPrimaryKey
{
    /// <summary>
    /// Determines whether <paramref name="foreignKey"/> is the entire primary key of its own
    /// declaring entity type.
    /// </summary>
    /// <param name="entityType">The dependent entity type <paramref name="foreignKey"/> is declared on.</param>
    /// <param name="foreignKey">The foreign key to check.</param>
    /// <returns><see langword="true"/> if <paramref name="entityType"/>'s primary key is exactly <paramref name="foreignKey"/>'s properties.</returns>
    public static bool IsDependent(IEntityType entityType, IForeignKey foreignKey)
    {
        IKey? primaryKey = entityType.FindPrimaryKey();
        return primaryKey is not null && primaryKey.Properties.ToHashSet().SetEquals(foreignKey.Properties);
    }
}
