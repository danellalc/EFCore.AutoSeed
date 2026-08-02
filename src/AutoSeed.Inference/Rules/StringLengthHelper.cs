using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EFCore.AutoSeed.Inference.Rules;

internal static class StringLengthHelper
{
    internal static string TruncateToMaxLength(string value, IProperty property)
    {
        int? maxLength = property.GetMaxLength();
        return maxLength is > 0 && value.Length > maxLength ? value[..maxLength.Value] : value;
    }
}
