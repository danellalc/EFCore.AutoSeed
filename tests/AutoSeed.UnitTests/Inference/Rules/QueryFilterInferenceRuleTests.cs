using EFCore.AutoSeed.Inference.Rules;
using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.UnitTests.Inference.Rules;

public sealed class QueryFilterInferenceRuleTests
{
    [Fact]
    public void CanInfer_RecognizesANegatedBooleanFilter()
    {
        IProperty property = GetProperty<SoftDeleteContext, SoftDeleteEntity>(nameof(SoftDeleteEntity.IsDeleted));

        Assert.True(new QueryFilterInferenceRule().CanInfer(property));
    }

    [Fact]
    public void Infer_MostlyReturnsTheValueThatPassesANegatedBooleanFilter()
    {
        IProperty property = GetProperty<SoftDeleteContext, SoftDeleteEntity>(nameof(SoftDeleteEntity.IsDeleted));
        QueryFilterInferenceRule rule = new();

        int passing = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            object? value = rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>());
            if (Equals(value, false))
            {
                passing++;
            }
        }

        Assert.True(passing > 150, $"expected most of 200 draws to be 'false' (passes the filter), got {passing}.");
        Assert.True(passing < 200, "expected at least one draw to fail the filter.");
    }

    [Fact]
    public void CanInfer_RecognizesADirectBooleanFilter()
    {
        IProperty property = GetProperty<ActiveOnlyContext, ActiveOnlyEntity>(nameof(ActiveOnlyEntity.IsActive));

        Assert.True(new QueryFilterInferenceRule().CanInfer(property));
    }

    [Fact]
    public void Infer_MostlyReturnsTrueForADirectBooleanFilter()
    {
        IProperty property = GetProperty<ActiveOnlyContext, ActiveOnlyEntity>(nameof(ActiveOnlyEntity.IsActive));
        QueryFilterInferenceRule rule = new();

        int passing = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            if (Equals(rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>()), true))
            {
                passing++;
            }
        }

        Assert.True(passing > 150, $"expected most of 200 draws to be 'true', got {passing}.");
    }

    [Fact]
    public void CanInfer_RecognizesANullEqualityFilter()
    {
        IProperty property = GetProperty<NullFilterContext, NullFilterEntity>(nameof(NullFilterEntity.DeletedAt));

        Assert.True(new QueryFilterInferenceRule().CanInfer(property));
    }

    [Fact]
    public void Infer_MostlyReturnsNullForANullEqualityFilter()
    {
        IProperty property = GetProperty<NullFilterContext, NullFilterEntity>(nameof(NullFilterEntity.DeletedAt));
        QueryFilterInferenceRule rule = new();

        int nullCount = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            if (rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>()) is null)
            {
                nullCount++;
            }
        }

        Assert.True(nullCount > 150, $"expected most of 200 draws to be null, got {nullCount}.");
        Assert.True(nullCount < 200, "expected at least one draw to be non-null.");
    }

    [Fact]
    public void Infer_WithACustomPassRate_BiasesTowardTheConfiguredProbability()
    {
        IProperty property = GetProperty<ActiveOnlyContext, ActiveOnlyEntity>(nameof(ActiveOnlyEntity.IsActive));
        QueryFilterInferenceRule rule = new(passesFilterProbability: 0.5);

        int passing = 0;
        const int TotalSeeds = 400;
        for (int seed = 0; seed < TotalSeeds; seed++)
        {
            if (Equals(rule.Infer(property, SeededRandom.FromRootSeed(seed), new Dictionary<string, object>()), true))
            {
                passing++;
            }
        }

        Assert.InRange(passing, TotalSeeds * 0.4, TotalSeeds * 0.6);
    }

    [Fact]
    public void CanInfer_IgnoresAnEntityTypeWithNoQueryFilter()
    {
        IProperty property = GetProperty<NoFilterContext, NoFilterEntity>(nameof(NoFilterEntity.IsDeleted));

        Assert.False(new QueryFilterInferenceRule().CanInfer(property));
    }

    [Fact]
    public void CanInfer_IgnoresAMultiPropertyFilter()
    {
        IProperty property = GetProperty<CompoundFilterContext, CompoundFilterEntity>(nameof(CompoundFilterEntity.IsDeleted));

        Assert.False(new QueryFilterInferenceRule().CanInfer(property));
    }

    private static IProperty GetProperty<TContext, TEntity>(string propertyName)
        where TContext : DbContext, new()
    {
        using TContext context = new();
        IEntityType entityType = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} entity type not found.");
        return entityType.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {typeof(TEntity).Name}.");
    }

    private sealed class SoftDeleteContext : DbContext
    {
        public DbSet<SoftDeleteEntity> Entities => Set<SoftDeleteEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<SoftDeleteEntity>().HasQueryFilter(entity => !entity.IsDeleted);

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(SoftDeleteContext));
    }

    private sealed class SoftDeleteEntity
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    private sealed class ActiveOnlyContext : DbContext
    {
        public DbSet<ActiveOnlyEntity> Entities => Set<ActiveOnlyEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<ActiveOnlyEntity>().HasQueryFilter(entity => entity.IsActive);

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(ActiveOnlyContext));
    }

    private sealed class ActiveOnlyEntity
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class NullFilterContext : DbContext
    {
        public DbSet<NullFilterEntity> Entities => Set<NullFilterEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<NullFilterEntity>().HasQueryFilter(entity => entity.DeletedAt == null);

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(NullFilterContext));
    }

    private sealed class NullFilterEntity
    {
        public int Id { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    private sealed class NoFilterContext : DbContext
    {
        public DbSet<NoFilterEntity> Entities => Set<NoFilterEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(NoFilterContext));
    }

    private sealed class NoFilterEntity
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    private sealed class CompoundFilterContext : DbContext
    {
        public DbSet<CompoundFilterEntity> Entities => Set<CompoundFilterEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<CompoundFilterEntity>().HasQueryFilter(entity => !entity.IsDeleted && entity.TenantId == 1);

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(CompoundFilterContext));
    }

    private sealed class CompoundFilterEntity
    {
        public int Id { get; set; }
        public bool IsDeleted { get; set; }
        public int TenantId { get; set; }
    }
}
