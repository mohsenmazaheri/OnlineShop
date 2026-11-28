using System.Globalization;

namespace OnlineShop.Application.Notifications;

/// <summary>What happened to an order that the customer is told about.</summary>
public enum OrderNotice
{
    Placed,
    Paid,
    Shipped,
    Canceled,
    PaymentExpired,
    ReturnRequested,
    ReturnApproved,
    ReturnRejected,
    Refunded,
}

/// <summary>
/// The facts an order message needs (already formatted where it matters). <see cref="Detail"/> is the return number or
/// the refunded amount; <see cref="Note"/> a text from staff for the customer (a reason, an answer).
/// </summary>
public sealed record OrderMessageData(long Number, string Total, string? TrackingCarrier, string? TrackingCode, Uri Link, string? Detail = null, string? Note = null);

/// <summary>
/// Order emails and SMS texts in every supported language. Plain and short like the account emails: one link to the
/// order's page, where everything else is.
/// </summary>
public static class OrderMessages
{
    private sealed record Text(string Subject, string Body, string Action);

    private static readonly Dictionary<(OrderNotice, string), Text> Emails = new()
    {
        [(OrderNotice.Placed, "fa")] = new("سفارش {1} در {0} ثبت شد", "سپاس از خرید شما. سفارش شمارهٔ {1} به مبلغ {2} ثبت شد و کالاهای آن برایتان نگه داشته می‌شوند. جزئیات و وضعیت سفارش را در صفحهٔ آن ببینید.", "دیدن سفارش"),
        [(OrderNotice.Placed, "en")] = new("Your order {1} at {0}", "Thank you for your order. Order {1} for {2} is placed and its items are held for you. See its details and status on its page.", "View your order"),
        [(OrderNotice.Placed, "hy")] = new("Ձեր {1} պատվերը {0}-ում", "Շնորհակալություն պատվերի համար։ {1} պատվերը {2} գումարով ձևակերպված է, և ապրանքները պահվում են ձեզ համար։ Մանրամասները տեսեք պատվերի էջում։", "Դիտել պատվերը"),
        [(OrderNotice.Placed, "de")] = new("Ihre Bestellung {1} bei {0}", "Vielen Dank für Ihre Bestellung. Bestellung {1} über {2} ist aufgegeben, die Artikel sind für Sie reserviert. Details und Status finden Sie auf ihrer Seite.", "Bestellung ansehen"),
        [(OrderNotice.Placed, "ar")] = new("طلبك {1} في {0}", "شكرًا لطلبك. تم تسجيل الطلب {1} بمبلغ {2} وحجز منتجاته لك. اطّلع على التفاصيل والحالة في صفحة الطلب.", "عرض الطلب"),

        [(OrderNotice.Paid, "fa")] = new("پرداخت سفارش {1} در {0} تأیید شد", "پرداخت سفارش شمارهٔ {1} به مبلغ {2} تأیید شد و سفارش برای ارسال آماده می‌شود.", "دیدن سفارش"),
        [(OrderNotice.Paid, "en")] = new("Payment received for order {1} at {0}", "The payment for order {1} ({2}) is confirmed. We are preparing your order.", "View your order"),
        [(OrderNotice.Paid, "hy")] = new("{1} պատվերի վճարումը {0}-ում ստացված է", "{1} պատվերի վճարումը ({2}) հաստատված է։ Պատրաստում ենք ձեր պատվերը։", "Դիտել պատվերը"),
        [(OrderNotice.Paid, "de")] = new("Zahlung für Bestellung {1} bei {0} erhalten", "Die Zahlung für Bestellung {1} ({2}) ist bestätigt. Wir bereiten Ihre Bestellung vor.", "Bestellung ansehen"),
        [(OrderNotice.Paid, "ar")] = new("تم استلام دفع الطلب {1} في {0}", "تم تأكيد دفع الطلب {1} ({2}). نحن نجهّز طلبك.", "عرض الطلب"),

        [(OrderNotice.Shipped, "fa")] = new("سفارش {1} از {0} ارسال شد", "سفارش شمارهٔ {1} ارسال شد. شرکت حمل: {3}، کد رهگیری: {4}", "دیدن سفارش"),
        [(OrderNotice.Shipped, "en")] = new("Your order {1} from {0} is on its way", "Order {1} has shipped. Carrier: {3}, tracking code: {4}", "View your order"),
        [(OrderNotice.Shipped, "hy")] = new("{0}-ի ձեր {1} պատվերը ճանապարհին է", "{1} պատվերն ուղարկված է։ Առաքիչ՝ {3}, հետևման կոդ՝ {4}", "Դիտել պատվերը"),
        [(OrderNotice.Shipped, "de")] = new("Ihre Bestellung {1} von {0} ist unterwegs", "Bestellung {1} wurde versandt. Versanddienst: {3}, Sendungsnummer: {4}", "Bestellung ansehen"),
        [(OrderNotice.Shipped, "ar")] = new("طلبك {1} من {0} في الطريق إليك", "تم شحن الطلب {1}. شركة الشحن: {3}، رمز التتبع: {4}", "عرض الطلب"),

        [(OrderNotice.Canceled, "fa")] = new("سفارش {1} در {0} لغو شد", "سفارش شمارهٔ {1} لغو شد. اگر پرسشی دارید، با فروشگاه تماس بگیرید.", "دیدن سفارش"),
        [(OrderNotice.Canceled, "en")] = new("Order {1} at {0} is canceled", "Order {1} has been canceled. If you have any questions, please contact the shop.", "View your order"),
        [(OrderNotice.Canceled, "hy")] = new("{0}-ում {1} պատվերը չեղարկված է", "{1} պատվերը չեղարկվել է։ Հարցերի դեպքում կապվեք խանութի հետ։", "Դիտել պատվերը"),
        [(OrderNotice.Canceled, "de")] = new("Bestellung {1} bei {0} storniert", "Bestellung {1} wurde storniert. Bei Fragen wenden Sie sich bitte an den Shop.", "Bestellung ansehen"),
        [(OrderNotice.Canceled, "ar")] = new("تم إلغاء الطلب {1} في {0}", "تم إلغاء الطلب {1}. إن كانت لديك أسئلة، يُرجى التواصل مع المتجر.", "عرض الطلب"),

        [(OrderNotice.PaymentExpired, "fa")] = new("مهلت پرداخت سفارش {1} در {0} تمام شد", "مهلت پرداخت سفارش شمارهٔ {1} تمام شد و سفارش لغو شد. می‌توانید دوباره سفارش دهید.", "دیدن سفارش"),
        [(OrderNotice.PaymentExpired, "en")] = new("Order {1} at {0}: the payment time ran out", "The payment time for order {1} ran out, so the order was canceled. You are welcome to order again.", "View your order"),
        [(OrderNotice.PaymentExpired, "hy")] = new("{0}-ում {1} պատվերի վճարման ժամկետը լրացել է", "{1} պատվերի վճարման ժամկետը լրացել է, և պատվերը չեղարկվել է։ Կարող եք կրկին պատվիրել։", "Դիտել պատվերը"),
        [(OrderNotice.PaymentExpired, "de")] = new("Bestellung {1} bei {0}: Zahlungsfrist abgelaufen", "Die Zahlungsfrist für Bestellung {1} ist abgelaufen, die Bestellung wurde storniert. Sie können gern erneut bestellen.", "Bestellung ansehen"),
        [(OrderNotice.PaymentExpired, "ar")] = new("الطلب {1} في {0}: انتهت مهلة الدفع", "انتهت مهلة دفع الطلب {1} فتم إلغاؤه. يسعدنا أن تطلب مرة أخرى.", "عرض الطلب"),

        [(OrderNotice.ReturnRequested, "fa")] = new("درخواست مرجوعی {5} برای سفارش {1} در {0}", "درخواست مرجوعی {5} برای سفارش شمارهٔ {1} ثبت شد. پس از بررسی، نتیجه را به شما خبر می‌دهیم.", "دیدن سفارش"),
        [(OrderNotice.ReturnRequested, "en")] = new("Return {5} for order {1} at {0}", "We received your return request {5} for order {1}. We will let you know once we have looked at it.", "View your order"),
        [(OrderNotice.ReturnRequested, "hy")] = new("{5} վերադարձ {1} պատվերի համար {0}-ում", "Ստացել ենք ձեր {5} վերադարձի հայտը {1} պատվերի համար։ Կտեղեկացնենք ուսումնասիրելուց հետո։", "Դիտել պատվերը"),
        [(OrderNotice.ReturnRequested, "de")] = new("Rücksendung {5} zu Bestellung {1} bei {0}", "Wir haben Ihre Rücksendeanfrage {5} zu Bestellung {1} erhalten und melden uns nach der Prüfung.", "Bestellung ansehen"),
        [(OrderNotice.ReturnRequested, "ar")] = new("الإرجاع {5} للطلب {1} في {0}", "استلمنا طلب الإرجاع {5} للطلب {1}. سنُعلمك بعد مراجعته.", "عرض الطلب"),

        [(OrderNotice.ReturnApproved, "fa")] = new("مرجوعی {5} سفارش {1} در {0} پذیرفته شد", "درخواست مرجوعی {5} برای سفارش شمارهٔ {1} پذیرفته شد. لطفاً کالاها را برای فروشگاه بفرستید.", "دیدن سفارش"),
        [(OrderNotice.ReturnApproved, "en")] = new("Return {5} for order {1} at {0} is approved", "Your return {5} for order {1} is approved. Please send the items back to the shop.", "View your order"),
        [(OrderNotice.ReturnApproved, "hy")] = new("{1} պատվերի {5} վերադարձը {0}-ում հաստատված է", "{1} պատվերի ձեր {5} վերադարձը հաստատված է։ Խնդրում ենք ապրանքներն ուղարկել խանութ։", "Դիտել պատվերը"),
        [(OrderNotice.ReturnApproved, "de")] = new("Rücksendung {5} zu Bestellung {1} bei {0} genehmigt", "Ihre Rücksendung {5} zu Bestellung {1} ist genehmigt. Bitte senden Sie die Artikel an den Shop zurück.", "Bestellung ansehen"),
        [(OrderNotice.ReturnApproved, "ar")] = new("تمت الموافقة على الإرجاع {5} للطلب {1} في {0}", "تمت الموافقة على الإرجاع {5} للطلب {1}. يُرجى إرسال المنتجات إلى المتجر.", "عرض الطلب"),

        [(OrderNotice.ReturnRejected, "fa")] = new("مرجوعی {5} سفارش {1} در {0} پذیرفته نشد", "درخواست مرجوعی {5} برای سفارش شمارهٔ {1} پذیرفته نشد.", "دیدن سفارش"),
        [(OrderNotice.ReturnRejected, "en")] = new("Return {5} for order {1} at {0} was not accepted", "Your return request {5} for order {1} was not accepted.", "View your order"),
        [(OrderNotice.ReturnRejected, "hy")] = new("{1} պատվերի {5} վերադարձը {0}-ում չի ընդունվել", "{1} պատվերի ձեր {5} վերադարձի հայտը չի ընդունվել։", "Դիտել պատվերը"),
        [(OrderNotice.ReturnRejected, "de")] = new("Rücksendung {5} zu Bestellung {1} bei {0} abgelehnt", "Ihre Rücksendeanfrage {5} zu Bestellung {1} wurde nicht angenommen.", "Bestellung ansehen"),
        [(OrderNotice.ReturnRejected, "ar")] = new("لم يُقبل الإرجاع {5} للطلب {1} في {0}", "لم يُقبل طلب الإرجاع {5} للطلب {1}.", "عرض الطلب"),

        [(OrderNotice.Refunded, "fa")] = new("بازپرداخت سفارش {1} در {0}", "مبلغ {5} از سفارش شمارهٔ {1} به شما بازپرداخت شد.", "دیدن سفارش"),
        [(OrderNotice.Refunded, "en")] = new("Refund for order {1} at {0}", "We have paid back {5} for order {1}.", "View your order"),
        [(OrderNotice.Refunded, "hy")] = new("Վերադարձված գումար {1} պատվերի համար {0}-ում", "{1} պատվերի համար ձեզ վերադարձվել է {5}։", "Դիտել պատվերը"),
        [(OrderNotice.Refunded, "de")] = new("Erstattung zu Bestellung {1} bei {0}", "Wir haben Ihnen {5} für Bestellung {1} erstattet.", "Bestellung ansehen"),
        [(OrderNotice.Refunded, "ar")] = new("استرداد للطلب {1} في {0}", "أعدنا إليك {5} عن الطلب {1}.", "عرض الطلب"),
    };

