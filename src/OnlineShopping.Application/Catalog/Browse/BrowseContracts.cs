using System.Globalization;

namespace OnlineShopping.Application.Catalog.Browse;

public enum BrowseSort
{
    /// <summary>Best match first (text searches; otherwise the same as newest).</summary>
    Relevance,
    Newest,
    PriceAsc,
    PriceDesc,
    /// <summary>The largest discount (share of the old price) first.</summary>
    Discount
}

/// <summary>A closed or open number range: "5-7", "5-", "-7".</summary>
public sealed record NumberRange(decimal? Min, decimal? Max)
{
    public bool Contains(decimal value) => (Min is null || value >= Min) && (Max is null || value <= Max);
}

/// <summary>
/// What to list and how. The scope is a category (and sub-category), a brand, or a text search (or everything);
/// filters narrow it. Brands are slugs, groups ids, specs codes and options codes, as in the address bar.
/// </summary>
public sealed record BrowseQuery
{
    public string? Category { get; init; }
    public string? SubCategory { get; init; }
    public string? Brand { get; init; }
    public string? Text { get; init; }

    public IReadOnlyList<string> Brands { get; init; } = [];
    public IReadOnlyList<Guid> Groups { get; init; } = [];
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool InStock { get; init; }
    public bool Discounted { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Options { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyDictionary<string, NumberRange> Ranges { get; init; } = new Dictionary<string, NumberRange>();
    public IReadOnlyList<string> Flags { get; init; } = [];

    public BrowseSort? Sort { get; init; }
    public int Page { get; init; } = 1;

    /// <summary>Whether any filter (not the scope, sort or page) is set.</summary>
    public bool HasFilters =>
        Brands.Count > 0 || Groups.Count > 0 || MinPrice is not null || MaxPrice is not null || InStock || Discounted
        || Options.Count > 0 || Ranges.Count > 0 || Flags.Count > 0;
}

/// <summary>
/// Reads a browse query from the address bar's parameters (the web app writes the same ones):
/// c, s (category and sub-category slug), b (brand page), q (text); brand=a,b; group=id,id; min, max (base currency);
/// stock=1; sale=1; o.{spec}=option,option; n.{spec}=min-max; y.{spec}=1; sort=newest|price_asc|price_desc|discount;
/// page. Anything unknown or malformed is ignored, so an edited address never fails.
/// </summary>
public static class BrowseQueryParser
{
    public const int MaxValues = 30;

    public static BrowseQuery Parse(IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var values = parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .GroupBy(p => p.Key, StringComparer.Ordinal)
            // A key given twice (a form's checkboxes) counts as one comma list.
            .ToDictionary(g => g.Key, g => string.Join(',', g.Select(p => p.Value!.Trim())), StringComparer.Ordinal);
        string? Get(string key) => values.TryGetValue(key, out var v) ? v : null;

        var options = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var ranges = new Dictionary<string, NumberRange>(StringComparer.Ordinal);
        var flags = new List<string>();
        foreach (var (key, value) in values)
        {
            if (key.StartsWith("o.", StringComparison.Ordinal) && key.Length > 2)
                options[key[2..]] = List(value);
            else if (key.StartsWith("n.", StringComparison.Ordinal) && key.Length > 2 && Range(value) is { } range)
                ranges[key[2..]] = range;
            else if (key.StartsWith("y.", StringComparison.Ordinal) && key.Length > 2 && value == "1")
                flags.Add(key[2..]);
        }

        return new BrowseQuery
        {
            Category = Get("c"),
            SubCategory = Get("s"),
            Brand = Get("b"),
            Text = Get("q") is { } q ? q[..Math.Min(q.Length, 200)] : null,
            Brands = List(Get("brand")),
            Groups = List(Get("group")).Select(g => Guid.TryParse(g, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList(),
            MinPrice = Decimal(Get("min")),
            MaxPrice = Decimal(Get("max")),
            InStock = Get("stock") == "1",
            Discounted = Get("sale") == "1",
            Options = options,
            Ranges = ranges,
            Flags = flags,
            Sort = Get("sort") switch
            {
                "newest" => BrowseSort.Newest,
                "price_asc" => BrowseSort.PriceAsc,
                "price_desc" => BrowseSort.PriceDesc,
                "discount" => BrowseSort.Discount,
                "relevance" => BrowseSort.Relevance,
                _ => null,
            },
            Page = int.TryParse(Get("page"), NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page is > 0 and <= 10_000 ? page : 1,
        };
    }

    private static List<string> List(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).Take(MaxValues).ToList();

    private static decimal? Decimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && number >= 0 ? number : null;

    private static NumberRange? Range(string value)
    {
        var dash = value.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
            return null;
        var (min, max) = (Decimal(value[..dash]), Decimal(value[(dash + 1)..]));
        return min is null && max is null ? null : new NumberRange(min, max);
    }
}

/// <summary>A link of the listing's scope ("c/phones/android", without the language).</summary>
public sealed record ScopeLink(string Name, string Path);

/// <summary>The scope's address in another shop language (only languages it is translated into).</summary>
public sealed record ScopeAlternate(string Locale, string Path);

/// <summary>What is being listed: its title and description, breadcrumb, image, and addresses per language.</summary>
public sealed record BrowseScope(
    string Kind,
    string? Title,
    string? Description,
    string? ImageUrl,
    string? Path,
    IReadOnlyList<ScopeLink> Breadcrumb,
    IReadOnlyList<ScopeAlternate> Alternates);

/// <summary>One choice of a facet with how many products it would show (with the other filters applied).</summary>
public sealed record FacetValue(string Value, string Label, int Count, bool Selected);

/// <summary>A sub-category to go to, with how many products it would show.</summary>
public sealed record FacetLink(string Name, string Path, int Count);

/// <summary>
/// A filterable spec: options with counts (Option kind), the range of values (Number), or the count of "yes"
/// (Boolean). Options without products under the other filters have count 0 and are shown disabled.
/// </summary>
public sealed record SpecFacet(
    string Code,
    string Name,
    string? Group,
    string Kind,
    string? Unit,
    IReadOnlyList<FacetValue> Options,
    decimal? Min,
    decimal? Max,
    decimal? SelectedMin,
    decimal? SelectedMax,
    int YesCount,
    bool YesSelected);

public sealed record PriceRange(decimal Min, decimal Max);

public sealed record BrowseFacets(
    IReadOnlyList<FacetLink> SubCategories,
    IReadOnlyList<FacetValue> Groups,
    IReadOnlyList<FacetValue> Brands,
    PriceRange? Price,
    decimal? SelectedMin,
    decimal? SelectedMax,
    int InStock,
    bool InStockSelected,
    int Discounted,
    bool DiscountedSelected,
    IReadOnlyList<SpecFacet> Specs);

public sealed record BrowseResult(
    BrowseScope Scope,
    IReadOnlyList<ProductCard> Items,
    int Total,
    int Page,
    int PageSize,
    BrowseSort Sort,
    bool Filtered,
    BrowseFacets Facets,
    BrowseSearch? Search,
    IReadOnlyList<Relaxation> Relaxations);

/// <summary>Help with a text search: the product it names, the words searched instead, or a suggestion.</summary>
public sealed record BrowseSearch(long? ExactNumber, string? CorrectedText, string? DidYouMean);

/// <summary>On an empty result: how many products there would be without this one filter.</summary>
public sealed record Relaxation(string Filter, int Count);
