using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

internal static class PropertyNameMatch
{
    internal static bool EndsWithAny(IProperty property, params ReadOnlySpan<string> suffixes)
    {
        foreach (string suffix in suffixes)
        {
            if (property.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsClrType<T>(IProperty property)
        where T : struct =>
        property.ClrType == typeof(T) || property.ClrType == typeof(T?);
}
