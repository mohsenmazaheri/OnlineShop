using FluentValidation;
using OnlineShop.Domain.Catalog;

namespace OnlineShop.Application.Catalog.Reviews;

public static class ReviewErrors
{
    public const string NotFound = "review.not_found";
    public const string UnverifiedAccount = "review.unverified_account";
    public const string LinksNotAllowed = "review.links_not_allowed";
    public const string TooMany = "review.too_many";
    public const string RatingInvalid = "review.rating_invalid";
    public const string BodyRequired = "review.body_required";
    public const string BodyTooLong = "review.body_too_long";
    public const string TitleTooLong = "review.title_too_long";
    public const string StatusInvalid = "review.status_invalid";
}

/// <summary>What a customer writes: 1 to 5 stars, an optional title and the text.</summary>
public sealed record ReviewInput(int Rating, string? Title, string Body);

public sealed class ReviewInputValidator : AbstractValidator<ReviewInput>
{
    public const int MinBody = 10;
    public const int MaxBody = 2000;

    public ReviewInputValidator()
    {
        RuleFor(r => r.Rating).InclusiveBetween(1, 5).WithErrorCode(ReviewErrors.RatingInvalid);
        RuleFor(r => r.Title).MaximumLength(120).WithErrorCode(ReviewErrors.TitleTooLong);
        RuleFor(r => r.Body).Must(b => (b ?? "").Trim().Length >= MinBody).WithErrorCode(ReviewErrors.BodyRequired);
        RuleFor(r => r.Body).MaximumLength(MaxBody).WithErrorCode(ReviewErrors.BodyTooLong);
    }
}

/// <summary>An approved review as the storefront shows it (no author details beyond the chosen display name).</summary>
public sealed record ReviewView(Guid Id, string? AuthorName, int Rating, string? Title, string Body, string Locale, bool VerifiedBuyer, DateTime CreatedUtc);

/// <summary>
/// The product's rating: the average of approved reviews (0 without any), their count, and how many gave each number
/// of stars (<paramref name="Stars"/>[0] = five stars … [4] = one star).
/// </summary>
public sealed record RatingSummary(decimal Average, int Count, IReadOnlyList<int> Stars);

/// <summary>A page of a product's approved reviews, newest first.</summary>
public sealed record ProductReviews(RatingSummary Summary, IReadOnlyList<ReviewView> Items, int Page, int PageSize, int Total);

/// <summary>The signed-in customer's own review of a product, in any status.</summary>
public sealed record MyReview(int Rating, string? Title, string Body, ReviewStatus Status, DateTime WrittenUtc);

/// <summary>
/// Whether the visitor may write a review of the product, and theirs if there is one. <paramref name="SignedIn"/> is
/// false for a guest; <paramref name="CanReview"/> needs a confirmed email address or mobile number.
/// </summary>
public sealed record MyReviewState(bool SignedIn, bool CanReview, MyReview? Review);

/// <summary>A review in the moderation queue.</summary>
public sealed record ReviewAdminRow(
    Guid Id,
    long ProductNumber,
    string ProductName,
    string? AuthorName,
    int Rating,
    string? Title,
    string Body,
    string Locale,
    ReviewStatus Status,
    bool VerifiedBuyer,
    DateTime CreatedUtc);

public sealed record ReviewAdminList(IReadOnlyList<ReviewAdminRow> Items, int Page, int PageSize, int Total, int PendingCount);

/// <summary>Approve or reject a review (Pending is not a decision).</summary>
public sealed record ModerateReviewRequest(ReviewStatus Status);
