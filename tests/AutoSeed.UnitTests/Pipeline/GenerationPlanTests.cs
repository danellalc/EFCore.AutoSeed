using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Pipeline.Fixtures;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Pipeline;

public sealed class GenerationPlanTests
{
    [Fact]
    public void Plan_GivesTheRootEntityExactlyScaleRows()
    {
        using LinearChainContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(order, read.Edges, scale: 1_000, SeededRandom.FromRootSeed(42));

        EntityGenerationPlan customerPlan = Assert.Single(plan, entry => entry.EntityType.Name.EndsWith("Customer", StringComparison.Ordinal));
        Assert.Equal(1_000, customerPlan.RowCount);
    }

    [Fact]
    public void Plan_GivesDependentEntitiesACountDerivedFromTheirDriver()
    {
        using LinearChainContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan(meanChildrenPerParent: 3.0).Plan(order, read.Edges, scale: 1_000, SeededRandom.FromRootSeed(42));

        int orderCount = Single(plan, "Order").RowCount;
        int orderItemCount = Single(plan, "OrderItem").RowCount;

        Assert.InRange(orderCount, 500, 6_000);
        Assert.InRange(orderItemCount, 500, 30_000);
    }

    [Fact]
    public void Plan_WithTheSameInputs_IsDeterministic()
    {
        using LinearChainContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        IReadOnlyList<EntityGenerationPlan> first = new GenerationPlan().Plan(order, read.Edges, scale: 1_000, SeededRandom.FromRootSeed(42));
        IReadOnlyList<EntityGenerationPlan> second = new GenerationPlan().Plan(order, read.Edges, scale: 1_000, SeededRandom.FromRootSeed(42));

        Assert.Equal(first.Select(entry => entry.RowCount), second.Select(entry => entry.RowCount));
    }

    [Fact]
    public void Plan_ForAnEntityWithTwoRequiredParents_PicksTheAlphabeticallyFirstAsDriver()
    {
        using DiamondContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        IReadOnlyList<EntityGenerationPlan> plan = new GenerationPlan().Plan(order, read.Edges, scale: 100, SeededRandom.FromRootSeed(1));

        int leftCount = Single(plan, "Left").RowCount;
        int mergeCount = Single(plan, "Merge").RowCount;

        Assert.True(leftCount > 0);
        Assert.True(mergeCount >= 0);
    }

    [Fact]
    public void Plan_WithNullArguments_ThrowsArgumentNullException()
    {
        using LinearChainContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;
        SeededRandom random = SeededRandom.FromRootSeed(1);

        Assert.Throws<ArgumentNullException>(() => new GenerationPlan().Plan(null!, read.Edges, 10, random));
        Assert.Throws<ArgumentNullException>(() => new GenerationPlan().Plan(order, null!, 10, random));
        Assert.Throws<ArgumentNullException>(() => new GenerationPlan().Plan(order, read.Edges, 10, null!));
    }

    [Fact]
    public void Plan_WithNonPositiveScale_ThrowsArgumentOutOfRangeException()
    {
        using LinearChainContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationPlan().Plan(order, read.Edges, 0, SeededRandom.FromRootSeed(1)));
    }

    [Fact]
    public void Constructor_WithNonPositiveMean_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationPlan(0));
    }

    private static EntityGenerationPlan Single(IReadOnlyList<EntityGenerationPlan> plan, string shortName) =>
        Assert.Single(plan, entry => entry.EntityType.Name.EndsWith(shortName, StringComparison.Ordinal));
}
