using EFCore.AutoSeed.Pipeline;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

/// <summary>
/// Infers a Brazilian CPF or CNPJ, with a valid check digit, for properties named <c>Cpf</c> or <c>Cnpj</c>.
/// </summary>
public sealed class DocumentInferenceRule : IPropertyInferenceRule
{
    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool CanInfer(IProperty property) =>
        property.ClrType == typeof(string) && PropertyNameMatch.EndsWithAny(property, "Cpf", "Cnpj");

    /// <inheritdoc />
    public object Infer(IProperty property, SeededRandom random, IReadOnlyDictionary<string, object> generatedValues)
    {
        Bogus.Randomizer randomizer = new(random.Seed);

        return property.Name.EndsWith("Cnpj", StringComparison.OrdinalIgnoreCase)
            ? BrazilianDocuments.Cnpj(randomizer)
            : BrazilianDocuments.Cpf(randomizer);
    }
}
