using System.Security.Claims;
using OnlineShop.Api.Common;
using OnlineShop.Api.Security;
using OnlineShop.Application.Authorization;
using OnlineShop.Application.Common;
using OnlineShop.Application.Sales;
using OnlineShop.Domain.Sales;

namespace OnlineShop.Api.Endpoints;

/// <summary>
/// Orders: a customer's own (others' numbers answer 404), a guest's through the signed link of its emails, and the
/// staff's list, details and status changes (orders.view to read, orders.manage to change).
/// </summary>
public static class OrderEndpoints
{
    private static IResult NotFound() => ResultExtensions.ToProblem(AppError.NotFound(OrderErrors.NotFound, "Order not found."));

    private static IResult Answer(OrderView? view) => view is null ? NotFound() : TypedResults.Ok(view);

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("/api/v1/account/orders").WithTags("Account").RequireAuthorization();
        mine.MapGet("/", async (int? page, ClaimsPrincipal user, OrderQueries orders, CancellationToken ct) =>
                TypedResults.Ok(await orders.ListForCustomerAsync(user.GetUserId(), page ?? 1, ct)))
            .WithName("ListMyOrders").WithSummary("The signed-in customer's orders, the newest first.");
        mine.MapGet("/{number:long}", async (long number, ClaimsPrincipal user, OrderQueries orders, CancellationToken ct) =>
                Answer(await orders.GetForCustomerAsync(user.GetUserId(), number, ct)))
            .WithName("GetMyOrder").WithSummary("One of the customer's orders with its history (another customer's number answers 404).")
            .Produces<OrderView>().ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/orders/{number:long}", async (long number, string? key, OrderQueries orders, CancellationToken ct) =>
                Answer(await orders.GetWithTokenAsync(number, key ?? "", ct)))
            .WithTags("Orders")
            .WithName("GetOrderByLink").WithSummary("An order through the signed link of its emails (guest orders); a wrong key answers 404.")
            .Produces<OrderView>().ProducesProblem(StatusCodes.Status404NotFound);

        var admin = app.MapGroup("/api/v1/admin/orders").WithTags("Admin: orders")
            .RequireAuthorization(Permissions.Policy(Permissions.OrdersView));
        admin.MapGet("/", async (OrderStatus? status, string? q, int? page, OrderQueries orders, CancellationToken ct) =>
                TypedResults.Ok(await orders.ListAsync(new OrderListQuery(status, q, page ?? 1), ct)))
            .WithName("ListOrders").WithSummary("Every order, the newest first, by status and a search over number, name, email and phone.");
        admin.MapGet("/{number:long}", async (long number, OrderQueries orders, CancellationToken ct) =>
                Answer(await orders.GetForStaffAsync(number, ct)))
            .WithName("GetOrder").WithSummary("One order with its full history, internal notes included.")
            .Produces<OrderView>().ProducesProblem(StatusCodes.Status404NotFound);

        var manage = admin.MapGroup("/{number:long}").RequireAuthorization(Permissions.Policy(Permissions.OrdersManage));
        manage.MapPost("/paid", async (long number, OrderActionRequest request, OrderLifecycleService lifecycle, CancellationToken ct) =>
                (await lifecycle.MarkPaidAsync(number, request, ct)).ToHttpResult())
            .Validate<OrderActionRequest>()
            .WithName("MarkOrderPaid").WithSummary("Record a payment received by hand: the held stock is sold.").Produces<OrderStatus>();
        manage.MapPost("/ship", async (long number, ShipOrderRequest request, OrderLifecycleService lifecycle, CancellationToken ct) =>
                (await lifecycle.ShipAsync(number, request, ct)).ToHttpResult())
            .Validate<ShipOrderRequest>()
            .WithName("ShipOrder").WithSummary("Mark a paid order shipped with its carrier and tracking code; the customer is told.").Produces<OrderStatus>();
        manage.MapPost("/complete", async (long number, OrderActionRequest request, OrderLifecycleService lifecycle, CancellationToken ct) =>
                (await lifecycle.CompleteAsync(number, request, ct)).ToHttpResult())
            .Validate<OrderActionRequest>()
            .WithName("CompleteOrder").WithSummary("Mark a shipped order delivered (or a paid pickup order handed over).").Produces<OrderStatus>();
        manage.MapPost("/cancel", async (long number, OrderActionRequest request, OrderLifecycleService lifecycle, CancellationToken ct) =>
                (await lifecycle.CancelAsync(number, request, ct)).ToHttpResult())
            .Validate<OrderActionRequest>()
            .WithName("CancelOrder").WithSummary("Cancel an unpaid order: its stock and discount code use are given back; the text is shown to the customer.").Produces<OrderStatus>();
        manage.MapPost("/notes", async (long number, OrderNoteRequest request, OrderLifecycleService lifecycle, CancellationToken ct) =>
                (await lifecycle.AddNoteAsync(number, request, ct)).ToHttpResult())
            .Validate<OrderNoteRequest>()
            .WithName("AddOrderNote").WithSummary("Add a note to the order's history for staff only.");

        return app;
    }
}
