namespace EFCore.AutoSeed.Inference.Rules;

internal static class BrazilianDocuments
{
    private static readonly int[] CnpjFirstCheckWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] CnpjSecondCheckWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    internal static string Cpf(Bogus.Randomizer randomizer)
    {
        int[] digits = randomizer.Digits(9, 0, 9);
        int firstCheck = CheckDigit(digits, firstWeight: 10);
        int[] withFirstCheck = [.. digits, firstCheck];
        int secondCheck = CheckDigit(withFirstCheck, firstWeight: 11);

        return string.Concat(digits) + firstCheck + secondCheck;
    }

    internal static string Cnpj(Bogus.Randomizer randomizer)
    {
        int[] digits = randomizer.Digits(12, 0, 9);
        int firstCheck = CheckDigit(digits, CnpjFirstCheckWeights);
        int[] withFirstCheck = [.. digits, firstCheck];
        int secondCheck = CheckDigit(withFirstCheck, CnpjSecondCheckWeights);

        return string.Concat(digits) + firstCheck + secondCheck;
    }

    private static int CheckDigit(int[] digits, int firstWeight)
    {
        int sum = 0;
        int weight = firstWeight;
        foreach (int digit in digits)
        {
            sum += digit * weight;
            weight--;
        }

        return ToCheckDigit(sum);
    }

    private static int CheckDigit(int[] digits, int[] weights)
    {
        int sum = 0;
        for (int index = 0; index < digits.Length; index++)
        {
            sum += digits[index] * weights[index];
        }

        return ToCheckDigit(sum);
    }

    private static int ToCheckDigit(int sum)
    {
        int remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
