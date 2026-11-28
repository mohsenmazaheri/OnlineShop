using System.Security.Claims;
using OnlineShop.Api.Common;
using OnlineShop.Api.Security;
using OnlineShop.Application.Customers;

namespace OnlineShop.Api.Endpoints;

/// <summary>
/// Web Push: whether the shop sends notifications (and the key browsers subscribe with), and a signed-in customer's
/// browsers switching them on or off.
/// </summary>
public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/push", (PushSubscriptionService push) => TypedResults.Ok(push.Settings()))
            .WithTags("Account")
            .WithName("GetPushSettings").WithSummary("Whether the shop sends notifications, and its VAPID public key for subscribing.");

        var mine = app.MapGroup("/api/v1/account/push").WithTags("Account").RequireAuthorization();
        mine.MapPut("/", async (PushSubscriptionRequest request, ClaimsPrincipal user, PushSubscriptionService push, CancellationToken ct) =>
                (await push.SubscribeAsync(user.GetUserId(), request, ct)).ToHttpResult())
            .Validate<PushSubscriptionRequest>()
            .WithName("SubscribePush").WithSummary("Switch notifications on for this browser (its push subscription).")
            .Produces<bool>();
        mine.MapPost("/remove", async (PushUnsubscribeRequest request, ClaimsPrincipal user, PushSubscriptionService push, CancellationToken ct) =>
                TypedResults.Ok(await push.UnsubscribeAsync(user.GetUserId(), request.Endpoint, ct)))
            .Validate<PushUnsubscribeRequest>()
            .WithName("UnsubscribePush").WithSummary("Switch notifications off for this browser.");

        return app;
    }
}
