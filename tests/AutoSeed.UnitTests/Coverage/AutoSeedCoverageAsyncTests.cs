using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests.Coverage;

public sealed class AutoSeedCoverageAsyncTests
{
    private static int _databaseCounter;

    [Fact]
    public async Task AutoSeedCoverageAsync_WritesFewerThanFiftyRowsTotal()
    {
        using CoverageStoreContext context = NewContext();

        IReadOnlyDictionary<string, int> result = await context.AutoSeedCoverageAsync();

        Assert.True(result.Values.Sum() < 50, $"expected fewer than 50 rows total, got {result.Values.Sum()}.");
    }

    [Fact]
    public async Task AutoSeedCoverageAsync_TouchesEveryDeclaredEnumValue()
    {
        using CoverageStoreContext context = NewContext();

        await context.AutoSeedCoverageAsync();

        HashSet<ProductStatus> statuses = [.. await context.Products.Select(product => product.Status).ToListAsync()];
        Assert.Equal(Enum.GetValues<ProductStatus>().Length, statuses.Count);
    }

    [Fact]
    public async Task AutoSeedCoverageAsync_LeavesANullablePropertyNullOnSomeRowsAndSetOnOthers()
    {
        using CoverageStoreContext context = NewContext();

        await context.AutoSeedCoverageAsync();

        List<string?> descriptions = await context.Products.Select(product => product.Description).ToListAsync();
        Assert.Contains(descriptions, description => description is null);
        Assert.Contains(descriptions, description => description is not null);
    }

    [Fact]
    public async Task AutoSeedCoverageAsync_CoversEveryRelationshipCardinality()
    {
        using CoverageStoreContext context = NewContext();

        await context.AutoSeedCoverageAsync();

        List<Product> products = await context.Products.Include(product => product.Reviews).ToListAsync();
        Assert.Contains(products, product => product.Reviews.Count == 0);
        Assert.Contains(products, product => product.Reviews.Count == 1);
        Assert.Contains(products, product => product.Reviews.Count > 1);
    }

    [Fact]
    public async Task AutoSeedCoverageAsync_ProducesNoReferentialIntegrityViolations()
    {
        using CoverageStoreContext context = NewContext();

        await context.AutoSeedCoverageAsync();

        HashSet<int> productIds = [.. await context.Products.Select(product => product.Id).ToListAsync()];
        List<int> reviewProductIds = await context.Reviews.Select(review => review.ProductId).ToListAsync();
        Assert.All(reviewProductIds, productId => Assert.Contains(productId, productIds));
    }

    [Fact]
    public async Task AutoSeedCoverageAsync_WithTheSameModel_IsDeterministic()
    {
        IReadOnlyDictionary<string, int> firstResult = await NewContext().AutoSeedCoverageAsync();
        IReadOnlyDictionary<string, int> secondResult = await NewContext().AutoSeedCoverageAsync();

        Assert.Equal(firstResult, secondResult);
    }

    [Fact]
    public async Task AutoSeedCoverageAsync_WithNullContext_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => DbContextAutoSeedExtensions.AutoSeedCoverageAsync(null!));
    }

    private static CoverageStoreContext NewContext()
    {
        int id = Interlocked.Increment(ref _databaseCounter);
        return new CoverageStoreContext($"{nameof(CoverageStoreContext)}_{id}");
    }
}

public sealed class CoverageStoreContext(string databaseName) : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase(databaseName);
}

public enum ProductStatus
{
    Draft,
    Published,
    Discontinued,
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsFeatured { get; set; }
    public ProductStatus Status { get; set; }
    public List<Review> Reviews { get; set; } = [];
}

public sealed class Review
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Comment { get; set; } = "";
}
