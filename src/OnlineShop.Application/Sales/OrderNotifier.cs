using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Common;
using OnlineShop.Application.Notifications;

namespace OnlineShop.Application.Sales;

/// <summary>
/// Tells the customer what happened to their order: an email in the order's language (when it has an address) and,
/// for "placed" and "shipped", an SMS when the shop has SMS. A message that cannot be sent never undoes the change:
/// the failure is logged.
/// </summary>
public sealed partial class OrderNotifier(
    IApplicationDbContext db,
    IEmailSender email,
    ISmsSender sms,
    ISmsCapability smsCapability,
    IOrderLinks links,
    Customers.PushSubscriptionService push,
    IOptions<StoreOptions> store,
    ILogger<OrderNotifier> logger)
{
    /// <summary>The order's page: the account page for customers, the signed link for guests (relative to the language).</summary>
    public string PathFor(Guid orderId, long number, string? customerId) =>
        customerId is null
            ? $"order/{number}?key={Uri.EscapeDataString(links.TokenFor(orderId))}"
            : $"account/orders/{number}";

    public async Task NotifyAsync(Guid orderId, OrderNotice notice, CancellationToken cancellationToken = default) =>
        await NotifyAsync(orderId, notice, null, null, cancellationToken);

    /// <param name="detail">The return number, or for a refund the amount (formatted here in the order's currency).</param>
    /// <param name="note">A text from staff for the customer (a cancellation reason, an answer to a return).</param>
    public async Task NotifyAsync(Guid orderId, OrderNotice notice, string? detail, string? note, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.AsNoTracking().Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.Number, o.CustomerId, o.Email, o.PhoneNumber, o.Locale, o.Total, o.Currency, o.TrackingCarrier, o.TrackingCode })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null)
            return;

        var options = store.Value;
        var locale = options.LocaleList.Contains(order.Locale, StringComparer.Ordinal) ? order.Locale : options.DefaultLocale;
        var link = new Uri($"{options.PublicUrl.TrimEnd('/')}/{locale}/{PathFor(order.Id, order.Number, order.CustomerId)}");
        if (notice == OrderNotice.Refunded && decimal.TryParse(detail, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            detail = FormatTotal(amount, order.Currency, locale);
        var data = new OrderMessageData(order.Number, FormatTotal(order.Total, order.Currency, locale), order.TrackingCarrier, order.TrackingCode, link, detail, note);

        if (!string.IsNullOrWhiteSpace(order.Email))
        {
            try
            {
                await email.SendAsync(OrderMessages.Email(notice, locale, order.Email, options.Name, data), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogEmailFailed(logger, order.Number, notice, ex);
            }
        }

        // Notifications reach the browsers the customer switched them on in (signed-in customers only), in each one's language.
        if (order.CustomerId is { } customerId)
        {
            var path = $"/{{0}}/{PathFor(order.Id, order.Number, customerId)}";
            await push.NotifyAsync(customerId, browserLocale =>
            {
                var (title, body) = OrderMessages.Push(notice, browserLocale, options.Name, data);
                return new PushMessage(title, body, string.Format(CultureInfo.InvariantCulture, path, browserLocale));
            }, cancellationToken);
        }

        if (OrderMessages.HasSms(notice) && smsCapability.IsEnabled && !string.IsNullOrWhiteSpace(order.PhoneNumber))
        {
            try
            {
                await sms.SendOrderUpdateAsync(order.PhoneNumber, notice, data, locale, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSmsFailed(logger, order.Number, notice, ex);
            }
        }
    }

    /// <summary>"1,047,900 IRR": grouped in the language's way, decimals only when there are any.</summary>
    public static string FormatTotal(decimal total, string currency, string locale)
    {
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }
        return $"{total.ToString(decimal.Truncate(total) == total ? "N0" : "N2", culture)} {currency}";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The email about order {Number} ({Notice}) could not be sent.")]
    private static partial void LogEmailFailed(ILogger logger, long number, OrderNotice notice, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The SMS about order {Number} ({Notice}) could not be sent.")]
    private static partial void LogSmsFailed(ILogger logger, long number, OrderNotice notice, Exception exception);
}
