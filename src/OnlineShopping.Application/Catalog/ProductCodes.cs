using System.Globalization;
using System.Text;

namespace OnlineShopping.Application.Catalog;

/// <summary>Barcodes and search words as admins type them: Persian/Arabic digits, spaces and dashes allowed.</summary>
public static class ProductCodes
{
    /// <summary>
    /// A GTIN (EAN-8, UPC-A, EAN-13 or GTIN-14) as digits only, or null when it is not one (wrong length or check
    /// digit). Persian and Arabic digits, spaces and dashes are accepted.
    /// </summary>
    public static string? NormalizeGtin(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var digits = new StringBuilder();
        foreach (var c in input)
        {
            if (c is ' ' or '-')
                continue;
            var value = CharUnicodeInfo.GetDecimalDigitValue(c);
            if (value < 0)
                return null;
            digits.Append((char)('0' + value));
        }
        var gtin = digits.ToString();
        return gtin.Length is 8 or 12 or 13 or 14 && CheckDigitValid(gtin) ? gtin : null;
    }

    /// <summary>GS1 check digit: weights 3 and 1 from the right, excluding the check digit itself.</summary>
    private static bool CheckDigitValid(string gtin)
    {
        var sum = 0;
        for (var i = gtin.Length - 2; i >= 0; i--)
            sum += (gtin[i] - '0') * ((gtin.Length - 2 - i) % 2 == 0 ? 3 : 1);
        return (10 - sum % 10) % 10 == gtin[^1] - '0';
    }

    /// <summary>
    /// Comma-separated words, trimmed, single-spaced, each once (case-insensitive), in input order; Persian "،" counts
    /// as a comma too. Null when nothing is left.
    /// </summary>
    public static string? NormalizeTags(string? input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var words = input.Split([',', '،', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => string.Join(' ', w.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var joined = string.Join(", ", words);
        return joined.Length == 0 ? null : joined.Length > maxLength ? joined[..maxLength].TrimEnd(',', ' ') : joined;
    }
}
