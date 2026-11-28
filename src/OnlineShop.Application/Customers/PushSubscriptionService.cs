using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Common;
using OnlineShop.Application.Notifications;
using OnlineShop.Domain.Customers;

namespace OnlineShop.Application.Customers;

/// <summary>Whether this shop sends notifications and the key browsers subscribe with.</summary>
public sealed record PushSettingsView(bool Enabled, string? PublicKey);

/// <summary>A browser's subscription as <c>PushSubscription.toJSON()</c> gives it (keys base64url).</summary>
public sealed record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth, string? Locale);

public sealed record PushUnsubscribeRequest(string Endpoint);

public static class PushErrors
{
    public const string Disabled = "account.push_disabled";
    public const string Invalid = "account.push_invalid";
}

public sealed class PushSubscriptionRequestValidator : AbstractValidator<PushSubscriptionRequest>
{
    public PushSubscriptionRequestValidator()
    {
        // Push services are always https; anything else is not a browser's subscription.
        RuleFor(r => r.Endpoint).NotEmpty().WithErrorCode(PushErrors.Invalid).MaximumLength(2048).WithErrorCode(PushErrors.Invalid)
            .Must(e => Uri.TryCreate(e, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps).WithErrorCode(PushErrors.Invalid);
        RuleFor(r => r.P256dh).Must(k => Base64Url.TryDecode(k, out var bytes) && bytes.Length == 65 && bytes[0] == 4).WithErrorCode(PushErrors.Invalid);
        RuleFor(r => r.Auth).Must(k => Base64Url.TryDecode(k, out var bytes) && bytes.Length == 16).WithErrorCode(PushErrors.Invalid);
        RuleFor(r => r.Locale!).MaximumLength(8).When(r => r.Locale is not null);
    }
}

public sealed class PushUnsubscribeRequestValidator : AbstractValidator<PushUnsubscribeRequest>
{
    public PushUnsubscribeRequestValidator() =>
        RuleFor(r => r.Endpoint).NotEmpty().WithErrorCode(PushErrors.Invalid).MaximumLength(2048).WithErrorCode(PushErrors.Invalid);
}

/// <summary>URL-safe base64 without padding, as browsers write Web Push keys.</summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }

    public static bool TryDecode(string? text, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(text) || text.Length > 200)
            return false;
        try
        {
            bytes = Decode(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// Customers' notification subscriptions: one per browser, at most <see cref="PushSubscription.MaxPerCustomer"/>
/// (the oldest goes). A browser that signs in with another account moves to it. Sending forgets browsers the push
/// service reports gone; a failed send never stops the caller.
/// </summary>
public sealed partial class PushSubscriptionService(
    IApplicationDbContext db,
    IWebPushSender sender,
    IOptions<StoreOptions> store,
    ILogger<PushSubscriptionService> logger)
{
    public PushSettingsView Settings() => new(sender.IsEnabled, sender.PublicKey);

    public async Task<Result<bool>> SubscribeAsync(string userId, PushSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!sender.IsEnabled)
            return AppError.Conflict(PushErrors.Disabled, "This shop does not send notifications.");
        var locale = store.Value.ContentLocale(request.Locale);
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, cancellationToken);
        if (existing is null)
        {
            db.PushSubscriptions.Add(new PushSubscription { CustomerId = userId, Endpoint = request.Endpoint, P256dh = request.P256dh, Auth = request.Auth, Locale = locale });
        }
        else
        {
            existing.CustomerId = userId;
            existing.P256dh = request.P256dh;
            existing.Auth = request.Auth;
            existing.Locale = locale;
        }
        await db.SaveChangesAsync(cancellationToken);

        // Keep the newest browsers only.
        var extra = await db.PushSubscriptions.Where(s => s.CustomerId == userId).OrderByDescending(s => s.CreatedUtc)
            .Skip(PushSubscription.MaxPerCustomer).Select(s => s.Id).ToListAsync(cancellationToken);
        if (extra.Count > 0)
            await db.PushSubscriptions.Where(s => extra.Contains(s.Id)).ExecuteDeleteAsync(cancellationToken);
        return true;
    }

    /// <summary>Removes this browser of the customer (another customer's endpoint is left alone).</summary>
    public async Task<bool> UnsubscribeAsync(string userId, string endpoint, CancellationToken cancellationToken = default) =>
        await db.PushSubscriptions.Where(s => s.Endpoint == endpoint && s.CustomerId == userId).ExecuteDeleteAsync(cancellationToken) > 0;

    /// <summary>Sends to every browser of the customer; returns how many received it. Never throws.</summary>
    public async Task<int> NotifyAsync(string customerId, Func<string, PushMessage> message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!sender.IsEnabled)
            return 0;
        var subscriptions = await db.PushSubscriptions.AsNoTracking().Where(s => s.CustomerId == customerId)
            .Select(s => new { s.Id, s.Endpoint, s.P256dh, s.Auth, s.Locale }).ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var s in subscriptions)
        {
            PushResult result;
            try
            {
                result = await sender.SendAsync(new PushTarget(s.Endpoint, s.P256dh, s.Auth), message(s.Locale), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
                continue;
            }
            if (result == PushResult.Sent)
                sent++;
            else if (result == PushResult.Gone)
                await db.PushSubscriptions.Where(x => x.Id == s.Id).ExecuteDeleteAsync(cancellationToken);
        }
        return sent;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A notification could not be sent.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
