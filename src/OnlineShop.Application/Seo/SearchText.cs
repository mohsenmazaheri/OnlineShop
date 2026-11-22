using System.Globalization;
using System.Text;

namespace OnlineShop.Application.Seo;

/// <summary>
/// One spelling for search words, applied to what is stored and to what visitors type (docs/ROADMAP.md, phase 6):
/// Arabic "ي/ك" become Persian "ی/ک", Persian and Arabic digits become 0-9, the zero-width non-joiner (نیم‌فاصله)
/// becomes a space, Arabic diacritics are dropped, Latin letters are lower-cased and spaces collapsed.
/// </summary>
public static class SearchText
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var result = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            var mapped = c switch
            {
                'ي' or 'ى' => 'ی',
                'ك' => 'ک',
                '‌' or ' ' or '\t' or '\n' or '\r' => ' ',
                _ when CharUnicodeInfo.GetDecimalDigitValue(c) is >= 0 and var d && c > 127 => (char)('0' + d),
                _ => char.ToLowerInvariant(c),
            };
            // Arabic diacritics (fatha, kasra, shadda, …) and the tatweel do not change a word.
            if (mapped is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ')
                continue;
            if (mapped == ' ')
            {
                space = result.Length > 0;
                continue;
            }
            if (space)
            {
                result.Append(' ');
                space = false;
            }
            result.Append(mapped);
        }
        return result.ToString();
    }
}
