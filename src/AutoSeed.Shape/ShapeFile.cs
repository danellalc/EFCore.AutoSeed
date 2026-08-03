using System.Text.Json;

namespace EFCore.AutoSeed.Shape;

/// <summary>
/// Reads and writes a <see cref="ShapeCapture"/> as JSON.
/// </summary>
public static class ShapeFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Serializes <paramref name="shape"/> to <paramref name="path"/> as JSON.
    /// </summary>
    /// <param name="shape">The capture to write.</param>
    /// <param name="path">The file path to write to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shape"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    public static async Task WriteAsync(ShapeCapture shape, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(path);

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, shape, Options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deserializes a <see cref="ShapeCapture"/> from <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The file path to read from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The deserialized capture.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The file does not contain a valid <see cref="ShapeCapture"/>.</exception>
    public static async Task<ShapeCapture> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        await using FileStream stream = File.OpenRead(path);
        ShapeCapture? shape = await JsonSerializer.DeserializeAsync<ShapeCapture>(stream, Options, cancellationToken).ConfigureAwait(false);
        return shape ?? throw new InvalidOperationException($"'{path}' does not contain a valid shape capture.");
    }
}
