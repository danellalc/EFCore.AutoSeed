using EFCore.AutoSeed.Inference;
using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Inference.Fixtures;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class GenericPrimitiveRuleTests
{
    [Fact]
    public void GenericNumber_CanInferAnIntProperty()
    {
        IProperty property = InferenceFixtureModel.GetProperty("VisitCount");
        GenericNumberInferenceRule rule = new();

        Assert.True(rule.CanInfer(property));
        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;

        int value = Assert.IsType<int>(first);
        Assert.True(value is >= 0 and < 1_000);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericNumber_RespectsDecimalScaleWhenNoNameMatches()
    {
        IProperty property = InferenceFixtureModel.GetProperty("EqualScalePrice");
        GenericNumberInferenceRule rule = new();

        object value = rule.Infer(property, SeededRandom.FromRootSeed(7), new Dictionary<string, object>())!;

        decimal decimalValue = Assert.IsType<decimal>(value);
        Assert.True(decimalValue.Scale <= 2);
    }

    [Fact]
    public void GenericNumber_DoesNotClaimTheDatabaseGeneratedPrimaryKey()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Id");
        Assert.False(new GenericNumberInferenceRule().CanInfer(property));
    }

    [Fact]
    public void GenericBoolean_CanInferAndIsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("IsActive");
        GenericBooleanInferenceRule rule = new();

        Assert.True(rule.CanInfer(property));
        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;

        Assert.IsType<bool>(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericBoolean_ProducesBothValuesAcrossSeeds()
    {
        GenericBooleanInferenceRule rule = new();
        IProperty property = InferenceFixtureModel.GetProperty("IsActive");

        HashSet<bool> seen = [];
        for (int seed = 0; seed < 20; seed++)
        {
            seen.Add((bool)rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>())!);
        }

        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void GenericEnum_CanInferAndOnlyProducesDeclaredValues()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Status");
        GenericEnumInferenceRule rule = new();

        Assert.True(rule.CanInfer(property));

        for (int seed = 0; seed < 30; seed++)
        {
            object value = rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>())!;
            PersonStatus status = Assert.IsType<PersonStatus>(value);
            Assert.True(Enum.IsDefined(status));
        }
    }

    [Fact]
    public void GenericEnum_IsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Status");
        GenericEnumInferenceRule rule = new();

        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;

        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericGuid_CanInferAndIsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("ExternalId");
        GenericGuidInferenceRule rule = new();

        Assert.True(rule.CanInfer(property));
        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;

        Guid guid = Assert.IsType<Guid>(first);
        Assert.NotEqual(Guid.Empty, guid);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericGuid_DifferentSeedsProduceDifferentValues()
    {
        IProperty property = InferenceFixtureModel.GetProperty("ExternalId");
        GenericGuidInferenceRule rule = new();

        Guid first = (Guid)rule.Infer(property, SeededRandom.FromRootSeed(1), new Dictionary<string, object>())!;
        Guid second = (Guid)rule.Infer(property, SeededRandom.FromRootSeed(2), new Dictionary<string, object>())!;

        Assert.NotEqual(first, second);
    }

    private static readonly DateTime ReferenceNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GenericDateTime_CanInferAndIsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("ExpiresAt");
        GenericDateTimeInferenceRule rule = new(ReferenceNow);

        Assert.True(rule.CanInfer(property));
        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;

        DateTime value = Assert.IsType<DateTime>(first);
        Assert.True(value <= ReferenceNow);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericDateTime_DoesNotClaimAPropertyANameSpecificRuleAlreadyOwns()
    {
        IProperty property = InferenceFixtureModel.GetProperty("CreatedAt");
        GenericDateTimeInferenceRule rule = new(ReferenceNow);

        Assert.True(rule.CanInfer(property));
        Assert.True(new CreatedAtInferenceRule(ReferenceNow).CanInfer(property));
    }

    [Fact]
    public void GenericDateTime_DoesNotClaimAConcurrencyToken()
    {
        IProperty property = InferenceFixtureModel.GetProperty("DatabaseComputedTimestamp");
        Assert.False(new GenericDateTimeInferenceRule(ReferenceNow).CanInfer(property));
    }

    [Fact]
    public void GenericDateOnly_CanInferAndIsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("BirthDate");
        GenericDateOnlyInferenceRule rule = new(ReferenceNow);

        Assert.True(rule.CanInfer(property));
        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;

        DateOnly value = Assert.IsType<DateOnly>(first);
        Assert.True(value <= DateOnly.FromDateTime(ReferenceNow));
        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericDateOnly_DoesNotClaimAConcurrencyToken()
    {
        IProperty property = InferenceFixtureModel.GetProperty("DatabaseComputedDate");
        Assert.False(new GenericDateOnlyInferenceRule(ReferenceNow).CanInfer(property));
    }

    [Fact]
    public void GenericTimeOnly_CanInferAndIsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("PreferredContactTime");
        GenericTimeOnlyInferenceRule rule = new();

        Assert.True(rule.CanInfer(property));
        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;

        Assert.IsType<TimeOnly>(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericTimeOnly_DoesNotClaimAConcurrencyToken()
    {
        IProperty property = InferenceFixtureModel.GetProperty("DatabaseComputedTime");
        Assert.False(new GenericTimeOnlyInferenceRule().CanInfer(property));
    }

    [Fact]
    public void GenericTimeSpan_CanInferAndIsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("SessionDuration");
        GenericTimeSpanInferenceRule rule = new();

        Assert.True(rule.CanInfer(property));
        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;

        TimeSpan value = Assert.IsType<TimeSpan>(first);
        Assert.True(value >= TimeSpan.Zero && value <= TimeSpan.FromHours(24));
        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericTimeSpan_DoesNotClaimAConcurrencyToken()
    {
        IProperty property = InferenceFixtureModel.GetProperty("DatabaseComputedDuration");
        Assert.False(new GenericTimeSpanInferenceRule().CanInfer(property));
    }

    [Fact]
    public void GenericByteArray_CanInferAndIsDeterministic()
    {
        IProperty property = InferenceFixtureModel.GetProperty("Avatar");
        GenericByteArrayInferenceRule rule = new();

        Assert.True(rule.CanInfer(property));
        object first = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;
        object second = rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;

        byte[] value = Assert.IsType<byte[]>(first);
        Assert.Equal(16, value.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GenericByteArray_DoesNotClaimAConcurrencyToken()
    {
        IProperty property = InferenceFixtureModel.GetProperty("ConcurrencyToken");
        Assert.False(new GenericByteArrayInferenceRule().CanInfer(property));
    }

    [Fact]
    public void GenericByteArray_RespectsMaxLength()
    {
        IProperty property = InferenceFixtureModel.GetProperty("AvatarThumbnail");
        GenericByteArrayInferenceRule rule = new();

        byte[] value = (byte[])rule.Infer(property, SeededRandom.FromRootSeed(42), new Dictionary<string, object>())!;

        Assert.Equal(4, value.Length);
    }
}
