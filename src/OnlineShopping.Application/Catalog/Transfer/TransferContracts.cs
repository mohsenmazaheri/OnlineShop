using System.Globalization;
using System.Text;

namespace OnlineShopping.Application.Catalog.Transfer;

/// <summary>Error codes of bulk import and export (translated by the web app under <c>admin.importErrors</c>).</summary>
public static class TransferErrors
{
    public const string FormatUnknown = "import.format_unknown";
    public const string FileEmpty = "import.file_empty";
    public const string FileTooLarge = "import.file_too_large";
    public const string FileUnreadable = "import.file_unreadable";
    public const string TooManyRows = "import.too_many_rows";
    public const string ColumnMissing = "import.column_missing";
    public const string ColumnUnknown = "import.column_unknown";
    public const string ColumnRepeated = "import.column_repeated";
    public const string SkuRequired = "import.sku_required";
    public const string SkuRepeated = "import.sku_repeated";
    public const string SkuNotFound = "import.sku_not_found";
    public const string NumberUnknown = "import.number_unknown";
    public const string NumberInvalid = "import.number_invalid";
    public const string BooleanInvalid = "import.boolean_invalid";
    public const string GroupInvalid = "import.group_invalid";
    public const string OptionUnknown = "import.option_unknown";
    public const string StockOnVariants = "import.stock_on_variants";
}

/// <summary>One problem in an imported file: the sheet row (the header is row 1), the column, and the error code.</summary>
public sealed record ImportProblem(int Row, string? Column, string Code);

/// <summary>
/// What an import did (or, for a check, would do). Nothing is saved unless the whole file is valid and it was not a
/// check: <see cref="Applied"/> says which.
/// </summary>
public sealed record ImportReport(bool Applied, int Created, int Updated, int Rows, IReadOnlyList<ImportProblem> Problems);

/// <summary>An exported file.</summary>
public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>The columns of the product sheet. Language columns are "name:fa"; spec columns are "spec:color".</summary>
public static class ProductColumns
{
    public const string Number = "number";
    public const string Sku = "sku";
    public const string Published = "published";
    public const string Group = "group";
    public const string Brand = "brand";
    public const string Price = "price";
    public const string CompareAtPrice = "compare_at_price";
    public const string Stock = "stock";
    public const string Gtin = "gtin";
    public const string Mpn = "mpn";
    public const string WeightGrams = "weight_grams";

    /// <summary>Per-language columns, written as "{name}:{locale}".</summary>
    public static readonly string[] LocalizedNames = ["name", "slug", "description", "meta_title", "meta_description", "tags"];

    public const string SpecPrefix = "spec:";

    /// <summary>"Category > Sub-category > Group" (also accepted: "›" and "/").</summary>
    public const string GroupSeparator = " > ";

    public static readonly string[] Fixed = [Number, Sku, Published, Group, Brand, Price, CompareAtPrice, Stock, Gtin, Mpn, WeightGrams];
}

/// <summary>Cells as admins write them in spreadsheets, in any of the shop's languages.</summary>
public static class Cells
{
    /// <summary>
    /// A number with Persian or Arabic digits, "٫" or "." as decimal point, and "," / "٬" / spaces as thousands
    /// separators (a comma followed by exactly three digits); null when empty, <c>false</c> when not a number.
    /// </summary>
    public static (bool Ok, decimal? Value) Number(string cell)
    {
        var text = cell.Trim();
        if (text.Length == 0)
            return (true, null);
        var ascii = new StringBuilder();
        foreach (var c in text)
        {
            var digit = CharUnicodeInfo.GetDecimalDigitValue(c);
            ascii.Append(digit >= 0 ? (char)('0' + digit) : c);
        }
        var normalized = System.Text.RegularExpressions.Regex.Replace(ascii.ToString(), "[,٬](?=\\d{3}(\\D|$))", "")
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace('٫', '.')
            .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? (true, value)
            : (false, null);
    }

    private static readonly string[] Yes = ["true", "1", "yes", "y", "بله", "بلی", "آری", "ja", "نعم", "այո"];
    private static readonly string[] No = ["false", "0", "no", "n", "خیر", "نه", "nein", "لا", "ոչ"];

    /// <summary>Yes/no in English, Persian, German, Arabic or Armenian, or 1/0; null when empty, <c>false</c> when neither.</summary>
    public static (bool Ok, bool? Value) Boolean(string cell)
    {
        var text = cell.Trim().ToLowerInvariant();
        if (text.Length == 0)
            return (true, null);
        if (Yes.Contains(text))
            return (true, true);
        return No.Contains(text) ? (true, false) : (false, null);
    }

    public static string Format(decimal? value) => value?.ToString("0.############", CultureInfo.InvariantCulture) ?? "";

    public static string? Text(string cell) => string.IsNullOrWhiteSpace(cell) ? null : cell.Trim();
}
