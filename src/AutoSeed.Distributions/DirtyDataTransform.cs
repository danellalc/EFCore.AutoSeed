using System.Text;
using EFCore.AutoSeed.Pipeline;

namespace EFCore.AutoSeed.Distributions;

/// <summary>
/// The kinds of "dirty data" noise <see cref="DirtyDataTransform"/> can apply to a string value.
/// </summary>
[Flags]
public enum DirtyDataKind
{
    /// <summary>Apply no noise at all.</summary>
    None = 0,

    /// <summary>Inconsistent casing: all caps, all lowercase, or mixed casing per word.</summary>
    Casing = 1 << 0,

    /// <summary>Stray leading/trailing whitespace, or duplicated/collapsed internal whitespace.</summary>
    Whitespace = 1 << 1,

    /// <summary>Stripped Portuguese diacritics, simulating an encoding-unaware legacy system.</summary>
    Diacritics = 1 << 2,

    /// <summary>All noise kinds: <see cref="Casing"/>, <see cref="Whitespace"/> and <see cref="Diacritics"/>.</summary>
    All = Casing | Whitespace | Diacritics,
}

/// <summary>
/// Turns an already-generated, clean string value into a "dirtied" variant simulating the kind of
/// messy data real production databases accumulate: inconsistent casing, stray or duplicated
/// whitespace, and diacritics stripped by an encoding-unaware legacy system. Each transform leaves
/// the value unchanged the large majority of the time, since real dirty data is messy in parts,
/// not uniformly.
/// </summary>
public static class DirtyDataTransform
{
    /// <summary>
    /// The probability that a given noise kind is applied to a value, when no explicit rate is
    /// supplied. Matches <see cref="NullRateSampler.DefaultRate"/>: most rows are clean, a small
    /// minority are not.
    /// </summary>
    public const double DefaultRate = 0.1;

    private const double UpperCaseThreshold = 1.0 / 3.0;
    private const double LowerCaseThreshold = 2.0 / 3.0;

    private static readonly IReadOnlyDictionary<char, char> DiacriticMap = new Dictionary<char, char>
    {
        ['á'] = 'a', ['à'] = 'a', ['â'] = 'a', ['ã'] = 'a', ['ä'] = 'a',
        ['Á'] = 'A', ['À'] = 'A', ['Â'] = 'A', ['Ã'] = 'A', ['Ä'] = 'A',
        ['é'] = 'e', ['è'] = 'e', ['ê'] = 'e', ['ë'] = 'e',
        ['É'] = 'E', ['È'] = 'E', ['Ê'] = 'E', ['Ë'] = 'E',
        ['í'] = 'i', ['ì'] = 'i', ['î'] = 'i', ['ï'] = 'i',
        ['Í'] = 'I', ['Ì'] = 'I', ['Î'] = 'I', ['Ï'] = 'I',
        ['ó'] = 'o', ['ò'] = 'o', ['ô'] = 'o', ['õ'] = 'o', ['ö'] = 'o',
        ['Ó'] = 'O', ['Ò'] = 'O', ['Ô'] = 'O', ['Õ'] = 'O', ['Ö'] = 'O',
        ['ú'] = 'u', ['ù'] = 'u', ['û'] = 'u', ['ü'] = 'u',
        ['Ú'] = 'U', ['Ù'] = 'U', ['Û'] = 'U', ['Ü'] = 'U',
        ['ç'] = 'c', ['Ç'] = 'C',
    };

    /// <summary>
    /// Applies the requested noise kinds to <paramref name="value"/> in sequence: casing, then
    /// whitespace, then diacritics. Each kind decides independently, from its own derived path,
    /// whether it fires, so enabling or disabling one kind never changes another kind's decision
    /// for the same seed.
    /// </summary>
    /// <param name="value">The clean, already-generated value to dirty.</param>
    /// <param name="random">The random source this transform derives from.</param>
    /// <param name="kinds">The noise kinds to consider. Defaults to <see cref="DirtyDataKind.All"/>.</param>
    /// <returns>The dirtied value, or <paramref name="value"/> unchanged if no requested kind fired.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> or <paramref name="random"/> is <see langword="null"/>.</exception>
    public static string Apply(string value, SeededRandom random, DirtyDataKind kinds = DirtyDataKind.All)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(random);

        string result = value;
        if (kinds.HasFlag(DirtyDataKind.Casing))
        {
            result = ApplyCasingNoise(result, random.Derive(nameof(DirtyDataKind.Casing)));
        }

        if (kinds.HasFlag(DirtyDataKind.Whitespace))
        {
            result = ApplyWhitespaceNoise(result, random.Derive(nameof(DirtyDataKind.Whitespace)));
        }

        if (kinds.HasFlag(DirtyDataKind.Diacritics))
        {
            result = StripDiacritics(result, random.Derive(nameof(DirtyDataKind.Diacritics)));
        }

