using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Fixtures;

internal static class InferenceFixtureModel
{
    internal static IEntityType GetPersonEntityType()
    {
        using InferenceFixtureContext context = new();
        return context.Model.FindEntityType(typeof(Person))
            ?? throw new InvalidOperationException("Person entity type not found in the fixture model.");
    }

    internal static IProperty GetProperty(string propertyName)
    {
        IEntityType entityType = GetPersonEntityType();
        return entityType.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on Person.");
    }
}
