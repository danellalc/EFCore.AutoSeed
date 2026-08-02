using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Pipeline.Fixtures;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Pipeline;

public sealed class PipelineTests
{
    [Fact]
    public void LinearChain_OrdersFromLeastToMostDependent()
    {
        using LinearChainContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        Assert.Equal(["Customer", "Order", "OrderItem"], order.Select(ShortName));
    }

    [Fact]
    public void Diamond_OrdersRootFirstAndMergeLast()
    {
        using DiamondContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        Assert.Equal(["Root", "Left", "Right", "Merge"], order.Select(ShortName));
    }

    [Fact]
    public void NullableSelfCycle_IsResolvedByDeferringTheNullableForeignKey()
    {
        using NullableSelfCycleContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        Assert.Equal(["Employee"], order.Select(ShortName));
    }

    [Fact]
    public void RequiredMutualCycle_ThrowsNamingBothEntities()
    {
        using RequiredMutualCycleContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        UnresolvableCycleException exception = Assert.Throws<UnresolvableCycleException>(
            () => new CycleResolver().Resolve(read.EntityTypes, read.Edges));

        IReadOnlyList<string> cycle = Assert.Single(exception.Cycles);
        Assert.Contains(cycle, name => name.Contains("CycleLeft"));
        Assert.Contains(cycle, name => name.Contains("CycleRight"));
    }

    [Fact]
    public void EntityDownstreamOfARequiredCycle_IsNotReportedAsPartOfTheCycle()
    {
        using DownstreamOfCycleContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        UnresolvableCycleException exception = Assert.Throws<UnresolvableCycleException>(
            () => new CycleResolver().Resolve(read.EntityTypes, read.Edges));

        IReadOnlyList<string> cycle = Assert.Single(exception.Cycles);
        Assert.Contains(cycle, name => name.Contains("LoopA"));
        Assert.Contains(cycle, name => name.Contains("LoopB"));
        Assert.DoesNotContain(cycle, name => name.Contains("Downstream"));
    }

    [Fact]
    public void TwoUnrelatedRequiredCycles_AreReportedAsSeparateCycles()
    {
        using DisjointCyclesContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        UnresolvableCycleException exception = Assert.Throws<UnresolvableCycleException>(
            () => new CycleResolver().Resolve(read.EntityTypes, read.Edges));

        Assert.Equal(2, exception.Cycles.Count);
        Assert.Contains(exception.Cycles, cycle => cycle.Any(name => name.Contains("PairOne")));
        Assert.Contains(exception.Cycles, cycle => cycle.Any(name => name.Contains("PairTwo")));
        Assert.DoesNotContain(exception.Cycles, cycle =>
            cycle.Any(name => name.Contains("PairOne")) && cycle.Any(name => name.Contains("PairTwo")));
    }

    [Fact]
    public void MultipleNullableCandidatesInACycle_ResolveTheSameWayEveryTime()
    {
        using MultiNullableCandidateCycleContext firstContext = new();
        ModelReadResult firstRead = new ModelReader().Read(firstContext.Model);
        IReadOnlyList<IEntityType> firstOrder = new CycleResolver().Resolve(firstRead.EntityTypes, firstRead.Edges).Order;

        using MultiNullableCandidateCycleContext secondContext = new();
        ModelReadResult secondRead = new ModelReader().Read(secondContext.Model);
        IReadOnlyList<IEntityType> secondOrder = new CycleResolver().Resolve(secondRead.EntityTypes, secondRead.Edges).Order;

        Assert.Equal(["TriangleX", "TriangleY", "TriangleZ"], firstOrder.Select(ShortName).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(firstOrder.Select(ShortName), secondOrder.Select(ShortName));
    }

    [Fact]
    public void UnrelatedEntities_OrderAlphabeticallyRegardlessOfDeclarationOrder()
    {
        using UnrelatedEntitiesContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        Assert.Equal(["Apple", "Mango", "Zebra"], order.Select(ShortName));
    }

    [Fact]
    public void KeylessEntity_IsSkippedWithReason()
    {
        using KeylessAndOwnedContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        SkippedEntityType skipped = Assert.Single(read.SkippedEntityTypes);
        Assert.Contains("AuditLogEntry", skipped.EntityTypeName);
        Assert.Equal("no primary key", skipped.Reason);
    }

    [Fact]
    public void OwnedType_IsNotSeededSeparately()
    {
        using KeylessAndOwnedContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        Assert.DoesNotContain(read.EntityTypes, entityType => ShortName(entityType) == nameof(Address));
        Assert.DoesNotContain(read.SkippedEntityTypes, skipped => skipped.EntityTypeName.Contains(nameof(Address)));
        Assert.Contains(read.EntityTypes, entityType => ShortName(entityType) == nameof(Invoice));
    }

    [Fact]
    public void Read_WithNullModel_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ModelReader().Read(null!));
    }

    private static string ShortName(IEntityType entityType) => entityType.Name.Split('.')[^1];
}
