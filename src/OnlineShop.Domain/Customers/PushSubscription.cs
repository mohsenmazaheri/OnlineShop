using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Customers;

/// <summary>
/// One browser (device) of a signed-in customer that agreed to receive notifications: the push service address and the
/// browser's public key and secret that encrypt every message for it (RFC 8291). Personal data: erased with the account
/// and exported. Deleted when the push service answers that the browser withdrew it.
/// </summary>
public sealed class PushSubscription : BaseEntity
{
    public const int MaxPerCustomer = 20;

    public required string CustomerId { get; set; }

    /// <summary>The push service URL of this browser (unique).</summary>
    public required string Endpoint { get; set; }

    /// <summary>The browser's P-256 public key, base64url (65 bytes uncompressed).</summary>
    public required string P256dh { get; set; }

    /// <summary>The browser's 16-byte authentication secret, base64url.</summary>
    public required string Auth { get; set; }

    /// <summary>Language of the messages for this browser.</summary>
    public string Locale { get; set; } = string.Empty;
}
