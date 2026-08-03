using EFCore.AutoSeed.Distributions;

namespace EFCore.AutoSeed.Inference;

/// <summary>
/// Tunes the value-generation stage's built-in inference rules without replacing them. Every
/// property has a default that reproduces the exact behavior of not passing an
/// <see cref="AutoSeedOptions"/> at all.
/// </summary>
/// <param name="QueryFilterPassRate">
/// The probability a property governed by its entity type's global query filter is generated with
/// the value that passes the filter, in <c>[0, 1]</c>. Defaults to 0.9.
/// </param>
/// <param name="NullRate">
/// The fraction of eligible nullable columns whose generated value is discarded, in <c>[0, 1]</c>.
/// Defaults to <see cref="NullRateSampler.DefaultRate"/>.
/// </param>
/// <param name="TemporalClustering">
/// How strongly inferred <c>CreatedAt</c>/<c>UpdatedAt</c>/<c>DeletedAt</c> timestamps cluster
/// toward weekdays and business hours, or <see langword="null"/> for
/// <see cref="TemporalClusteringOptions.Default"/>.
/// </param>
/// <param name="Locale">
/// The Bogus locale string used by name-, address- and phone-related inference rules. Defaults to
/// <c>"en"</c>.
/// </param>
/// <param name="DirtyData">
/// The kinds of casing, whitespace and diacritic noise applied to free-text values (names, generic
/// text) after generation, simulating messy production data. Defaults to
/// <see cref="DirtyDataKind.None"/>: no noise, matching the behavior of not passing an
/// <see cref="AutoSeedOptions"/> at all. Never applied to a value with a fixed format, checksum or
/// cross-property coherence to preserve, such as an email address, a URL or a document number.
/// </param>
public sealed record AutoSeedOptions(
    double QueryFilterPassRate = 0.9,
    double NullRate = NullRateSampler.DefaultRate,
    TemporalClusteringOptions? TemporalClustering = null,
    string Locale = "en",
    DirtyDataKind DirtyData = DirtyDataKind.None)
{
    /// <summary>
    /// The options every public seeding method uses when the caller does not supply its own.
    /// </summary>
    public static readonly AutoSeedOptions Default = new();
}
