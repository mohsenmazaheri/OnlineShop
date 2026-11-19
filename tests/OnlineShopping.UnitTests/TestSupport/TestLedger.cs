using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Inventory;

namespace OnlineShopping.UnitTests.TestSupport;

/// <summary>The stock ledger as the services get it, without a signed-in user unless one is given.</summary>
public static class TestLedger
{
    public static StockLedger For(IApplicationDbContext db, StoreOptions store, string? userId = null) =>
        new(db, new FixedUser(userId), Options.Create(store));

    private sealed class FixedUser(string? userId) : ICurrentUser
    {
        public string? UserId => userId;
    }
}
