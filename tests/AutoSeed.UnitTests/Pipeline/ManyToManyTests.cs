using EFCore.AutoSeed.Inference;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests.Pipeline;

public sealed class ManyToManyTests
{
    [Fact]
    public async Task AutoSeedAsync_WithAnImplicitManyToMany_SeedsBothSidesAndLeavesTheJoinTableEmpty()
    {
        using ManyToManyContext context = new();

        await context.AutoSeedAsync(seed: 42, scale: 10);

        List<M2MPost> posts = await context.Posts.ToListAsync();
        List<M2MTag> tags = await context.Tags.ToListAsync();

        Assert.NotEmpty(posts);
        Assert.NotEmpty(tags);
    }

    [Fact]
    public async Task AutoSeedExplainAsync_ReportsTheJoinTableAsSkipped()
    {
        using ManyToManyContext context = new();

        AutoSeedExplainResult plan = await context.AutoSeedExplainAsync(seed: 42, scale: 10);

        SkippedEntityType skipped = Assert.Single(plan.SkippedEntityTypes, entry => entry.EntityTypeName.Contains("PostM2MTag"));
        Assert.Contains("many-to-many", skipped.Reason);
    }
}

public sealed class ManyToManyContext : DbContext
{
    public DbSet<M2MPost> Posts => Set<M2MPost>();
    public DbSet<M2MTag> Tags => Set<M2MTag>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(nameof(ManyToManyContext));
}

public sealed class M2MPost
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public List<M2MTag> Tags { get; set; } = [];
}

public sealed class M2MTag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<M2MPost> Posts { get; set; } = [];
}
