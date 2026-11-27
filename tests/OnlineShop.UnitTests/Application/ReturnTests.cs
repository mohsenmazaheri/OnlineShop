using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Customers;
using OnlineShop.Application.Notifications;
using OnlineShop.Application.Sales;
using OnlineShop.Domain.Sales;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class InvoiceAndReturnTests : DemoCatalogTestBase
{
    private SalesKit? _kit;

    private SalesKit Kit => _kit ??= new SalesKit(Database, Store) { StaffUserId = "staff-1" };

    private const string Phone = "DEMO-PHN-001";

    private async Task<PlacedOrderView> PlaceAsync(int quantity = 2, string? code = null)
    {
        var number = await NumberOfAsync(Phone);
        await using var db = Database.CreateContext();
        var owner = new CartOwner(null, CartService.NewGuestToken());
        await new CartService(db, Options.Create(Store)).AddAsync(owner, new AddToCartRequest(number, null, quantity), "en");
        var method = await db.ShippingMethods.Select(m => (Guid?)m.Id).FirstOrDefaultAsync()
            ?? (await new ShippingMethodService(db, Options.Create(Store)).CreateAsync(
                new SaveShippingMethodRequest([new LocalizedText("fa", "پست", null, null)], false, 5, 0, null, [], null, null, true, 0))).Value.Id;
        var checkout = Kit.Checkout(db);
        var total = (await checkout.QuoteAsync(owner, new CheckoutQuoteRequest("IR", method, code), "en")).Totals.Total;
        var address = new AddressInput(null, "Sara", "09121234567", "IR", null, "Tehran", "Valiasr 10", null, null, false);
        return (await checkout.PlaceAsync(owner, new PlaceOrderRequest(method, null, address, false, "sara@example.com", null, null, code, null, total), "en")).Value;
    }

    private static string KeyOf(PlacedOrderView order) => Uri.UnescapeDataString(order.Path[(order.Path.IndexOf("key=", StringComparison.Ordinal) + 4)..]);

    private async Task<int> StockAsync()
    {
        await using var db = Database.CreateContext();
        return await db.Products.Where(p => p.SKU == Phone).Select(p => p.Stock).SingleAsync();
    }

    /// <summary>Places, pays, ships and delivers an order.</summary>
    private async Task<PlacedOrderView> DeliveredAsync(int quantity = 2, string? code = null)
    {
        var order = await PlaceAsync(quantity, code);
        await using var db = Database.CreateContext();
        var lifecycle = Kit.Lifecycle(db);
        await lifecycle.MarkPaidAsync(order.Number, new OrderActionRequest(null));
        await lifecycle.ShipAsync(order.Number, new ShipOrderRequest("Post", "RR1"));
        await lifecycle.CompleteAsync(order.Number, new OrderActionRequest(null));
        return order;
    }

    [Fact]
    public async Task Paid_orders_get_invoices_in_one_unbroken_sequence_with_the_seller_details_of_that_moment()
    {
        await SeedDemoAsync();
        await using (var setup = Database.CreateContext())
            await new SalesSettingsService(setup).SaveAsync(new SalesSettingsView(" Sara Trading LLC ", "Valiasr 1, Tehran", "IR-123", "Thank you", 14));
        var first = await PlaceAsync(1);
        var second = await PlaceAsync(1);
        await using var db = Database.CreateContext();
        var lifecycle = Kit.Lifecycle(db);
        var queries = Kit.Queries(db);

        // Unpaid: no invoice yet.
        Assert.Null(await queries.GetInvoiceAsync(first.Number, null, null, staff: true));
        await lifecycle.MarkPaidAsync(second.Number, new OrderActionRequest(null));
        await new SalesSettingsService(db).SaveAsync(new SalesSettingsView("Renamed", null, null, null, 14));
        await lifecycle.MarkPaidAsync(first.Number, new OrderActionRequest(null));

        var invoiceOfSecond = (await queries.GetInvoiceAsync(second.Number, null, KeyOf(second), staff: false))!;
        Assert.Equal((1L, "Sara Trading LLC", "IR-123", "Thank you"), (invoiceOfSecond.Number, invoiceOfSecond.SellerName, invoiceOfSecond.SellerTaxNumber, invoiceOfSecond.Note));
        Assert.Equal(second.Number, invoiceOfSecond.Order.Number);
        var invoiceOfFirst = (await queries.GetInvoiceAsync(first.Number, null, null, staff: true))!;
        Assert.Equal((2L, "Renamed"), (invoiceOfFirst.Number, invoiceOfFirst.SellerName));
        // A guest needs the right link; nobody else's order opens.
        Assert.Null(await queries.GetInvoiceAsync(first.Number, null, KeyOf(second), staff: false));
        Assert.Null(await queries.GetInvoiceAsync(first.Number, "someone", null, staff: false));
        Assert.Equal(new InvoiceSummary(2, Database.Clock.GetUtcNow().UtcDateTime), (await queries.GetForStaffAsync(first.Number))!.Invoice);
    }

    [Fact]
    public async Task A_paid_order_canceled_before_shipping_brings_its_stock_back_and_can_be_refunded()
    {
        await SeedDemoAsync();
        await using (var setup = Database.CreateContext())
            await new DiscountCodeService(setup, Database.Clock).CreateAsync(new SaveDiscountCodeRequest("TEN", DiscountKind.Percent, 10, null, null, null, null, null, false, true, null));
        var stock = await StockAsync();
        var order = await PlaceAsync(2, "TEN");
        await using var db = Database.CreateContext();
        var lifecycle = Kit.Lifecycle(db);
        await lifecycle.MarkPaidAsync(order.Number, new OrderActionRequest(null));
        Assert.Equal(stock - 2, await StockAsync());

        Assert.Equal(OrderStatus.Canceled, (await lifecycle.CancelAsync(order.Number, new OrderActionRequest("Customer changed their mind"))).Value);
        Assert.Equal(stock, await StockAsync());
        Assert.Equal(0, await db.DiscountCodes.Select(d => d.UsedCount).SingleAsync());
        Assert.Equal("Customer changed their mind", Kit.Emails.Sent[^1].TextBody.Split("\n\n")[1]);

        var view = (await Kit.Queries(db).GetForStaffAsync(order.Number))!;
        Assert.Equal((order.Total, true), (view.Refundable, view.Actions.CanRefund));
        var returns = Kit.Returns(db);
        Assert.Equal(OrderErrors.RefundTooMuch, (await returns.RefundAsync(order.Number, new RecordRefundRequest(order.Total + 1, "Card", null, null))).Error!.Code);
        Assert.Equal(order.Total, (await returns.RefundAsync(order.Number, new RecordRefundRequest(order.Total, "Card", "Ref 42", null))).Value);
        view = (await Kit.Queries(db).GetForStaffAsync(order.Number))!;
        Assert.Equal((order.Total, 0m, false), (view.RefundedTotal, view.Refundable, view.Actions.CanRefund));
        Assert.Equal("Ref 42", Assert.Single(view.Refunds).Note);
        Assert.Contains("Refund for order", Kit.Emails.Sent[^1].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delivered_order_is_returned_received_back_into_stock_and_refunded()
    {
        await SeedDemoAsync();
        var order = await DeliveredAsync(2);
        var stock = await StockAsync();
        await using var db = Database.CreateContext();
        var queries = Kit.Queries(db);
        var guestView = (await queries.GetWithTokenAsync(order.Number, KeyOf(order)))!;
        Assert.True(guestView.Actions.CanRequestReturn);
        Assert.Equal(Database.Clock.GetUtcNow().UtcDateTime.AddDays(14), guestView.ReturnableUntilUtc);
        var line = Assert.Single(guestView.Lines);
        var id = (await queries.FindIdAsync(order.Number, null, KeyOf(order)))!.Value;
        var returns = Kit.Returns(db);

        Assert.Equal(OrderErrors.ReturnQuantityInvalid, (await returns.RequestAsync(id, new RequestReturnRequest([new(line.Id, 3)], "Too big"))).Error!.Code);
        Assert.Equal(OrderErrors.ReturnQuantityInvalid, (await returns.RequestAsync(id, new RequestReturnRequest([new(Guid.NewGuid(), 1)], "Too big"))).Error!.Code);
        Assert.Equal(1L, (await returns.RequestAsync(id, new RequestReturnRequest([new(line.Id, 1)], " Too big "))).Value);
        Assert.Equal(1, (await queries.GetWithTokenAsync(order.Number, KeyOf(order)))!.Lines[0].Returnable);
        Assert.Contains("R-1", Kit.Emails.Sent[^1].Subject, StringComparison.Ordinal);

        var staff = (await queries.GetForStaffAsync(order.Number))!;
        var request = Assert.Single(staff.Returns);
        Assert.Equal((ReturnStatus.Requested, "Too big", true, false), (request.Status, request.Reason, request.CanApprove, request.CanReceive));
        // Its share of the paid goods (no discount here).
        Assert.Equal(line.UnitPrice, request.SuggestedRefund);
        Assert.Equal(OrderErrors.ReturnStatusInvalid, (await returns.ReceiveAsync(order.Number, request.Id, new ReturnAnswerRequest(null))).Error!.Code);
        Assert.Equal(OrderErrors.ReturnStatusInvalid, (await returns.RefundAsync(order.Number, new RecordRefundRequest(1, "Card", null, request.Id))).Error!.Code);

        Assert.Equal(ReturnStatus.Approved, (await returns.ApproveAsync(order.Number, request.Id, new ReturnAnswerRequest("Send it to Valiasr 1"))).Value);
        Assert.Equal("Send it to Valiasr 1", Kit.Emails.Sent[^1].TextBody.Split("\n\n")[1]);
        Assert.Equal(ReturnStatus.Received, (await returns.ReceiveAsync(order.Number, request.Id, new ReturnAnswerRequest(null))).Value);
        Assert.Equal(stock + 1, await StockAsync());
        Assert.Equal(line.UnitPrice, (await returns.RefundAsync(order.Number, new RecordRefundRequest(line.UnitPrice, "Bank transfer", null, request.Id))).Value);

        var customer = (await queries.GetWithTokenAsync(order.Number, KeyOf(order)))!;
        var refund = Assert.Single(customer.Refunds);
        Assert.Equal((line.UnitPrice, (string?)null, (long?)1), (refund.Amount, refund.Note, refund.ReturnNumber));
        Assert.Equal(ReturnStatus.Received, Assert.Single(customer.Returns).Status);
        Assert.False(customer.Returns[0].CanApprove);
        Assert.Contains(customer.History, h => h.Kind == OrderEventKind.ReturnReceived && h.Text == "R-1");
    }

    [Fact]
    public async Task Returns_follow_the_return_period_and_a_rejected_return_frees_its_items()
    {
        await SeedDemoAsync();
        var order = await DeliveredAsync(1);
        await using var db = Database.CreateContext();
        var queries = Kit.Queries(db);
        var returns = Kit.Returns(db);
        var id = (await queries.FindIdAsync(order.Number, null, KeyOf(order)))!.Value;
        var lineId = (await queries.GetForStaffAsync(order.Number))!.Lines[0].Id;

        await returns.RequestAsync(id, new RequestReturnRequest([new(lineId, 1)], "Broken"));
        Assert.Equal(OrderErrors.ReturnQuantityInvalid, (await returns.RequestAsync(id, new RequestReturnRequest([new(lineId, 1)], "Again"))).Error!.Code);
        var first = (await queries.GetForStaffAsync(order.Number))!.Returns[0];
        Assert.Equal(ReturnStatus.Rejected, (await returns.RejectAsync(order.Number, first.Id, new ReturnAnswerRequest("Used"))).Value);
        Assert.Equal(OrderErrors.ReturnStatusInvalid, (await returns.ApproveAsync(order.Number, first.Id, new ReturnAnswerRequest(null))).Error!.Code);
        Assert.Equal(OrderErrors.ReturnNotFound, (await returns.ApproveAsync(order.Number + 1, first.Id, new ReturnAnswerRequest(null))).Error!.Code);
        Assert.Equal(2L, (await returns.RequestAsync(id, new RequestReturnRequest([new(lineId, 1)], "Again"))).Value);

        // After the period, or with returns switched off, no more requests.
        Database.Clock.Advance(TimeSpan.FromDays(15));
        Assert.Null((await queries.GetForStaffAsync(order.Number))!.ReturnableUntilUtc);
        Assert.Equal(OrderErrors.ReturnNotAllowed, (await returns.RequestAsync(id, new RequestReturnRequest([new(lineId, 1)], "Late"))).Error!.Code);
        var other = await DeliveredAsync(1);
        await new SalesSettingsService(db).SaveAsync(new SalesSettingsView(null, null, null, null, 0));
        var otherId = (await queries.FindIdAsync(other.Number, null, KeyOf(other)))!.Value;
        Assert.Equal(OrderErrors.ReturnNotAllowed, (await returns.RequestAsync(otherId, new RequestReturnRequest([new((await queries.GetForStaffAsync(other.Number))!.Lines[0].Id, 1)], "No"))).Error!.Code);

        // Nothing refunds an unpaid order.
        var unpaid = await PlaceAsync(1);
        Assert.Equal(OrderErrors.NoInvoice, (await returns.RefundAsync(unpaid.Number, new RecordRefundRequest(1, "Card", null, null))).Error!.Code);
        Assert.Equal(OrderErrors.NotFound, (await returns.RefundAsync(999_999, new RecordRefundRequest(1, "Card", null, null))).Error!.Code);
    }

    [Fact]
    public async Task Settings_defaults_validation_and_refund_share()
    {
        await using var db = Database.CreateContext();
        Assert.Equal(SalesSettingsService.Defaults, await new SalesSettingsService(db).GetAsync());
        var validator = new SalesSettingsViewValidator();
        Assert.False(validator.Validate(new SalesSettingsView(null, null, null, null, -1)).IsValid);
        Assert.False(validator.Validate(new SalesSettingsView(new string('x', 201), null, null, null, 14)).IsValid);
        Assert.True(validator.Validate(SalesSettingsService.Defaults).IsValid);

        Assert.False(new RequestReturnRequestValidator().Validate(new RequestReturnRequest([], "x")).IsValid);
        Assert.False(new RequestReturnRequestValidator().Validate(new RequestReturnRequest([new(Guid.NewGuid(), 1)], "")).IsValid);
        Assert.False(new RecordRefundRequestValidator().Validate(new RecordRefundRequest(0, "Card", null, null)).IsValid);
        Assert.False(new RecordRefundRequestValidator().Validate(new RecordRefundRequest(5, "", null, null)).IsValid);
        Assert.True(new ReturnAnswerRequestValidator().Validate(new ReturnAnswerRequest(null)).IsValid);

        // 100 of 200 goods, 20 discount: 90 paid back; rial amounts stay whole.
        Assert.Equal(90m, OrderQueries.SuggestedRefund(new Order { Subtotal = 200, Discount = 20, Currency = "EUR" }, 100));
        Assert.Equal(33_333m, OrderQueries.SuggestedRefund(new Order { Subtotal = 300_000, Discount = 200_000, Currency = "IRR" }, 100_000));
        Assert.Equal((false, true, true), OrderTransitions.ForReturn(ReturnStatus.Approved));
        Assert.Equal(2, CheckoutPricing.DefaultDecimals("EUR"));

        var data = new OrderMessageData(7, "1 EUR", null, null, new Uri("http://shop.test/en/order/7"), "R-3", "Please use the box");
        var mail = OrderMessages.Email(OrderNotice.ReturnApproved, "en", "a@example.com", "Shop", data);
        Assert.Equal("Return R-3 for order 7 at Shop is approved", mail.Subject);
        Assert.Contains("Please use the box", mail.HtmlBody, StringComparison.Ordinal);
    }
}
