using System.Text.Json;

namespace EFCore.AutoSeed;

/// <summary>
/// Reads and writes an <see cref="AutoSeedPlanSnapshot"/> as JSON.
/// </summary>
public static class AutoSeedPlanFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Serializes <paramref name="snapshot"/> to <paramref name="path"/> as JSON.
    /// </summary>
    /// <param name="snapshot">The snapshot to write.</param>
    /// <param name="path">The file path to write to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    public static async Task WriteAsync(AutoSeedPlanSnapshot snapshot, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(path);

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, snapshot, Options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deserializes an <see cref="AutoSeedPlanSnapshot"/> from <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The file path to read from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The deserialized snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The file does not contain a valid <see cref="AutoSeedPlanSnapshot"/>.</exception>
    public static async Task<AutoSeedPlanSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        await using FileStream stream = File.OpenRead(path);
        AutoSeedPlanSnapshot? snapshot = await JsonSerializer.DeserializeAsync<AutoSeedPlanSnapshot>(stream, Options, cancellationToken).ConfigureAwait(false);
        return snapshot ?? throw new InvalidOperationException($"'{path}' does not contain a valid plan snapshot.");
    }
}
