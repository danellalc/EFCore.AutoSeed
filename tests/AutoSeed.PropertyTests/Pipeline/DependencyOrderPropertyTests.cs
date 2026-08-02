using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.PropertyTests.Pipeline.Fixtures;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.PropertyTests.Pipeline;

public sealed class DependencyOrderPropertyTests
{
    [Property]
    public bool OrderIsIndependentOfInputOrderingAndRespectsEveryEdge(int shuffleSeed)
    {
        using GraphFixtureContext context = new();
        ModelReadResult read = new ModelReader().Read(context.Model);

        Random shuffler = new(shuffleSeed);
        List<IEntityType> shuffledEntityTypes = [.. read.EntityTypes.OrderBy(_ => shuffler.Next())];

        IReadOnlyList<IEntityType> canonicalOrder = new CycleResolver().Resolve(read.EntityTypes, read.Edges).Order;
        IReadOnlyList<IEntityType> shuffledOrder = new CycleResolver().Resolve(shuffledEntityTypes, read.Edges).Order;

        bool sameOrderRegardlessOfInput = canonicalOrder.SequenceEqual(shuffledOrder);

        Dictionary<IEntityType, int> position = shuffledOrder
            .Select((entityType, index) => (entityType, index))
            .ToDictionary(pair => pair.entityType, pair => pair.index);

        bool everyEdgeRespected = read.Edges.All(edge => position[edge.Principal] < position[edge.Dependent]);

        return sameOrderRegardlessOfInput && everyEdgeRespected;
    }
}
