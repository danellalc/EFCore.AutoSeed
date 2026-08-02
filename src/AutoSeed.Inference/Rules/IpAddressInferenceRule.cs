using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers an IPv4 address for properties named <c>IpAddress</c> or <c>IpV4Address</c>.
/// </summary>
public sealed class IpAddressInferenceRule : IPropertyInferenceRule
{
    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        property.ClrType == typeof(string) && PropertyNameMatch.EndsWithAny(property, "IpAddress", "IpV4Address");

    /// <inheritdoc />
    public object Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        Bogus.DataSets.Internet internet = new() { Random = new Bogus.Randomizer(random.Seed) };
        return StringLengthHelper.TruncateToMaxLength(internet.Ip(), property);
    }
}
