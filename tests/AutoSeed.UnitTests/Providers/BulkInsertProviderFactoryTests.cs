using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Providers;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests.Providers;

public sealed class BulkInsertProviderFactoryTests
{
    [Fact]
    public void Create_WithAProviderThatHasNoBulkInsertImplementation_ThrowsUnsupportedProviderException()
    {
        using InMemoryOnlyContext context = new();

        UnsupportedProviderException exception = Assert.Throws<UnsupportedProviderException>(() => BulkInsertProviderFactory.Create(context));

        Assert.Contains("InMemory", exception.ProviderName);
    }

    [Fact]
    public void Create_WithNullContext_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => BulkInsertProviderFactory.Create(null!));
    }

    private sealed class InMemoryOnlyContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(InMemoryOnlyContext));
    }
}
