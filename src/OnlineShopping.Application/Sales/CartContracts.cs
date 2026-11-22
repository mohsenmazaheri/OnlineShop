using FluentValidation;

namespace OnlineShopping.Application.Sales;

public static class CartErrors
{
    public const string ProductNotFound = "cart.product_not_found";
    public const string VariantRequired = "cart.variant_required";
    public const string NotEnoughStock = "cart.not_enough_stock";
    public const string LineNotFound = "cart.line_not_found";
    public const string QuantityInvalid = "cart.quantity_invalid";
    public const string TooManyLines = "cart.too_many_lines";
}

/// <summary>Whose cart: the signed-in customer's, or the guest's (the token from the guest's cookie). Never both empty.</summary>
public sealed record CartOwner(string? UserId, string? GuestToken)
{
    public bool IsEmpty => UserId is null && GuestToken is null;
}

/// <summary>Add a product (by its public number) or one of its variants (by SKU) to the cart.</summary>
public sealed record AddToCartRequest(long Number, string? VariantSku, int Quantity);

public sealed class AddToCartRequestValidator : AbstractValidator<AddToCartRequest>
{
    public AddToCartRequestValidator()
    {
        RuleFor(r => r.Number).GreaterThan(0).WithErrorCode(CartErrors.ProductNotFound);
        RuleFor(r => r.Quantity).InclusiveBetween(1, CartService.MaxQuantity).WithErrorCode(CartErrors.QuantityInvalid);
    }
}

public sealed record SetQuantityRequest(int Quantity);

public sealed class SetQuantityRequestValidator : AbstractValidator<SetQuantityRequest>
{
    public SetQuantityRequestValidator() =>
        RuleFor(r => r.Quantity).InclusiveBetween(0, CartService.MaxQuantity).WithErrorCode(CartErrors.QuantityInvalid);
}

/// <summary>Why a line cannot be bought as it is (it stays in the cart, out of the total, until changed or removed).</summary>
public enum CartLineProblem
{
    None,
    /// <summary>The product was unpublished or removed.</summary>
    Unavailable,
    /// <summary>Out of stock now.</summary>
    OutOfStock,
    /// <summary>Fewer in stock than in the cart (see <see cref="CartLine.Available"/>).</summary>
    NotEnoughStock,
}

/// <summary>
/// One cart line with the current price (the variant's own or the product's) and stock; <see cref="LineTotal"/> is 0
/// for a line with a problem.
/// </summary>
public sealed record CartLine(
    Guid Id,
    long Number,
    string Name,
    string Slug,
    string? VariantSku,
    string? VariantLabel,
    string? ImageUrl,
    decimal UnitPrice,
    decimal? CompareAtPrice,
    int Quantity,
    int Available,
    decimal LineTotal,
    CartLineProblem Problem);

/// <summary>
/// The cart in the shop's base currency: its lines, how many pieces (<see cref="ItemCount"/>, for the header badge),
/// the subtotal of the lines that can be bought, and whether any line has a problem (checkout waits for it).
/// </summary>
public sealed record CartView(IReadOnlyList<CartLine> Lines, int ItemCount, decimal Subtotal, string Currency, bool HasProblems);
