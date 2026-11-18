using System.Globalization;
using System.Text;

namespace OnlineShopping.Application.Catalog;

/// <summary>
/// URL slugs in any script: "گوشی موبایل نوا X1" → "گوشی-موبایل-نوا-x1", "Kettle 1.7 L" → "kettle-1-7-l". Letters and
/// digits are kept (Persian/Arabic digits become ASCII), everything else becomes a single hyphen, Latin is lower-cased
/// and diacritics (Arabic harakat, Latin accents) are dropped.
/// </summary>
public static class Slugs
{
    public const int MaxLength = 80;

    public static string From(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        var pendingHyphen = false;

        foreach (var original in text.Normalize(NormalizationForm.FormC))
        {
            // Separate marks (Arabic harakat, combining accents) are dropped. Only Latin letters are decomposed
            // ("ü" → "u"): decomposing Arabic script would also strip letters such as "آ" down to "ا".
            if (CharUnicodeInfo.GetUnicodeCategory(original) == UnicodeCategory.NonSpacingMark)
                continue;
            var c = original is >= 'À' and < 'ɐ' ? original.ToString().Normalize(NormalizationForm.FormD)[0] : original;
            var digit = AsciiDigit(c);
            if (digit is not null || char.IsLetter(c))
            {
                if (pendingHyphen && builder.Length > 0)
                    builder.Append('-');
                pendingHyphen = false;
                builder.Append(digit ?? char.ToLowerInvariant(c));
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var slug = builder.ToString();
        return slug.Length <= MaxLength ? slug : slug[..MaxLength].TrimEnd('-');
    }

    private static char? AsciiDigit(char c) => c switch
    {
        >= '0' and <= '9' => c,
        >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
        _ => null,
    };
}
