using EFCore.AutoSeed.Inference;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests;

public sealed class AutoSeedDiffTests
{
    private static int _databaseCounter;

    [Fact]
    public void Compare_WithIdenticalSnapshots_ReportsNoChanges()
    {
        AutoSeedPlanSnapshot snapshot = new(
            AutoSeedPlanSnapshot.CurrentVersion,
            ["A", "B"],
            new Dictionary<string, int> { ["A"] = 10, ["B"] = 20 },
            [],
            []);

        AutoSeedDiffResult diff = AutoSeedDiff.Compare(snapshot, snapshot);

        Assert.False(diff.HasChanges);
        Assert.Equal("No differences from the baseline.", diff.ToReport());
    }

    [Fact]
    public void Compare_WithANewlyAddedEntityType_ReportsItAsAdded()
    {
        AutoSeedPlanSnapshot baseline = new(1, ["A"], new Dictionary<string, int> { ["A"] = 10 }, [], []);
        AutoSeedPlanSnapshot current = new(1, ["A", "B"], new Dictionary<string, int> { ["A"] = 10, ["B"] = 5 }, [], []);

        AutoSeedDiffResult diff = AutoSeedDiff.Compare(baseline, current);

        Assert.True(diff.HasChanges);
        Assert.Equal(["B"], diff.AddedEntityTypes);
        Assert.Empty(diff.RemovedEntityTypes);
        Assert.Contains("now seedable", diff.ToReport());
    }

    [Fact]
    public void Compare_WithARemovedEntityType_ReportsItAsRemoved()
    {
        AutoSeedPlanSnapshot baseline = new(1, ["A", "B"], new Dictionary<string, int> { ["A"] = 10, ["B"] = 5 }, [], []);
        AutoSeedPlanSnapshot current = new(1, ["A"], new Dictionary<string, int> { ["A"] = 10 }, [], []);

        AutoSeedDiffResult diff = AutoSeedDiff.Compare(baseline, current);

        Assert.True(diff.HasChanges);
        Assert.Equal(["B"], diff.RemovedEntityTypes);
        Assert.Contains("no longer seedable", diff.ToReport());
    }

    [Fact]
    public void Compare_WithADifferentRowCount_ReportsTheChange()
    {
        AutoSeedPlanSnapshot baseline = new(1, ["A"], new Dictionary<string, int> { ["A"] = 10 }, [], []);
        AutoSeedPlanSnapshot current = new(1, ["A"], new Dictionary<string, int> { ["A"] = 25 }, [], []);

        AutoSeedDiffResult diff = AutoSeedDiff.Compare(baseline, current);

        AutoSeedRowCountChange change = Assert.Single(diff.RowCountChanges);
        Assert.Equal("A", change.EntityTypeName);
        Assert.Equal(10, change.BaselineRowCount);
        Assert.Equal(25, change.CurrentRowCount);
        Assert.Contains("10 -> 25", diff.ToReport());
    }

    [Fact]
    public void Compare_WithANewlySkippedEntityType_ReportsIt()
    {
        AutoSeedPlanSnapshot baseline = new(1, ["A"], new Dictionary<string, int> { ["A"] = 10 }, [], []);
        AutoSeedPlanSnapshot current = new(1, [], new Dictionary<string, int>(), [new("A", "no public parameterless constructor")], []);

        AutoSeedDiffResult diff = AutoSeedDiff.Compare(baseline, current);

        AutoSeedSkipChange change = Assert.Single(diff.SkipChanges);
        Assert.Equal("A", change.EntityTypeName);
        Assert.Null(change.BaselineReason);
        Assert.Equal("no public parameterless constructor", change.CurrentReason);
        Assert.Contains("newly skipped", diff.ToReport());
    }

    [Fact]
    public void Compare_WithANewlyResolvedSkip_ReportsIt()
    {
        AutoSeedPlanSnapshot baseline = new(1, [], new Dictionary<string, int>(), [new("A", "no public parameterless constructor")], []);
        AutoSeedPlanSnapshot current = new(1, ["A"], new Dictionary<string, int> { ["A"] = 10 }, [], []);

        AutoSeedDiffResult diff = AutoSeedDiff.Compare(baseline, current);

        AutoSeedSkipChange change = Assert.Single(diff.SkipChanges);
        Assert.Equal("A", change.EntityTypeName);
        Assert.Equal("no public parameterless constructor", change.BaselineReason);
        Assert.Null(change.CurrentReason);
        Assert.Contains("no longer skipped", diff.ToReport());
    }

    [Fact]
    public void Compare_WithADifferentSkipReason_ReportsIt()
    {
        AutoSeedPlanSnapshot baseline = new(1, [], new Dictionary<string, int>(), [new("A", "abstract type, cannot be instantiated")], []);
        AutoSeedPlanSnapshot current = new(1, [], new Dictionary<string, int>(), [new("A", "no primary key")], []);

        AutoSeedDiffResult diff = AutoSeedDiff.Compare(baseline, current);

        AutoSeedSkipChange change = Assert.Single(diff.SkipChanges);
        Assert.Equal("abstract type, cannot be instantiated", change.BaselineReason);
        Assert.Equal("no primary key", change.CurrentReason);
        Assert.Contains("skip reason changed", diff.ToReport());
    }

