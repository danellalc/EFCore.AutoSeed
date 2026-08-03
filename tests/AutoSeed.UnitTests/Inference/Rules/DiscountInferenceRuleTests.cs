using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class DiscountInferenceRuleTests
{
    [Theory]
    [InlineData(nameof(OrderLine.Discount))]
    [InlineData(nameof(OrderLine.DiscountPercent))]
    [InlineData(nameof(OrderLine.DiscountRate))]
    public void CanInfer_MatchesDiscountStyleProperties(string propertyName)
    {
        IProperty property = GetProperty(propertyName);
        Assert.True(new DiscountInferenceRule().CanInfer(property));
    }

    [Fact]
    public void CanInfer_DoesNotMatchAnUnrelatedDecimalProperty()
    {
        IProperty property = GetProperty(nameof(OrderLine.UnitPrice));
        Assert.False(new DiscountInferenceRule().CanInfer(property));
    }

    [Fact]
    public void Infer_AlwaysProducesAFractionWithinTheRealisticRange()
    {
        IProperty property = GetProperty(nameof(OrderLine.Discount));
        DiscountInferenceRule rule = new();

        for (int seed = 0; seed < 200; seed++)
        {
            decimal value = (decimal)rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>())!;
            Assert.True(value is >= 0m and <= 0.3m, $"seed {seed}: {value} outside [0, 0.3].");
        }
    }

    [Fact]
    public void Infer_WithTheSameSeed_IsDeterministic()
    {
        IProperty property = GetProperty(nameof(OrderLine.Discount));
        DiscountInferenceRule rule = new();

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
        public decimal UnitPrice { get; set; }
        public decimal Discount { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountRate { get; set; }
    }
}
