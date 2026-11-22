namespace OnlineShop.Domain.Customers;

/// <summary>A product a signed-in customer saved for later. Personal data: erased with the account and exported.</summary>
public sealed class WishlistItem
{
    public required string CustomerId { get; set; }

    public Guid ProductId { get; set; }

    public DateTime AddedUtc { get; set; }
}
