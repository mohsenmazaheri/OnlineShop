using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Customers;

namespace OnlineShop.Application.Customers;

/// <summary>
/// A customer's address book. Every query is limited to the signed-in customer's own rows
/// (<c>CustomerId == userId</c> inside the query), so another customer's address id answers 404.
/// At most one address is the default; the first one always is.
/// </summary>
public sealed class AddressService(IApplicationDbContext db, IPhoneNumberNormalizer phones, IOptions<StoreOptions> store)
{
    public async Task<IReadOnlyList<AddressView>> ListAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.CustomerAddresses.AsNoTracking()
            .Where(a => a.CustomerId == userId)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedUtc)
            .Select(a => new AddressView(a.Id, a.Label, a.RecipientName, a.PhoneNumber, a.CountryCode, a.Region, a.City,
                a.Line1, a.Line2, a.PostalCode, a.IsDefault))
            .ToListAsync(cancellationToken);

    public async Task<Result<AddressView>> CreateAsync(string userId, AddressInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var existing = await db.CustomerAddresses.CountAsync(a => a.CustomerId == userId, cancellationToken);
        if (existing >= CustomerAddress.MaxPerCustomer)
            return AppError.Conflict(AddressErrors.LimitReached, $"An address book holds at most {CustomerAddress.MaxPerCustomer} addresses.");

        var address = new CustomerAddress
        {
            CustomerId = userId, RecipientName = "", PhoneNumber = "", CountryCode = "", City = "", Line1 = ""
        };
        var applied = Apply(address, input);
        if (applied is not null)
            return applied;

        // The first address is the default; a new default replaces the old one.
        address.IsDefault = input.IsDefault || existing == 0;
        if (address.IsDefault)
            await ClearDefaultAsync(userId, cancellationToken);

        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync(cancellationToken);
        return ToView(address);
    }

    public async Task<Result<AddressView>> UpdateAsync(string userId, Guid id, AddressInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var address = await db.CustomerAddresses.SingleOrDefaultAsync(a => a.Id == id && a.CustomerId == userId, cancellationToken);
        if (address is null)
            return NotFound();

        var applied = Apply(address, input);
        if (applied is not null)
            return applied;

        // Un-ticking "default" on the default address is ignored: there must be one while the book is not empty.
        if (input.IsDefault && !address.IsDefault)
        {
            await ClearDefaultAsync(userId, cancellationToken);
            address.IsDefault = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToView(address);
    }

    public async Task<Result<bool>> SetDefaultAsync(string userId, Guid id, CancellationToken cancellationToken = default)
    {
        var address = await db.CustomerAddresses.SingleOrDefaultAsync(a => a.Id == id && a.CustomerId == userId, cancellationToken);
        if (address is null)
            return NotFound();
        if (!address.IsDefault)
        {
            await ClearDefaultAsync(userId, cancellationToken);
            address.IsDefault = true;
            await db.SaveChangesAsync(cancellationToken);
        }
        return true;
    }

    public async Task<Result<bool>> DeleteAsync(string userId, Guid id, CancellationToken cancellationToken = default)
    {
        var address = await db.CustomerAddresses.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == id && a.CustomerId == userId, cancellationToken);
        if (address is null)
            return NotFound();

        // Personal data: delete the row for real (a soft delete would keep the address forever).
        await db.CustomerAddresses.Where(a => a.Id == id && a.CustomerId == userId).ExecuteDeleteAsync(cancellationToken);

        if (address.IsDefault)
        {
            var next = await db.CustomerAddresses.Where(a => a.CustomerId == userId)
                .OrderByDescending(a => a.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
            if (next is not null)
            {
                next.IsDefault = true;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        return true;
    }

    /// <summary>
    /// The address after the business checks (a shipping country, a valid phone number read in that country, a postal
    /// code of the country's format), or the first problem.
    /// </summary>
    public Result<ShippingAddress> Check(AddressInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var country = input.CountryCode.Trim().ToUpperInvariant();
        if (!store.Value.ShippingCountryList.Contains(country, StringComparer.Ordinal))
            return AppError.Validation(AddressErrors.CountryNotSupported, "The shop does not ship to this country.");

        // A local number is read in the address's country (a German number typed without +49 for a German address).
        var phone = phones.Normalize(input.Phone, country);
        if (phone is null)
            return AppError.Validation(AccountErrors.PhoneInvalid, "The phone number is not valid.");

        string? postalCode = null;
        if (!string.IsNullOrWhiteSpace(input.PostalCode))
        {
            postalCode = PostalCodes.Normalize(country, input.PostalCode);
            if (postalCode is null)
                return AppError.Validation(AddressErrors.PostalCodeInvalid, "The postal code is not valid for this country.");
        }
        return new ShippingAddress(input.RecipientName.Trim(), phone, country, Clean(input.Region), input.City.Trim(), input.Line1.Trim(), Clean(input.Line2), postalCode);
    }

    /// <summary>One of the customer's own addresses (ownership checked in the query), or null.</summary>
    public async Task<ShippingAddress?> FindAsync(string userId, Guid id, CancellationToken cancellationToken = default) =>
        await db.CustomerAddresses.AsNoTracking()
            .Where(a => a.Id == id && a.CustomerId == userId)
            .Select(a => new ShippingAddress(a.RecipientName, a.PhoneNumber, a.CountryCode, a.Region, a.City, a.Line1, a.Line2, a.PostalCode))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Copies the input onto the entity after the business checks; returns the error, or null when it was applied.</summary>
    private AppError? Apply(CustomerAddress address, AddressInput input)
    {
        var checkedAddress = Check(input);
        if (!checkedAddress.IsSuccess)
            return checkedAddress.Error;
        var a = checkedAddress.Value;
        address.Label = Clean(input.Label);
        address.RecipientName = a.RecipientName;
        address.PhoneNumber = a.PhoneNumber;
        address.CountryCode = a.CountryCode;
        address.Region = a.Region;
        address.City = a.City;
        address.Line1 = a.Line1;
        address.Line2 = a.Line2;
        address.PostalCode = a.PostalCode;
        return null;
    }

    private async Task ClearDefaultAsync(string userId, CancellationToken cancellationToken)
    {
        var defaults = await db.CustomerAddresses.Where(a => a.CustomerId == userId && a.IsDefault).ToListAsync(cancellationToken);
        foreach (var other in defaults)
            other.IsDefault = false;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AppError NotFound() => AppError.NotFound(AddressErrors.NotFound, "Address not found.");

    private static AddressView ToView(CustomerAddress a) =>
        new(a.Id, a.Label, a.RecipientName, a.PhoneNumber, a.CountryCode, a.Region, a.City, a.Line1, a.Line2, a.PostalCode, a.IsDefault);
}
