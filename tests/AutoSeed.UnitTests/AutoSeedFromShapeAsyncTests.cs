using EFCore.AutoSeed.Exceptions;
using EFCore.AutoSeed.Shape;
using EFCore.AutoSeed.UnitTests.Pipeline.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace EFCore.AutoSeed.UnitTests;

public sealed class AutoSeedFromShapeAsyncTests
{
    [Fact]
    public async Task AutoSeedFromShapeAsync_ScalesEachRootEntityTypeProportionallyToTheCapturedShape()
    {
        using UnrelatedEntitiesContext context = new();
        ShapeCapture shape = new(Version: 1, Tables:
        [
            new TableShape(typeof(Zebra).FullName!, RowCount: 1_000),
            new TableShape(typeof(Apple).FullName!, RowCount: 100),
        ]);

        IReadOnlyDictionary<string, int> result = await context.AutoSeedFromShapeAsync(seed: 42, shape, scale: 200);

        Assert.Equal(200, result[typeof(Zebra).FullName!]);
        Assert.Equal(20, result[typeof(Apple).FullName!]);
        Assert.Equal(200, result[typeof(Mango).FullName!]);
    }

    [Fact]
    public async Task AutoSeedFromShapeAsync_WithAnEmptyShape_BehavesLikeAutoSeedAsync()
    {
        using UnrelatedEntitiesContext context = new();
        ShapeCapture shape = new(Version: 1, Tables: []);

        IReadOnlyDictionary<string, int> result = await context.AutoSeedFromShapeAsync(seed: 42, shape, scale: 50);

        Assert.Equal(50, result[typeof(Zebra).FullName!]);
        Assert.Equal(50, result[typeof(Apple).FullName!]);
        Assert.Equal(50, result[typeof(Mango).FullName!]);
    }

    [Fact]
    public async Task AutoSeedFromShapeAsync_NeverDerivesAScaleOfZero()
    {
        using UnrelatedEntitiesContext context = new();
        ShapeCapture shape = new(Version: 1, Tables:
        [
            new TableShape(typeof(Zebra).FullName!, RowCount: 1_000_000),
            new TableShape(typeof(Apple).FullName!, RowCount: 1),
        ]);

        IReadOnlyDictionary<string, int> result = await context.AutoSeedFromShapeAsync(seed: 42, shape, scale: 100);

        Assert.True(result[typeof(Apple).FullName!] >= 1);
    }

    [Fact]
    public async Task AutoSeedFromShapeAsync_WithNullArguments_ThrowsArgumentNullException()
    {
        using UnrelatedEntitiesContext context = new();
        ShapeCapture shape = new(Version: 1, Tables: []);

        await Assert.ThrowsAsync<ArgumentNullException>(() => DbContextAutoSeedExtensions.AutoSeedFromShapeAsync(null!, seed: 1, shape, scale: 10));
        await Assert.ThrowsAsync<ArgumentNullException>(() => context.AutoSeedFromShapeAsync(seed: 1, null!, scale: 10));
    }

    [Fact]
    public async Task AutoSeedFromShapeAsync_WithNonPositiveScale_ThrowsArgumentOutOfRangeException()
    {
        using UnrelatedEntitiesContext context = new();
        ShapeCapture shape = new(Version: 1, Tables: []);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => context.AutoSeedFromShapeAsync(seed: 1, shape, scale: 0));
    }

    [Fact]
    public async Task CaptureShapeAsync_WithAnUnsupportedProvider_ThrowsUnsupportedProviderException()
    {
        using UnrelatedEntitiesContext context = new();

        UnsupportedProviderException exception =
            await Assert.ThrowsAsync<UnsupportedProviderException>(() => context.CaptureShapeAsync());

        Assert.Contains("InMemory", exception.ProviderName);
    }
}