        return result;
    }

    /// <summary>
    /// Sometimes returns <paramref name="value"/> unchanged, sometimes ALL CAPS, sometimes all
    /// lowercase, and sometimes with mixed casing where each word independently has its case
    /// swapped, mirroring how real user-entered or badly-migrated data often looks.
    /// </summary>
    /// <param name="value">The value to apply casing noise to.</param>
    /// <param name="random">The random source this draw derives from.</param>
    /// <param name="rate">The probability that any casing noise is applied at all, in <c>[0, 1]</c>.</param>
    /// <returns><paramref name="value"/>, or a variant with different casing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> or <paramref name="random"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is outside <c>[0, 1]</c>.</exception>
    public static string ApplyCasingNoise(string value, SeededRandom random, double rate = DefaultRate)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(random);
        ValidateRate(rate);

        if (random.NextDouble() >= rate)
        {
            return value;
        }

        double variant = random.NextDouble();
        return variant switch
        {
            < UpperCaseThreshold => value.ToUpperInvariant(),
            < LowerCaseThreshold => value.ToLowerInvariant(),
            _ => MixCasing(value, random),
        };
    }

    /// <summary>
    /// Sometimes adds leading and/or trailing whitespace, and sometimes duplicates or collapses
    /// internal whitespace, leaving <paramref name="value"/> unchanged the rest of the time.
    /// Mirrors artifacts left by manual data entry, copy-paste and lossy migrations.
    /// </summary>
    /// <param name="value">The value to apply whitespace noise to.</param>
    /// <param name="random">The random source this draw derives from.</param>
    /// <param name="rate">The probability that any whitespace noise is applied at all, in <c>[0, 1]</c>.</param>
    /// <returns><paramref name="value"/>, or a variant with extra, duplicated or removed whitespace.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> or <paramref name="random"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is outside <c>[0, 1]</c>.</exception>
    public static string ApplyWhitespaceNoise(string value, SeededRandom random, double rate = DefaultRate)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(random);
        ValidateRate(rate);

        return random.NextDouble() < rate ? ApplyWhitespaceVariant(value, random) : value;
    }

    /// <summary>
    /// Sometimes strips the common Portuguese diacritics from <paramref name="value"/> (for
    /// example <c>"Jose"</c> for <c>"José"</c>, <c>"Sao"</c> for <c>"São"</c>), simulating data
    /// entered without proper keyboard or encoding support. Values with no recognized diacritic
    /// are always returned unchanged, since there is nothing to strip.
    /// </summary>
    /// <param name="value">The value to strip diacritics from.</param>
    /// <param name="random">The random source this draw derives from.</param>
    /// <param name="rate">The probability that diacritics are stripped, in <c>[0, 1]</c>.</param>
    /// <returns><paramref name="value"/>, or a variant with its diacritics replaced by plain ASCII letters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> or <paramref name="random"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is outside <c>[0, 1]</c>.</exception>
    public static string StripDiacritics(string value, SeededRandom random, double rate = DefaultRate)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(random);
        ValidateRate(rate);

        return random.NextDouble() < rate ? RemoveDiacritics(value) : value;
    }

    private static void ValidateRate(double rate)
    {
        if (rate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "Must be between 0 and 1.");
        }
    }

    private static string MixCasing(string value, SeededRandom random)
    {
        string[] words = value.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0 && random.NextBoolean())
            {
                words[i] = SwapCase(words[i]);
            }
        }

        return string.Join(' ', words);
    }

    private static string SwapCase(string word)
    {
        char[] chars = word.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = char.IsUpper(chars[i]) ? char.ToLowerInvariant(chars[i])
                : char.IsLower(chars[i]) ? char.ToUpperInvariant(chars[i])
                : chars[i];
        }

        return new string(chars);
    }

    private static string ApplyWhitespaceVariant(string value, SeededRandom random) =>
        random.NextBoolean() ? PadEdges(value, random) : ApplyInternalWhitespaceNoise(value, random);

    private static string ApplyInternalWhitespaceNoise(string value, SeededRandom random)
    {
        if (!value.Contains(' '))
        {
            return PadEdges(value, random);
        }

        return random.NextBoolean() ? CollapseOneInternalSpace(value, random) : DuplicateInternalSpaces(value, random);
    }

    private static string PadEdges(string value, SeededRandom random)
    {
        bool leading = random.NextBoolean();
        bool trailing = random.NextBoolean();
        if (!leading && !trailing)
        {
            leading = true;
        }

        string result = leading ? new string(' ', random.Next(1, 4)) + value : value;
        return trailing ? result + new string(' ', random.Next(1, 4)) : result;
    }

    private static string CollapseOneInternalSpace(string value, SeededRandom random)
    {
        List<int> spaceIndexes = new();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == ' ')
            {
                spaceIndexes.Add(i);
            }
        }

        int indexToRemove = spaceIndexes[random.Next(0, spaceIndexes.Count)];
        return value.Remove(indexToRemove, 1);
    }

    private static string DuplicateInternalSpaces(string value, SeededRandom random)
    {
        StringBuilder builder = new(value.Length + 4);
        foreach (char c in value)
        {
            builder.Append(c);
            if (c == ' ')
            {
                builder.Append(' ', random.Next(1, 3));
            }
        }

        return builder.ToString();
    }

    private static string RemoveDiacritics(string value)
    {
        StringBuilder? builder = null;
        for (int i = 0; i < value.Length; i++)
        {
            if (DiacriticMap.TryGetValue(value[i], out char replacement))
            {
                builder ??= new StringBuilder(value, 0, i, value.Length);
                builder.Append(replacement);
            }
            else
            {
                builder?.Append(value[i]);
            }
        }

        return builder?.ToString() ?? value;
    }
}