    private static readonly Dictionary<(OrderNotice, string), string> Sms = new()
    {
        [(OrderNotice.Placed, "fa")] = "{0}: سفارش {1} ثبت شد. {2}",
        [(OrderNotice.Placed, "en")] = "{0}: order {1} is placed. {2}",
        [(OrderNotice.Placed, "hy")] = "{0}՝ {1} պատվերը ձևակերպված է։ {2}",
        [(OrderNotice.Placed, "de")] = "{0}: Bestellung {1} ist aufgegeben. {2}",
        [(OrderNotice.Placed, "ar")] = "{0}: تم تسجيل الطلب {1}. {2}",
        [(OrderNotice.Shipped, "fa")] = "{0}: سفارش {1} ارسال شد. کد رهگیری: {3}",
        [(OrderNotice.Shipped, "en")] = "{0}: order {1} has shipped. Tracking code: {3}",
        [(OrderNotice.Shipped, "hy")] = "{0}՝ {1} պատվերն ուղարկված է։ Հետևման կոդ՝ {3}",
        [(OrderNotice.Shipped, "de")] = "{0}: Bestellung {1} ist versandt. Sendungsnummer: {3}",
        [(OrderNotice.Shipped, "ar")] = "{0}: تم شحن الطلب {1}. رمز التتبع: {3}",
    };

