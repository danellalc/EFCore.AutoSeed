using EFCore.AutoSeed.Pipeline;
using FsCheck;
using FsCheck.Xunit;

namespace EFCore.AutoSeed.PropertyTests.Pipeline;

public sealed class SeededRandomPropertyTests
{
    [Property]
    public bool SamePathAlwaysProducesTheSameValue(
        long rootSeed, NonNull<string> entityName, int rowIndex, NonNull<string> propertyName)
    {
        double first = Derive(rootSeed, entityName.Get, rowIndex, propertyName.Get);
        double second = Derive(rootSeed, entityName.Get, rowIndex, propertyName.Get);

        return first == second;
    }

    [Property]
    public bool DerivingOtherSiblingsDoesNotChangeAPreviouslyDerivedPath(
        long rootSeed, NonNull<string> entityName, int rowIndex, NonNull<string> propertyName)
    {
        SeededRandom entityScope = SeededRandom.FromRootSeed(rootSeed).Derive(entityName.Get);

        double before = entityScope.Derive(rowIndex).Derive(propertyName.Get).NextDouble();

        entityScope.Derive(rowIndex + 1);
        entityScope.Derive(rowIndex - 1);

        double after = entityScope.Derive(rowIndex).Derive(propertyName.Get).NextDouble();

        return before == after;
    }

    private static double Derive(long rootSeed, string entityName, int rowIndex, string propertyName) =>
        SeededRandom.FromRootSeed(rootSeed).Derive(entityName).Derive(rowIndex).Derive(propertyName).NextDouble();
}
