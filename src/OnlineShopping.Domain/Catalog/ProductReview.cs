using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Catalog;

/// <summary>Where a review is in moderation: only approved reviews are shown (and counted in the rating).</summary>
public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>
/// A customer's review of a product: one per customer and product (editing sends it back to moderation). Personal
/// data: erased with the account. <see cref="VerifiedBuyer"/> is decided when written, from the customer's paid orders.
/// </summary>
public sealed class ProductReview : BaseEntity
{
    public Guid ProductId { get; set; }

    public required string CustomerId { get; set; }

    /// <summary>The name shown with the review (the customer's display name when written; empty: "a customer").</summary>
    public string? AuthorName { get; set; }

    /// <summary>1 to 5 stars.</summary>
    public int Rating { get; set; }

    public string? Title { get; set; }

    public required string Body { get; set; }

    /// <summary>The storefront language it was written in.</summary>
    public required string Locale { get; set; }

    public ReviewStatus Status { get; set; }

    public bool VerifiedBuyer { get; set; }

    public DateTime? ModeratedUtc { get; set; }

    public string? ModeratedBy { get; set; }
}