    private static readonly HashSet<string> RtlLocales = new(["fa", "ar"], StringComparer.Ordinal);

    /// <summary>Which notices also go by SMS (when the shop has SMS): the order is in, and it is on its way.</summary>
    public static bool HasSms(OrderNotice notice) => notice is OrderNotice.Placed or OrderNotice.Shipped;

    public static EmailMessage Email(OrderNotice notice, string locale, string to, string storeName, OrderMessageData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var effective = Emails.ContainsKey((notice, locale)) ? locale : "en";
        var text = Emails[(notice, effective)];
        string fill(string template) => string.Format(CultureInfo.InvariantCulture, template, storeName, data.Number, data.Total, data.TrackingCarrier, data.TrackingCode, data.Detail);
        var subject = fill(text.Subject);
        var body = fill(text.Body);
        var note = string.IsNullOrWhiteSpace(data.Note) ? null : data.Note.Trim();
        var url = data.Link.AbsoluteUri;
        var textBody = note is null ? $"{body}\n\n{url}\n\n— {storeName}" : $"{body}\n\n{note}\n\n{url}\n\n— {storeName}";
        static string encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        var dir = RtlLocales.Contains(effective) ? "rtl" : "ltr";
        var htmlBody =
            $"<!doctype html><html lang=\"{effective}\" dir=\"{dir}\"><body style=\"font-family:Tahoma,Arial,sans-serif;line-height:1.7\">" +
            $"<p>{encode(body)}</p>" +
            (note is null ? "" : $"<p style=\"padding:10px;background:#f4f4f5;border-radius:8px\">{encode(note)}</p>") +
            $"<p><a href=\"{encode(url)}\" style=\"display:inline-block;padding:10px 18px;background:#0f766e;color:#ffffff;border-radius:8px;text-decoration:none\">{encode(text.Action)}</a></p>" +
            $"<p>— {encode(storeName)}</p></body></html>";
        return new EmailMessage(to, subject, textBody, htmlBody);
    }

    /// <summary>The notification of a notice: the email's subject and first paragraph, in that language.</summary>
    public static (string Title, string Body) Push(OrderNotice notice, string locale, string storeName, OrderMessageData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var text = Emails[(notice, Emails.ContainsKey((notice, locale)) ? locale : "en")];
        string fill(string template) => string.Format(CultureInfo.InvariantCulture, template, storeName, data.Number, data.Total, data.TrackingCarrier, data.TrackingCode, data.Detail);
        return (fill(text.Subject), fill(text.Body));
    }

    /// <summary>The free-text SMS (Twilio, development pickup); Iranian providers use their own approved templates.</summary>
    public static string SmsText(OrderNotice notice, string locale, string storeName, OrderMessageData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var template = Sms.TryGetValue((notice, locale), out var text) ? text : Sms[(notice, "en")];
        return string.Format(CultureInfo.InvariantCulture, template, storeName, data.Number, data.Link.AbsoluteUri, data.TrackingCode);
    }
}
