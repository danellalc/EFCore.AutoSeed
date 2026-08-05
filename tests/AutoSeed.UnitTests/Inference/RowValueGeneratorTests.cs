using EFCore.AutoSeed.Distributions;
using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Inference;
using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Inference.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference;

public sealed class RowValueGeneratorTests
{
    private static readonly DateTime ReferenceNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GenerateRow_WithTheFullRuleSet_InfersEveryRecognizedProperty()
    {
        RowValueGenerator generator = CreateGenerator();
        IEntityType entityType = InferenceFixtureModel.GetPersonEntityType();

        IReadOnlyDictionary<string, object> values = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(42));

        string[] expectedProperties =
        [
            "FirstName", "LastName", "Email", "Cpf", "Cnpj", "PostalCode", "Phone",
            "Balance", "TinyPrice", "EqualScalePrice", "HugeTotal", "Url", "Slug", "IpAddress",
            "CreatedAt", "UpdatedAt",
        ];

        foreach (string propertyName in expectedProperties)
        {
            Assert.True(values.ContainsKey(propertyName), $"expected a generated value for '{propertyName}'.");
        }

        Assert.False(values.ContainsKey("Id"));
    }

    [Fact]
    public void GenerateRow_WithTheSameSeed_IsFullyDeterministic()
    {
        RowValueGenerator generator = CreateGenerator();
        IEntityType entityType = InferenceFixtureModel.GetPersonEntityType();

        IReadOnlyDictionary<string, object> first = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(42));
        IReadOnlyDictionary<string, object> second = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(42));

        Assert.Equal(first, second);
    }

    [Fact]
    public void GenerateRow_KeepsTheLifecycleTimestampsInOrder()
    {
        RowValueGenerator generator = CreateGenerator();
        IEntityType entityType = InferenceFixtureModel.GetPersonEntityType();

        for (int seed = 0; seed < 50; seed++)
        {
            IReadOnlyDictionary<string, object> values = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(seed));

            DateTime createdAt = (DateTime)values["CreatedAt"];
            DateTime updatedAt = (DateTime)values["UpdatedAt"];

            Assert.True(createdAt <= updatedAt, $"seed {seed}: CreatedAt after UpdatedAt.");

            if (values.TryGetValue("DeletedAt", out object? deletedAtValue))
            {
                Assert.True(updatedAt <= (DateTime)deletedAtValue, $"seed {seed}: UpdatedAt after DeletedAt.");
            }
        }
    }

    [Fact]
    public void GenerateRow_LeavesANullableNonForeignKeyPropertyAbsentOnSomeRowsButNotOthers()
    {
        RowValueGenerator generator = CreateGenerator();
        IEntityType entityType = InferenceFixtureModel.GetPersonEntityType();

        int present = 0;
        const int TotalSeeds = 200;
        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            IReadOnlyDictionary<string, object> values = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(seed));
            if (values.ContainsKey("DeletedAt"))
            {
                present++;
            }
        }

        Assert.True(present > TotalSeeds * 0.7, $"expected most of {TotalSeeds} rows to have DeletedAt, got {present}.");
        Assert.True(present < TotalSeeds, "expected at least one row to leave DeletedAt null.");
    }

    [Fact]
    public void GenerateRow_KeepsEmailCoherentWithTheGeneratedName()
    {
        RowValueGenerator generator = new([new NameInferenceRule(), new EmailInferenceRule()]);
        IEntityType entityType = InferenceFixtureModel.GetPersonEntityType();

        for (int seed = 0; seed < 20; seed++)
        {
            IReadOnlyDictionary<string, object> values = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(seed));

            string firstName = (string)values["FirstName"];
            string email = (string)values["Email"];

            Assert.Contains(firstName, email, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void GenerateRow_WithDirtyDataAll_SometimesButNotAlwaysAltersAFreeTextValue()
    {
        RowValueGenerator dirty = CreateGenerator(dirtyData: DirtyDataKind.All);
        RowValueGenerator clean = CreateGenerator();
        IEntityType entityType = InferenceFixtureModel.GetPersonEntityType();

        int changed = 0;
        const int TotalSeeds = 200;
        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            object dirtyFirstName = dirty.GenerateRow(entityType, SeededRandom.FromRootSeed(seed))["FirstName"];
            object cleanFirstName = clean.GenerateRow(entityType, SeededRandom.FromRootSeed(seed))["FirstName"];

            if (!Equals(dirtyFirstName, cleanFirstName))
            {
                changed++;
            }
        }

        Assert.True(changed > 0, "expected at least one of 200 rows to have its FirstName altered by dirty data noise.");
        Assert.True(changed < TotalSeeds, "expected most rows to keep their FirstName unchanged, dirty data noise is a minority.");
    }

    [Fact]
    public void GenerateRow_NeverAppliesDirtyDataToAnEmailAddress()
    {
        RowValueGenerator dirty = new([new NameInferenceRule(), new EmailInferenceRule()], dirtyData: DirtyDataKind.All);
        RowValueGenerator clean = new([new NameInferenceRule(), new EmailInferenceRule()]);
        IEntityType entityType = InferenceFixtureModel.GetPersonEntityType();

        for (int seed = 0; seed < 50; seed++)
        {
            object dirtyEmail = dirty.GenerateRow(entityType, SeededRandom.FromRootSeed(seed))["Email"];
            object cleanEmail = clean.GenerateRow(entityType, SeededRandom.FromRootSeed(seed))["Email"];

            Assert.Equal(cleanEmail, dirtyEmail);
        }
    }

    [Fact]
    public void Constructor_WithNullRules_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RowValueGenerator(null!));
    }

    [Fact]
    public void ValidateRequiredProperties_WithARequiredPropertyNoRuleRecognizes_ThrowsUnsupportedPropertyException()
    {
        using TphDiscriminatorContext context = new();
        IEntityType employeeEntityType = context.Model.FindEntityType(typeof(DiscriminatorEmployee))
            ?? throw new InvalidOperationException("DiscriminatorEmployee entity type not found.");

        RowValueGenerator generator = CreateGenerator();

        UnsupportedPropertyException exception = Assert.Throws<UnsupportedPropertyException>(
            () => generator.ValidateRequiredProperties([employeeEntityType]));

        Assert.Equal("Name", exception.PropertyName);
        Assert.Contains("DiscriminatorEmployee", exception.EntityTypeName);
    }

    [Fact]
    public void ValidateRequiredProperties_NeverFlagsTheHierarchyDiscriminatorColumn()
    {
        using TphDiscriminatorContext context = new();
        IEntityType employeeEntityType = context.Model.FindEntityType(typeof(DiscriminatorEmployee))
            ?? throw new InvalidOperationException("DiscriminatorEmployee entity type not found.");
        string discriminatorPropertyName = employeeEntityType.FindDiscriminatorProperty()!.Name;

        RowValueGenerator generator = new([]);

        UnsupportedPropertyException exception = Assert.Throws<UnsupportedPropertyException>(
            () => generator.ValidateRequiredProperties([employeeEntityType]));

        Assert.Equal("Name", exception.PropertyName);
        Assert.NotEqual(discriminatorPropertyName, exception.PropertyName);
    }

    [Fact]
    public void ValidateRequiredProperties_WithEveryRequiredPropertyCovered_DoesNotThrow()
    {
        RowValueGenerator generator = new(
        [
            new NameInferenceRule(),
            new EmailInferenceRule(),
            new DocumentInferenceRule(),
            new PostalCodeInferenceRule(),
            new PhoneInferenceRule(),
            new DecimalAmountInferenceRule(),
            new CorrelatedTotalInferenceRule(),
            new UrlInferenceRule(),
            new SlugInferenceRule(),
            new IpAddressInferenceRule(),
            new CreatedAtInferenceRule(ReferenceNow),
            new UpdatedAtInferenceRule(ReferenceNow),
            new DeletedAtInferenceRule(ReferenceNow),
            new GenericTextInferenceRule(),
            new GenericNumberInferenceRule(),
            new GenericBooleanInferenceRule(),
            new GenericEnumInferenceRule(),
            new GenericGuidInferenceRule(),
            new GenericDateTimeInferenceRule(ReferenceNow),
            new GenericDateOnlyInferenceRule(ReferenceNow),
            new GenericTimeOnlyInferenceRule(),
            new GenericTimeSpanInferenceRule(),
            new GenericByteArrayInferenceRule(),
        ]);
        IEntityType entityType = InferenceFixtureModel.GetPersonEntityType();

        Exception? exception = Record.Exception(() => generator.ValidateRequiredProperties([entityType]));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateRequiredProperties_WithNullEntityTypes_ThrowsArgumentNullException()
    {
        RowValueGenerator generator = CreateGenerator();
        Assert.Throws<ArgumentNullException>(() => generator.ValidateRequiredProperties(null!));
    }

    [Fact]
    public void GenerateRow_WithNullRateZero_NeverLeavesAnEligibleNullablePropertyAbsent()
    {
        RowValueGenerator generator = CreateGenerator(nullRate: 0);
        IEntityType entityType = InferenceFixtureModel.GetPersonEntityType();

        for (int seed = 0; seed < 100; seed++)
        {
            IReadOnlyDictionary<string, object> values = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(seed));
            Assert.True(values.ContainsKey("DeletedAt"), $"seed {seed}: expected DeletedAt to be present with nullRate 0.");
        }
    }

    [Fact]
    public void GenerateRow_WithNullRateOne_AlwaysLeavesAnEligibleNullablePropertyAbsent()
    {
        RowValueGenerator generator = CreateGenerator(nullRate: 1);
        IEntityType entityType = InferenceFixtureModel.GetPersonEntityType();

        for (int seed = 0; seed < 100; seed++)
        {
            IReadOnlyDictionary<string, object> values = generator.GenerateRow(entityType, SeededRandom.FromRootSeed(seed));
            Assert.False(values.ContainsKey("DeletedAt"), $"seed {seed}: expected DeletedAt to be absent with nullRate 1.");
        }
    }

    [Fact]
    public void GenerateRow_NeverGeneratesAValueForATableThatHierarchyDiscriminatorColumn()
    {
        using TphDiscriminatorContext context = new();
        IEntityType employeeEntityType = context.Model.FindEntityType(typeof(DiscriminatorEmployee))
            ?? throw new InvalidOperationException("DiscriminatorEmployee entity type not found.");
        IEntityType managerEntityType = context.Model.FindEntityType(typeof(DiscriminatorManager))
            ?? throw new InvalidOperationException("DiscriminatorManager entity type not found.");

        RowValueGenerator generator = CreateGenerator();

        IReadOnlyDictionary<string, object> employeeValues = generator.GenerateRow(employeeEntityType, SeededRandom.FromRootSeed(1));
        IReadOnlyDictionary<string, object> managerValues = generator.GenerateRow(managerEntityType, SeededRandom.FromRootSeed(1));

        string discriminatorPropertyName = employeeEntityType.FindDiscriminatorProperty()!.Name;
        Assert.False(employeeValues.ContainsKey(discriminatorPropertyName));
        Assert.False(managerValues.ContainsKey(discriminatorPropertyName));
    }

    private sealed class TphDiscriminatorContext : DbContext
    {
        public DbSet<DiscriminatorEmployee> Employees => Set<DiscriminatorEmployee>();
        public DbSet<DiscriminatorManager> Managers => Set<DiscriminatorManager>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(TphDiscriminatorContext));
    }

    private class DiscriminatorEmployee
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class DiscriminatorManager : DiscriminatorEmployee
    {
        public decimal Budget { get; set; }
    }

    private static RowValueGenerator CreateGenerator(double nullRate = NullRateSampler.DefaultRate, DirtyDataKind dirtyData = DirtyDataKind.None) => new(
    [
        new NameInferenceRule(),
        new EmailInferenceRule(),
        new DocumentInferenceRule(),
        new PostalCodeInferenceRule(),
        new PhoneInferenceRule(),
        new DecimalAmountInferenceRule(),
        new CorrelatedTotalInferenceRule(),
        new UrlInferenceRule(),
        new SlugInferenceRule(),
        new IpAddressInferenceRule(),
        new CreatedAtInferenceRule(ReferenceNow),
        new UpdatedAtInferenceRule(ReferenceNow),
        new DeletedAtInferenceRule(ReferenceNow),
    ], nullRate, dirtyData);
}
