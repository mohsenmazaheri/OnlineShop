using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Inventory;

namespace OnlineShopping.UnitTests.TestSupport;

/// <summary>The stock ledger as the services get it, without a signed-in user unless one is given.</summary>
public static class TestLedger
{
    public static StockLedger For(IApplicationDbContext db, StoreOptions store, string? userId = null) =>
        new(db, new FixedCurrentUser(userId), Options.Create(store));
}

/// <summary>A fixed "signed-in user" for services that record who did something.</summary>
public sealed class FixedCurrentUser(string? userId) : ICurrentUser
{
    public string? UserId => userId;
}
