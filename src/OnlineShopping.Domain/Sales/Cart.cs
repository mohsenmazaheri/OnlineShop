using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Sales;

/// <summary>
/// A shopping cart: a signed-in customer's (<see cref="CustomerId"/>), or a guest's, found by the hash of the random
/// token in the guest's cookie (<see cref="GuestTokenHash"/>). A guest cart is merged into the account's cart after
/// signing in. Prices are never stored here: the cart always shows the current ones.
/// </summary>
public class Cart : BaseEntity
{
    public string? CustomerId { get; set; }

    /// <summary>SHA-256 (hex) of a guest's cart token; the token itself is only in the guest's HttpOnly cookie.</summary>
    public string? GuestTokenHash { get; set; }

    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}
