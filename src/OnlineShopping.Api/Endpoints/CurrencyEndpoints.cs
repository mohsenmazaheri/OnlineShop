using OnlineShopping.Api.Common;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Application.Pricing;

namespace OnlineShopping.Api.Endpoints;

/// <summary>Display currencies: the public list for the storefront and the admin's management (settings.manage).</summary>
public static class CurrencyEndpoints
{
    public static IEndpointRouteBuilder MapCurrencyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/currencies", async (CurrencyService currencies, CancellationToken ct) => TypedResults.Ok(await currencies.GetPublicAsync(ct)))
            .WithTags("Storefront")
            .WithName("GetCurrencies")
            .WithSummary("The base currency, the currencies a visitor may show prices in (with rates) and each language's starting currency.");

        var admin = app.MapGroup("/api/v1/admin/currencies").WithTags("Admin: currencies")
            .RequireAuthorization(Permissions.Policy(Permissions.SettingsManage));

        admin.MapGet("/", async (CurrencyService currencies, CancellationToken ct) => TypedResults.Ok(await currencies.GetAdminAsync(ct)))
            .WithName("GetCurrencyAdmin")
            .WithSummary("All currencies with rates and settings, the language defaults and the automatic rate source.");

        admin.MapPost("/", async (SaveCurrencyRequest request, CurrencyService currencies, CancellationToken ct) =>
                (await currencies.CreateAsync(request, ct)).ToHttpResult())
            .Validate<SaveCurrencyRequest>()
            .WithName("CreateCurrency")
            .WithSummary("Add a display currency (ISO code or a shop-defined one such as IRT for toman).")
            .Produces<CurrencyView>();

        admin.MapPut("/{code}", async (string code, SaveCurrencyRequest request, CurrencyService currencies, CancellationToken ct) =>
                (await currencies.UpdateAsync(code, request, ct)).ToHttpResult())
            .Validate<SaveCurrencyRequest>()
            .WithName("UpdateCurrency")
            .WithSummary("Change a currency's symbol, decimals, rounding, rate, automatic updates or availability.")
            .Produces<CurrencyView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapPut("/locales/{locale}", async (string locale, SetLocaleCurrencyRequest request, CurrencyService currencies, CancellationToken ct) =>
                (await currencies.SetLocaleDefaultAsync(locale, request, ct)).ToHttpResult())
            .Validate<SetLocaleCurrencyRequest>()
            .WithName("SetLocaleCurrency")
            .WithSummary("Choose the currency a language starts with.")
            .Produces<LocaleCurrencyView>();

        admin.MapPost("/sync", async (CurrencyService currencies, CancellationToken ct) => (await currencies.SyncRatesAsync(ct)).ToHttpResult())
            .WithName("SyncCurrencyRates")
            .WithSummary("Fetch the automatic rates now (also done in the background every few hours).")
            .Produces<RateSyncResult>();

        return app;
    }
}
