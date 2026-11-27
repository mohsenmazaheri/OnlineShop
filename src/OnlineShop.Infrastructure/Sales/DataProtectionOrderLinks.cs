using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using OnlineShop.Application.Sales;

namespace OnlineShop.Infrastructure.Sales;

/// <summary>
/// Guest order links signed with the shop's Data Protection keys: the token is the protected order id, so nothing is
/// stored, a link for one order never opens another, and old links keep working after the keys rotate.
/// </summary>
public sealed class DataProtectionOrderLinks(IDataProtectionProvider provider) : IOrderLinks
{
    private readonly IDataProtector _protector = provider.CreateProtector("OnlineShop.OrderLinks.v1");

    public string TokenFor(Guid orderId) => _protector.Protect(orderId.ToString("N"));

    public bool IsValid(Guid orderId, string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512)
            return false;
        try
        {
            return string.Equals(_protector.Unprotect(token), orderId.ToString("N"), StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
