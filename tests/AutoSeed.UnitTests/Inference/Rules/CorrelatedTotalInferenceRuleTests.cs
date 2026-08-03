using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class CorrelatedTotalInferenceRuleTests
{
    [Theory]
    [InlineData(nameof(OrderLine.Total))]
    [InlineData(nameof(OrderLine.Subtotal))]
    [InlineData(nameof(OrderLine.LineTotal))]
    public void CanInfer_MatchesTotalStyleProperties(string propertyName)
    {
        IProperty property = GetProperty(propertyName);
        Assert.True(new CorrelatedTotalInferenceRule().CanInfer(property));
    }

    [Fact]
    public void Infer_WithPriceAndQuantitySiblings_ReturnsTheirProduct()
    {
        IProperty property = GetProperty(nameof(OrderLine.Total));
        CorrelatedTotalInferenceRule rule = new();
        Dictionary<string, object> siblings = new() { ["UnitPrice"] = 12.5m, ["Quantity"] = 4 };

        decimal value = (decimal)rule.Infer(property, SeededRandom.FromRootSeed(1), siblings)!;

        Assert.Equal(50.0m, value);
    }

    [Fact]
    public void Infer_WithoutSiblings_FallsBackToAnIndependentDraw()
    {
        IProperty property = GetProperty(nameof(OrderLine.Total));
        CorrelatedTotalInferenceRule rule = new();

        decimal value = (decimal)rule.Infer(property, SeededRandom.FromRootSeed(1), new Dictionary<string, object>())!;

        Assert.True(value >= 0m);
    }

    [Fact]
    public void Infer_RespectsThePropertysPrecisionEvenWhenCorrelated()
    {
        IProperty property = GetProperty(nameof(OrderLine.TightTotal));
        CorrelatedTotalInferenceRule rule = new();
        Dictionary<string, object> siblings = new() { ["UnitPrice"] = 90m, ["Quantity"] = 5 };

        decimal value = (decimal)rule.Infer(property, SeededRandom.FromRootSeed(1), siblings)!;

        Assert.True(value <= 99.99m, $"expected the precision(4,2) bound of 99.99 to be respected, got {value}.");
    }

    [Fact]
    public void Infer_WithTheSameSeedAndNoSiblings_IsDeterministic()
    {
        IProperty property = GetProperty(nameof(OrderLine.Total));
        CorrelatedTotalInferenceRule rule = new();

        object? first = rule.Infer(property, SeededRandom.FromRootSeed(7), new Dictionary<string, object>());
        object? second = rule.Infer(property, SeededRandom.FromRootSeed(7), new Dictionary<string, object>());

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

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<OrderLine>().Property(line => line.TightTotal).HasPrecision(4, 2);
    }

    private sealed class OrderLine
    {
        public int Id { get; set; }
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal Total { get; set; }
        public decimal Subtotal { get; set; }
        public decimal LineTotal { get; set; }
        public decimal TightTotal { get; set; }
    }
}
