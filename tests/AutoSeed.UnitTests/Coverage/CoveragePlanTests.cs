using EFCore.AutoSeed.Coverage;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Pipeline.Fixtures;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Coverage;

public sealed class CoveragePlanTests
{
    [Fact]
    public void Plan_GivesARootEntityAtLeastThreeRows()
    {
        using LinearChainContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        IReadOnlyList<EntityGenerationPlan> plan = new CoveragePlan().Plan(order, read.Edges);

        Assert.True(Single(plan, "Customer").RowCount >= 3);
    }

    [Fact]
    public void Plan_ForADependent_DemonstratesZeroOneAndManyAcrossDriverRows()
    {
        using LinearChainContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        IReadOnlyList<EntityGenerationPlan> plan = new CoveragePlan().Plan(order, read.Edges);

        EntityGenerationPlan orderPlan = Single(plan, "Order");
        Assert.NotNull(orderPlan.ChildCountsByDriverRow);
        IReadOnlyList<int> childCounts = orderPlan.ChildCountsByDriverRow!;

        Assert.Contains(0, childCounts);
        Assert.Contains(1, childCounts);
        Assert.True(childCounts.Max() > 1, "expected at least one driver row with more than one child.");
    }

    [Fact]
    public void Plan_ForASharedPrimaryKeyOneToOne_GivesExactlyOneDependentRowPerPrincipalRow()
    {
        using SharedPrimaryKeyContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        IReadOnlyList<EntityGenerationPlan> plan = new CoveragePlan().Plan(order, read.Edges);

        int instructorCount = Single(plan, "Instructor").RowCount;
        EntityGenerationPlan officeAssignmentPlan = Single(plan, "OfficeAssignment");

        Assert.Equal(instructorCount, officeAssignmentPlan.RowCount);
        Assert.All(officeAssignmentPlan.ChildCountsByDriverRow!, count => Assert.Equal(1, count));
    }

    [Fact]
    public void Plan_WithNullArguments_ThrowsArgumentNullException()
    {
        using LinearChainContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);
        IReadOnlyList<IEntityType> order = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;

        Assert.Throws<ArgumentNullException>(() => new CoveragePlan().Plan(null!, read.Edges));
        Assert.Throws<ArgumentNullException>(() => new CoveragePlan().Plan(order, null!));
    }

    private static EntityGenerationPlan Single(IReadOnlyList<EntityGenerationPlan> plan, string shortName) =>
        Assert.Single(plan, entry => entry.EntityType.Name.EndsWith(shortName, StringComparison.Ordinal));
}
