using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Pipeline.Fixtures;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Pipeline;

public sealed class UniquenessEnforcerTests
{
    [Fact]
    public void EnsureUnique_RewritesDuplicateValues()
    {
        IEntityType entityType = GetWidgetEntityType();
        List<Dictionary<string, object>> rows =
        [
            new() { ["Code"] = "abc", ["Description"] = "one" },
            new() { ["Code"] = "abc", ["Description"] = "two" },
            new() { ["Code"] = "abc", ["Description"] = "three" },
        ];

        new UniquenessEnforcer().EnsureUnique(entityType, rows, SeededRandom.FromRootSeed(42));

        HashSet<string> codes = [.. rows.Select(row => (string)row["Code"])];
        Assert.Equal(3, codes.Count);
    }

    [Fact]
    public void EnsureUnique_LeavesAlreadyUniqueValuesUnchanged()
    {
        IEntityType entityType = GetWidgetEntityType();
        List<Dictionary<string, object>> rows =
        [
            new() { ["Code"] = "one" },
            new() { ["Code"] = "two" },
            new() { ["Code"] = "three" },
        ];

        new UniquenessEnforcer().EnsureUnique(entityType, rows, SeededRandom.FromRootSeed(42));

        Assert.Equal("one", rows[0]["Code"]);
        Assert.Equal("two", rows[1]["Code"]);
        Assert.Equal("three", rows[2]["Code"]);
    }

    [Fact]
    public void EnsureUnique_DoesNotTouchPropertiesWithoutAUniqueConstraint()
    {
        IEntityType entityType = GetWidgetEntityType();
        List<Dictionary<string, object>> rows =
        [
            new() { ["Code"] = "one", ["Description"] = "same" },
            new() { ["Code"] = "two", ["Description"] = "same" },
        ];

        new UniquenessEnforcer().EnsureUnique(entityType, rows, SeededRandom.FromRootSeed(42));

        Assert.Equal("same", rows[0]["Description"]);
        Assert.Equal("same", rows[1]["Description"]);
    }

    [Fact]
    public void EnsureUnique_WithTheSameSeed_IsDeterministic()
    {
        IEntityType entityType = GetWidgetEntityType();

        List<Dictionary<string, object>> firstRows =
        [
            new() { ["Code"] = "abc" },
            new() { ["Code"] = "abc" },
        ];
        List<Dictionary<string, object>> secondRows =
        [
            new() { ["Code"] = "abc" },
            new() { ["Code"] = "abc" },
        ];

        new UniquenessEnforcer().EnsureUnique(entityType, firstRows, SeededRandom.FromRootSeed(42));
        new UniquenessEnforcer().EnsureUnique(entityType, secondRows, SeededRandom.FromRootSeed(42));

        Assert.Equal(firstRows[1]["Code"], secondRows[1]["Code"]);
    }

    [Fact]
    public void EnsureUnique_WithNullArguments_ThrowsArgumentNullException()
    {
        IEntityType entityType = GetWidgetEntityType();
        List<Dictionary<string, object>> rows = [];
        SeededRandom random = SeededRandom.FromRootSeed(1);

        Assert.Throws<ArgumentNullException>(() => new UniquenessEnforcer().EnsureUnique(null!, rows, random));
        Assert.Throws<ArgumentNullException>(() => new UniquenessEnforcer().EnsureUnique(entityType, null!, random));
        Assert.Throws<ArgumentNullException>(() => new UniquenessEnforcer().EnsureUnique(entityType, rows, null!));
    }

    [Fact]
    public void UnsatisfiableUniquenessException_NamesTheEntityAndProperty()
    {
        var exception = new UnsatisfiableUniquenessException("Widget", "Code");

        Assert.Contains("Widget", exception.Message);
        Assert.Contains("Code", exception.Message);
        Assert.IsAssignableFrom<AutoSeedException>(exception);
    }

    private static IEntityType GetWidgetEntityType()
    {
        using UniquenessFixtureContext context = new();
        return context.Model.FindEntityType(typeof(Widget))
            ?? throw new InvalidOperationException("Widget entity type not found in the fixture model.");
    }
}
