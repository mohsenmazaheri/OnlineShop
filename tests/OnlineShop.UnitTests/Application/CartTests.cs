using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Sales;
using OnlineShop.Infrastructure.Identity;
using OnlineShop.Infrastructure.Persistence;

namespace OnlineShop.UnitTests.Application;

public sealed class CartServiceTests : DemoCatalogTestBase
{
    private CartService Service(AppDbContext db) => new(db, Options.Create(Store));

    private async Task AddUserAsync(string id)
    {
        await using var db = Database.CreateContext();
        db.Users.Add(new AppUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_guest_adds_products_and_variants_with_current_prices_and_stock_limits()
    {
        await SeedDemoAsync();
        var phone = await NumberOfAsync("DEMO-PHN-001");
        var hoodie = await NumberOfAsync("DEMO-FSH-003");
        await using var db = Database.CreateContext();
        var carts = Service(db);
        var guest = new CartOwner(null, CartService.NewGuestToken());

        var view = (await carts.AddAsync(guest, new AddToCartRequest(phone, null, 2), "en")).Value;
        var line = Assert.Single(view.Lines);
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.PublicNumber == phone);
        Assert.Equal((2, product.Price, product.Price * 2, CartLineProblem.None), (line.Quantity, line.UnitPrice, line.LineTotal, line.Problem));
        Assert.Equal((2, product.Price * 2, "EUR"), (view.ItemCount, view.Subtotal, view.Currency));
        // Adding again adds up on the same line.
        Assert.Equal(3, Assert.Single((await carts.AddAsync(guest, new AddToCartRequest(phone, null, 1), "en")).Value.Lines).Quantity);

        Assert.Equal(CartErrors.VariantRequired, (await carts.AddAsync(guest, new AddToCartRequest(hoodie, null, 1), "en")).Error!.Code);
        var withVariant = (await carts.AddAsync(guest, new AddToCartRequest(hoodie, "DEMO-FSH-003-BG-M", 1), "en")).Value;
        var variantLine = withVariant.Lines.Single(l => l.VariantSku == "DEMO-FSH-003-BG-M");
        Assert.Equal("Beige / M", variantLine.VariantLabel);
        Assert.Equal(CartErrors.NotEnoughStock, (await carts.AddAsync(guest, new AddToCartRequest(hoodie, "DEMO-FSH-003-GR-XL", 1), "en")).Error!.Code);
        Assert.Equal(CartErrors.ProductNotFound, (await carts.AddAsync(guest, new AddToCartRequest(999_999, null, 1), "en")).Error!.Code);
        Assert.Equal(CartErrors.NotEnoughStock, (await carts.SetQuantityAsync(guest, line.Id, product.Stock + 1, "en")).Error!.Code);

        // Another guest sees nothing of it.
        Assert.Empty((await carts.GetAsync(new CartOwner(null, CartService.NewGuestToken()), "en")).Lines);
        Assert.Equal(CartErrors.LineNotFound, (await carts.SetQuantityAsync(new CartOwner(null, CartService.NewGuestToken()), line.Id, 1, "en")).Error!.Code);

        var removed = (await carts.SetQuantityAsync(guest, line.Id, 0, "en")).Value;
        Assert.Equal(["DEMO-FSH-003-BG-M"], removed.Lines.Select(l => l.VariantSku));
        // Only the hash of the token is stored.
        Assert.DoesNotContain(await db.Carts.Select(c => c.GuestTokenHash).ToListAsync(), h => h == guest.GuestToken);
    }

    [Fact]
    public async Task Lines_that_changed_are_marked_and_left_out_of_the_subtotal()
    {
        await SeedDemoAsync();
        await AddUserAsync("u-1");
        var phone = await NumberOfAsync("DEMO-PHN-001");
        var laptop = await NumberOfAsync("DEMO-LAP-001");
        await using var db = Database.CreateContext();
        var carts = Service(db);
        var me = new CartOwner("u-1", null);
        await carts.AddAsync(me, new AddToCartRequest(phone, null, 2), "en");
        await carts.AddAsync(me, new AddToCartRequest(laptop, null, 1), "en");

        await db.Products.Where(p => p.PublicNumber == phone).ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Stock - 1));
        await db.Products.Where(p => p.PublicNumber == laptop).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPublished, false));
        var view = await carts.GetAsync(me, "en");

        Assert.True(view.HasProblems);
        Assert.Equal((CartLineProblem.NotEnoughStock, 1, 0m), (view.Lines[0].Problem, view.Lines[0].Available, view.Lines[0].LineTotal));
        Assert.Equal(CartLineProblem.Unavailable, view.Lines[1].Problem);
        Assert.Equal(0, view.Subtotal);
        // Lowering is always allowed.
        Assert.False((await carts.SetQuantityAsync(me, view.Lines[0].Id, 1, "en")).Value.Lines[0].Problem != CartLineProblem.None);
    }

    [Fact]
    public async Task A_guest_cart_moves_into_the_account_after_signing_in()
    {
        await SeedDemoAsync();
        await AddUserAsync("u-1");
        await AddUserAsync("u-2");
        var phone = await NumberOfAsync("DEMO-PHN-001");
        var laptop = await NumberOfAsync("DEMO-LAP-001");
        await using var db = Database.CreateContext();
        var carts = Service(db);

        // No account cart yet: the guest cart simply becomes it.
        var first = CartService.NewGuestToken();
        await carts.AddAsync(new CartOwner(null, first), new AddToCartRequest(phone, null, 1), "en");
        await carts.MergeAsync("u-1", first);
        Assert.Single((await carts.GetAsync(new CartOwner("u-1", null), "en")).Lines);
        Assert.Empty((await carts.GetAsync(new CartOwner(null, first), "en")).Lines);

        // Both have carts: quantities add up, the guest cart is gone.
        var second = CartService.NewGuestToken();
        await carts.AddAsync(new CartOwner(null, second), new AddToCartRequest(phone, null, 2), "en");
        await carts.AddAsync(new CartOwner(null, second), new AddToCartRequest(laptop, null, 1), "en");
        await carts.MergeAsync("u-1", second);
        var mine = await carts.GetAsync(new CartOwner("u-1", null), "en");
        Assert.Equal([(phone, 3), (laptop, 1)], mine.Lines.Select(l => (l.Number, l.Quantity)));
        Assert.Equal(1, await db.Carts.CountAsync());
        await carts.MergeAsync("u-1", "unknown token");
        Assert.Empty((await carts.GetAsync(new CartOwner("u-2", null), "en")).Lines);
        Assert.Throws<InvalidOperationException>(() => Service(db).AddAsync(new CartOwner(null, null), new AddToCartRequest(phone, null, 1), "en").GetAwaiter().GetResult());
    }

    [Fact]
    public void Requests_are_validated_and_tokens_are_long_and_random()
    {
        Assert.False(new AddToCartRequestValidator().Validate(new AddToCartRequest(1, null, 0)).IsValid);
        Assert.False(new AddToCartRequestValidator().Validate(new AddToCartRequest(1, null, CartService.MaxQuantity + 1)).IsValid);
        Assert.True(new SetQuantityRequestValidator().Validate(new SetQuantityRequest(0)).IsValid);
        var a = CartService.NewGuestToken();
        Assert.True(a.Length >= 43);
        Assert.NotEqual(a, CartService.NewGuestToken());
        Assert.Equal(64, CartService.HashToken(a).Length);
    }
}