    [Fact]
    public void Compare_WithAnAddedCycle_ReportsIt()
    {
        AutoSeedPlanSnapshot baseline = new(1, ["A", "B"], new Dictionary<string, int> { ["A"] = 10, ["B"] = 10 }, [], []);
        AutoSeedPlanSnapshot current = new(1, ["A", "B"], new Dictionary<string, int> { ["A"] = 10, ["B"] = 10 }, [], [new("A", "B", "BId")]);

        AutoSeedDiffResult diff = AutoSeedDiff.Compare(baseline, current);

        AutoSeedPlanCycle cycle = Assert.Single(diff.AddedCycles);
        Assert.Equal("A", cycle.DependentEntityTypeName);
        Assert.Equal("B", cycle.PrincipalEntityTypeName);
        Assert.Contains("Cycle:", diff.ToReport());
    }

    [Fact]
    public void Compare_WithARemovedCycle_ReportsIt()
    {
        AutoSeedPlanSnapshot baseline = new(1, ["A", "B"], new Dictionary<string, int> { ["A"] = 10, ["B"] = 10 }, [], [new("A", "B", "BId")]);
        AutoSeedPlanSnapshot current = new(1, ["A", "B"], new Dictionary<string, int> { ["A"] = 10, ["B"] = 10 }, [], []);

        AutoSeedDiffResult diff = AutoSeedDiff.Compare(baseline, current);

        Assert.Single(diff.RemovedCycles);
        Assert.Empty(diff.AddedCycles);
    }

    [Fact]
    public void Compare_WithNullArguments_ThrowsArgumentNullException()
    {
        AutoSeedPlanSnapshot snapshot = new(1, [], new Dictionary<string, int>(), [], []);

        Assert.Throws<ArgumentNullException>(() => AutoSeedDiff.Compare(null!, snapshot));
        Assert.Throws<ArgumentNullException>(() => AutoSeedDiff.Compare(snapshot, null!));
    }

    [Fact]
    public void FromExplainResult_WithNullResult_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AutoSeedPlanSnapshot.FromExplainResult(null!));
    }

    [Fact]
    public async Task FromExplainResult_ReflectsTheLiveExplainResult()
    {
        using DiffCustomerOrderContext context = NewContext();

        AutoSeedExplainResult result = await context.AutoSeedExplainAsync(seed: 42, scale: 10);
        AutoSeedPlanSnapshot snapshot = AutoSeedPlanSnapshot.FromExplainResult(result);

        Assert.Equal(result.Order.Select(entityType => entityType.Name), snapshot.Order);
        Assert.Equal(result.RowCounts, snapshot.RowCounts);
        Assert.Empty(snapshot.Cycles);
    }

    [Fact]
    public async Task Compare_BetweenTwoScales_ReportsTheRowCountChange()
    {
        using DiffCustomerOrderContext baselineContext = NewContext();
        using DiffCustomerOrderContext currentContext = NewContext();

        AutoSeedPlanSnapshot baseline = AutoSeedPlanSnapshot.FromExplainResult(await baselineContext.AutoSeedExplainAsync(seed: 42, scale: 10));
        AutoSeedPlanSnapshot current = AutoSeedPlanSnapshot.FromExplainResult(await currentContext.AutoSeedExplainAsync(seed: 42, scale: 20));

        AutoSeedDiffResult diff = AutoSeedDiff.Compare(baseline, current);

        Assert.True(diff.HasChanges);
        Assert.NotEmpty(diff.RowCountChanges);
        Assert.All(diff.RowCountChanges, change => Assert.True(change.CurrentRowCount > change.BaselineRowCount));
    }

    [Fact]
    public async Task AutoSeedPlanFile_RoundTripsThroughJson()
    {
        using DiffCustomerOrderContext context = NewContext();
        AutoSeedExplainResult result = await context.AutoSeedExplainAsync(seed: 42, scale: 10);
        AutoSeedPlanSnapshot original = AutoSeedPlanSnapshot.FromExplainResult(result);

        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.autoseed-diff-test.json");
        try
        {
            await AutoSeedPlanFile.WriteAsync(original, path);
            AutoSeedPlanSnapshot roundTripped = await AutoSeedPlanFile.ReadAsync(path);

            Assert.Equal(original.Version, roundTripped.Version);
            Assert.Equal(original.Order, roundTripped.Order);
            Assert.Equal(original.RowCounts, roundTripped.RowCounts);
            Assert.False(AutoSeedDiff.Compare(original, roundTripped).HasChanges);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AutoSeedPlanFile_WithNullArguments_ThrowsArgumentNullException()
    {
        AutoSeedPlanSnapshot snapshot = new(1, [], new Dictionary<string, int>(), [], []);

        await Assert.ThrowsAsync<ArgumentNullException>(() => AutoSeedPlanFile.WriteAsync(null!, "path.json"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => AutoSeedPlanFile.WriteAsync(snapshot, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => AutoSeedPlanFile.ReadAsync(null!));
    }

    private static DiffCustomerOrderContext NewContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new DiffCustomerOrderContext($"{nameof(DiffCustomerOrderContext)}_{id}");
    }
}

public sealed class DiffCustomerOrderContext(string databaseName) : DbContext
{
    public DbSet<DiffCustomer> Customers => Set<DiffCustomer>();
    public DbSet<DiffOrder> Orders => Set<DiffOrder>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);
}

public sealed class DiffCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class DiffOrder
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public DiffCustomer Customer { get; set; } = null!;
}
