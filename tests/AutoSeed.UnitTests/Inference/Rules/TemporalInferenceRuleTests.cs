using EFCore.AutoSeed.Distributions;
using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using EFCore.AutoSeed.UnitTests.Inference.Fixtures;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class TemporalInferenceRuleTests
{
    private static readonly DateTime ReferenceNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreatedAt_NeverFallsAfterTheReferencePoint()
    {
        IProperty property = InferenceFixtureModel.GetProperty("CreatedAt");
        CreatedAtInferenceRule rule = new(ReferenceNow);

        for (int seed = 0; seed < 100; seed++)
        {
            DateTime createdAt = (DateTime)rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>())!;
            Assert.True(createdAt <= ReferenceNow, $"seed {seed}: {createdAt} is after the reference point.");
        }
    }

    [Fact]
    public void UpdatedAt_NeverFallsBeforeCreatedAt()
    {
        IProperty createdAtProperty = InferenceFixtureModel.GetProperty("CreatedAt");
        IProperty updatedAtProperty = InferenceFixtureModel.GetProperty("UpdatedAt");
        CreatedAtInferenceRule createdAtRule = new(ReferenceNow);
        UpdatedAtInferenceRule updatedAtRule = new(ReferenceNow);

        for (int seed = 0; seed < 100; seed++)
        {
            SeededRandom rowRandom = SeededRandom.FromRootSeed(seed);
            DateTime createdAt = (DateTime)createdAtRule.Infer(createdAtProperty, rowRandom.Derive("CreatedAt"), new Dictionary<string, object>())!;
            Dictionary<string, object> generated = new() { ["CreatedAt"] = createdAt };
            DateTime updatedAt = (DateTime)updatedAtRule.Infer(updatedAtProperty, rowRandom.Derive("UpdatedAt"), generated)!;

            Assert.True(updatedAt >= createdAt, $"seed {seed}: UpdatedAt {updatedAt} is before CreatedAt {createdAt}.");
            Assert.True(updatedAt <= ReferenceNow, $"seed {seed}: UpdatedAt {updatedAt} is after the reference point.");
        }
    }

    [Fact]
    public void DeletedAt_NeverFallsBeforeUpdatedAt()
    {
        IProperty createdAtProperty = InferenceFixtureModel.GetProperty("CreatedAt");
        IProperty updatedAtProperty = InferenceFixtureModel.GetProperty("UpdatedAt");
        IProperty deletedAtProperty = InferenceFixtureModel.GetProperty("DeletedAt");
        CreatedAtInferenceRule createdAtRule = new(ReferenceNow);
        UpdatedAtInferenceRule updatedAtRule = new(ReferenceNow);
        DeletedAtInferenceRule deletedAtRule = new(ReferenceNow);

        for (int seed = 0; seed < 100; seed++)
        {
            SeededRandom rowRandom = SeededRandom.FromRootSeed(seed);
            DateTime createdAt = (DateTime)createdAtRule.Infer(createdAtProperty, rowRandom.Derive("CreatedAt"), new Dictionary<string, object>())!;
            Dictionary<string, object> afterCreated = new() { ["CreatedAt"] = createdAt };
            DateTime updatedAt = (DateTime)updatedAtRule.Infer(updatedAtProperty, rowRandom.Derive("UpdatedAt"), afterCreated)!;
            Dictionary<string, object> afterUpdated = new() { ["CreatedAt"] = createdAt, ["UpdatedAt"] = updatedAt };
            DateTime deletedAt = (DateTime)deletedAtRule.Infer(deletedAtProperty, rowRandom.Derive("DeletedAt"), afterUpdated)!;

            Assert.True(deletedAt >= updatedAt, $"seed {seed}: DeletedAt {deletedAt} is before UpdatedAt {updatedAt}.");
        }
    }

    [Fact]
    public void CanInfer_MatchesTemporalProperties()
    {
        Assert.True(new CreatedAtInferenceRule(ReferenceNow).CanInfer(InferenceFixtureModel.GetProperty("CreatedAt")));
        Assert.True(new UpdatedAtInferenceRule(ReferenceNow).CanInfer(InferenceFixtureModel.GetProperty("UpdatedAt")));
        Assert.True(new DeletedAtInferenceRule(ReferenceNow).CanInfer(InferenceFixtureModel.GetProperty("DeletedAt")));
    }

    [Fact]
    public void CreatedAt_WithACustomTemporalClusteringOptions_HonorsTheConfiguredWeekdayBias()
    {
        IProperty property = InferenceFixtureModel.GetProperty("CreatedAt");
        TemporalClusteringOptions noWeekdayBias = new(WeekdayProbability: 0, BusinessHourProbability: 0);
        CreatedAtInferenceRule rule = new(ReferenceNow, temporalOptions: noWeekdayBias);

        int weekendCount = 0;
        const int TotalSeeds = 300;
        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            DateTime createdAt = (DateTime)rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>())!;
            if (createdAt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                weekendCount++;
            }
        }

        Assert.True(
            weekendCount > TotalSeeds * 0.2,
            $"expected close to the natural 2/7 weekend rate with weekday bias disabled, got {weekendCount} of {TotalSeeds}.");
    }
}
