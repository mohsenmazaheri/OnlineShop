namespace OnlineShopping.Domain.Customers;

/// <summary>
/// A product on a signed-in customer's comparison list (guests keep theirs in the browser). At most four, in the order
/// they were added. Personal data: erased with the account and included in its export.
/// </summary>
public sealed class CompareItem
{
    public required string CustomerId { get; set; }

    public Guid ProductId { get; set; }

    public int SortOrder { get; set; }

    public DateTime AddedUtc { get; set; }
}
