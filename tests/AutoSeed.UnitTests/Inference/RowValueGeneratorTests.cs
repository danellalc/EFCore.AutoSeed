using EFCore.AutoSeed.Inference;
using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Inference.Fixtures;
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
            "CreatedAt", "UpdatedAt", "DeletedAt",
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
            DateTime deletedAt = (DateTime)values["DeletedAt"];

            Assert.True(createdAt <= updatedAt, $"seed {seed}: CreatedAt after UpdatedAt.");
            Assert.True(updatedAt <= deletedAt, $"seed {seed}: UpdatedAt after DeletedAt.");
        }
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
    public void Constructor_WithNullRules_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RowValueGenerator(null!));
    }

    private static RowValueGenerator CreateGenerator() => new(
    [
        new NameInferenceRule(),
        new EmailInferenceRule(),
        new DocumentInferenceRule(),
        new PostalCodeInferenceRule(),
        new PhoneInferenceRule(),
        new DecimalAmountInferenceRule(),
        new UrlInferenceRule(),
        new SlugInferenceRule(),
        new IpAddressInferenceRule(),
        new CreatedAtInferenceRule(ReferenceNow),
        new UpdatedAtInferenceRule(ReferenceNow),
        new DeletedAtInferenceRule(ReferenceNow),
    ]);
}
