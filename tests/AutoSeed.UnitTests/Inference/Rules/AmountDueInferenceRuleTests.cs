using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class AmountDueInferenceRuleTests
{
    [Theory]
    [InlineData(nameof(OrderLine.AmountDue))]
    [InlineData(nameof(OrderLine.AmountPayable))]
    public void CanInfer_MatchesAmountDueStyleProperties(string propertyName)
    {
        IProperty property = GetProperty(propertyName);
        Assert.True(new AmountDueInferenceRule().CanInfer(property));
    }

    [Fact]
    public void Infer_WithPriceQuantityAndDiscountSiblings_ReturnsTheDiscountedProduct()
    {
        IProperty property = GetProperty(nameof(OrderLine.AmountDue));
        AmountDueInferenceRule rule = new();
        Dictionary<string, object> siblings = new() { ["UnitPrice"] = 12.5m, ["Quantity"] = 4, ["Discount"] = 0.1m };

        decimal value = (decimal)rule.Infer(property, SeededRandom.FromRootSeed(1), siblings)!;

        Assert.Equal(45.0m, value);
    }

    [Fact]
    public void Infer_WithoutSiblings_FallsBackToAnIndependentDraw()
    {
        IProperty property = GetProperty(nameof(OrderLine.AmountDue));
        AmountDueInferenceRule rule = new();

        decimal value = (decimal)rule.Infer(property, SeededRandom.FromRootSeed(1), new Dictionary<string, object>())!;

        Assert.True(value >= 0m);
    }

    [Fact]
    public void Infer_WithPriceAndQuantityButNoDiscount_FallsBackToAnIndependentDraw()
    {
        IProperty property = GetProperty(nameof(OrderLine.AmountDue));
        AmountDueInferenceRule rule = new();
        Dictionary<string, object> siblings = new() { ["UnitPrice"] = 12.5m, ["Quantity"] = 4 };

        decimal value = (decimal)rule.Infer(property, SeededRandom.FromRootSeed(1), siblings)!;

        Assert.True(value >= 0m);
    }

    [Fact]
    public void Infer_RespectsThePropertysPrecisionEvenWhenCorrelated()
    {
        IProperty property = GetProperty(nameof(OrderLine.TightAmountDue));
        AmountDueInferenceRule rule = new();
        Dictionary<string, object> siblings = new() { ["UnitPrice"] = 90m, ["Quantity"] = 5, ["Discount"] = 0.1m };

        decimal value = (decimal)rule.Infer(property, SeededRandom.FromRootSeed(1), siblings)!;

        Assert.True(value <= 99.99m, $"expected the precision(4,2) bound of 99.99 to be respected, got {value}.");
    }

    [Fact]
    public void Infer_WithTheSameSeedAndNoSiblings_IsDeterministic()
    {
        IProperty property = GetProperty(nameof(OrderLine.AmountDue));
        AmountDueInferenceRule rule = new();

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
            modelBuilder.Entity<OrderLine>().Property(line => line.TightAmountDue).HasPrecision(4, 2);
    }

    private sealed class OrderLine
    {
        public int Id { get; set; }
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal Discount { get; set; }
        public decimal AmountDue { get; set; }
        public decimal AmountPayable { get; set; }
        public decimal TightAmountDue { get; set; }
    }
}
