using EFCore.AutoSeed.Inference;
using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Inference.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class SimpleStringRuleTests
{
    [Fact]
    public void PostalCode_CanInferAndProducesANonEmptyDeterministicValue() =>
        AssertMatchesAndIsDeterministic(new PostalCodeInferenceRule(), "PostalCode");

    [Fact]
    public void Phone_CanInferAndProducesANonEmptyDeterministicValue() =>
        AssertMatchesAndIsDeterministic(new PhoneInferenceRule(), "Phone");

    [Fact]
    public void Url_CanInferAndProducesANonEmptyDeterministicValue() =>
        AssertMatchesAndIsDeterministic(new UrlInferenceRule(), "Url");

    [Fact]
    public void Slug_CanInferAndProducesANonEmptyDeterministicValue() =>
        AssertMatchesAndIsDeterministic(new SlugInferenceRule(), "Slug");

    [Fact]
    public void IpAddress_CanInferAndProducesANonEmptyDeterministicValue() =>
        AssertMatchesAndIsDeterministic(new IpAddressInferenceRule(), "IpAddress");

    [Fact]
    public void GenericText_CanInferAndProducesANonEmptyDeterministicValue() =>
        AssertMatchesAndIsDeterministic(new GenericTextInferenceRule(), "Nickname");

    [Fact]
    public void GenericText_DoesNotClaimForeignKeyProperties()
    {
        using StringForeignKeyContext context = new();
        IEntityType entityType = context.Model.FindEntityType(typeof(Child))
            ?? throw new InvalidOperationException("Child entity type not found.");
        IProperty foreignKey = entityType.FindProperty(nameof(Child.ParentCode))
            ?? throw new InvalidOperationException("ParentCode property not found.");

        Assert.False(new GenericTextInferenceRule().CanInfer(foreignKey));
    }

    private sealed class StringForeignKeyContext : DbContext
    {
        public DbSet<Parent> Parents => Set<Parent>();
        public DbSet<Child> Children => Set<Child>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(StringForeignKeyContext));

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Parent>().HasKey(parent => parent.Code);
    }

    private sealed class Parent
    {
        public string Code { get; set; } = "";
        public List<Child> Children { get; set; } = [];
    }

    private sealed class Child
    {
        public int Id { get; set; }
        public string ParentCode { get; set; } = "";
        public Parent Parent { get; set; } = null!;
    }

    [Fact]
    public void Url_ProducesAValueShapedLikeAUrl()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Url");
        object value = new UrlInferenceRule().Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        string url = Assert.IsType<string>(value);
        Assert.StartsWith("http", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IpAddress_ProducesFourDotSeparatedOctets()
    {
        IProperty property = InferenceFixtureModel.GetProperty("IpAddress");
        object value = new IpAddressInferenceRule().Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        string ip = Assert.IsType<string>(value);
        Assert.Equal(4, ip.Split('.').Length);
    }

    [Fact]
    public void PostalCode_ContainsAtLeastOneDigit()
    {
        IProperty property = InferenceFixtureModel.GetProperty("PostalCode");
        object value = new PostalCodeInferenceRule().Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        string postalCode = Assert.IsType<string>(value);
        Assert.Contains(postalCode, char.IsDigit);
    }

    [Fact]
    public void Phone_ContainsAtLeastOneDigit()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Phone");
        object value = new PhoneInferenceRule().Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        string phone = Assert.IsType<string>(value);
        Assert.Contains(phone, char.IsDigit);
    }

    [Fact]
    public void Slug_JoinsTwoWordsWithAHyphenAndNoWhitespace()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Slug");
        object value = new SlugInferenceRule().Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        string slug = Assert.IsType<string>(value);
        Assert.Contains('-', slug);
        Assert.DoesNotContain(slug, char.IsWhiteSpace);
    }

    [Fact]
    public void Url_TruncatesValuesLongerThanMaxLength()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Url");
        UrlInferenceRule rule = new();

        bool anyHitTheBoundary = false;
        for (int seed = 0; seed < 50; seed++)
        {
            string value = (string)rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>());
            Assert.True(value.Length <= 15, $"seed {seed}: '{value}' exceeds MaxLength 15.");
            anyHitTheBoundary |= value.Length == 15;
        }

        Assert.True(anyHitTheBoundary, "expected at least one generated URL to actually hit the MaxLength boundary.");
    }

    private static void AssertMatchesAndIsDeterministic(IPropertyInferenceRule rule, string propertyName)
    {
        IProperty property = InferenceFixtureModel.GetProperty(propertyName);
        Assert.True(rule.CanInfer(property));

        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>());

        string firstValue = Assert.IsType<string>(first);
        Assert.NotEmpty(firstValue);
        Assert.Equal(first, second);
    }
}
