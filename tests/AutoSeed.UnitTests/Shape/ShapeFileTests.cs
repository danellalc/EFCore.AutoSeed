using EFCore.AutoSeed.Shape;

namespace EFCore.AutoSeed.UnitTests.Shape;

public sealed class ShapeFileTests
{
    [Fact]
    public async Task WriteAsync_ThenReadAsync_RoundTripsTheCapture()
    {
        ShapeCapture shape = new(Version: 1, Tables:
        [
            new TableShape("MyApp.Customer", RowCount: 50_000),
            new TableShape("MyApp.Order", RowCount: 612_345),
        ]);
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

        try
        {
            await ShapeFile.WriteAsync(shape, path);
            ShapeCapture roundTripped = await ShapeFile.ReadAsync(path);

            Assert.Equal(shape.Version, roundTripped.Version);
            Assert.Equal(shape.Tables, roundTripped.Tables);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_ProducesHumanReadableJsonWithNoDataRowFields()
    {
        ShapeCapture shape = new(Version: 1, Tables: [new TableShape("MyApp.Customer", RowCount: 50_000)]);
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

        try
        {
            await ShapeFile.WriteAsync(shape, path);
            string json = await File.ReadAllTextAsync(path);

            Assert.Contains("\"version\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"rowCount\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("50000", json);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_WithNullPath_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ShapeFile.ReadAsync(null!));
    }

    [Fact]
    public async Task WriteAsync_WithNullArguments_ThrowsArgumentNullException()
    {
        ShapeCapture shape = new(Version: 1, Tables: []);

        await Assert.ThrowsAsync<ArgumentNullException>(() => ShapeFile.WriteAsync(null!, "path.json"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ShapeFile.WriteAsync(shape, null!));
    }
}
