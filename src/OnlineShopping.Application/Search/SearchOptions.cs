using System.ComponentModel.DataAnnotations;

namespace OnlineShopping.Application.Search;

/// <summary>Settings of the product search (section "Search").</summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>How often the index worker looks for queued catalog changes.</summary>
    [Range(1, 3600)]
    public int IndexIntervalSeconds { get; set; } = 5;
}
