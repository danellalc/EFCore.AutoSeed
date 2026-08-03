using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class QuantityInferenceRuleTests
{
    [Theory]
    [InlineData(nameof(OrderLine.Quantity))]
    [InlineData(nameof(OrderLine.OrderQty))]
    public void CanInfer_MatchesQuantityProperties(string propertyName)
    {
        IProperty property = GetProperty(propertyName);
        Assert.True(new QuantityInferenceRule().CanInfer(property));
    }

    [Fact]
    public void CanInfer_DoesNotMatchAnUnrelatedIntProperty()
    {
        IProperty property = GetProperty(nameof(OrderLine.Id));
        Assert.False(new QuantityInferenceRule().CanInfer(property));
    }

    [Fact]
    public void Infer_AlwaysProducesAValueWithinTheRealisticRange()
    {
        IProperty property = GetProperty(nameof(OrderLine.Quantity));
        QuantityInferenceRule rule = new();

        for (int seed = 0; seed < 200; seed++)
        {
            int value = (int)rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>())!;
            Assert.True(value is >= 1 and <= 20, $"seed {seed}: {value} outside [1, 20].");
        }
    }

    [Fact]
    public void Infer_WithTheSameSeed_IsDeterministic()
    {
        IProperty property = GetProperty(nameof(OrderLine.Quantity));
        QuantityInferenceRule rule = new();

        object? first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());
        object? second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        Assert.Equal(first, second);
    }

    private static IProperty GetProperty(string propertyName)
    {
        using OrderLineContext context = new();
        IEntityType entityType = context.Model.FindEntityType(typeof(OrderLine))
            ?? throw new InvalidOperationException("OrderLine entity type not found.");
        return entityType.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on OrderLine.");
    }

    private sealed class OrderLineContext : DbContext
    {
        public DbSet<OrderLine> OrderLines => Set<OrderLine>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(OrderLineContext));
    }

    private sealed class OrderLine
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        public int OrderQty { get; set; }
    }
}
