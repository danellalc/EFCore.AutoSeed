using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Shape;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests.Shape;

public sealed class ShapeCaptureProviderFactoryTests
{
    [Fact]
    public void Create_WithAProviderThatHasNoShapeCaptureImplementation_ThrowsUnsupportedProviderException()
    {
        using InMemoryOnlyContext context = new();

        UnsupportedProviderException exception = Assert.Throws<UnsupportedProviderException>(() => ShapeCaptureProviderFactory.Create(context));

        Assert.Contains("InMemory", exception.ProviderName);
    }

    [Fact]
    public void Create_WithNullContext_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ShapeCaptureProviderFactory.Create(null!));
    }

    private sealed class InMemoryOnlyContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(nameof(InMemoryOnlyContext));
    }
}
