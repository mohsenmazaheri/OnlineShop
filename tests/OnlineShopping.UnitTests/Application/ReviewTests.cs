using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog;
using OnlineShopping.Application.Catalog.Browse;
using OnlineShopping.Application.Catalog.Reviews;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Customers;
using OnlineShopping.Application.Search;
using OnlineShopping.Domain.Catalog;
using OnlineShopping.Domain.Sales;
using OnlineShopping.Infrastructure.Identity;
using OnlineShopping.Infrastructure.Persistence;
using OnlineShopping.UnitTests.TestSupport;

namespace OnlineShopping.UnitTests.Application;

public sealed class ReviewServiceTests : DemoCatalogTestBase
{
    private sealed class Moderator(string? id) : ICurrentUser
    {
        public string? UserId => id;
    }

    private readonly FakeAccountIdentity _accounts = new();

    private ReviewService Service(AppDbContext db, string? moderator = null) => new(db, _accounts, new Moderator(moderator), Database.Clock);

    private static ReviewInput Input(int rating, string body = "Works well and looks good.", string? title = null) => new(rating, title, body);

    /// <summary>An account row (reviews point at users) and the identity's view of it.</summary>
    private async Task AddCustomerAsync(string id, bool confirmed = true)
    {
        await using (var db = Database.CreateContext())
        {
            db.Users.Add(new AppUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() });
            await db.SaveChangesAsync();
        }
        _accounts.Users.Add(new FakeAccountIdentity.User(id, $"{id}@test.local", null, "x", "en") { EmailConfirmed = confirmed });
    }

    [Fact]
    public async Task Reviews_wait_for_approval_and_only_approved_ones_count_in_the_rating()
    {
        await SeedDemoAsync();
        await AddCustomerAsync("u-1");
        await AddCustomerAsync("u-2");
        var number = await NumberOfAsync("DEMO-PHN-001");
        await using var db = Database.CreateContext();
        var reviews = Service(db, moderator: "staff-1");

        var mine = (await reviews.SubmitAsync("u-1", number, Input(5, title: "  Great  "), "en")).Value;
        Assert.Equal((ReviewStatus.Pending, "Great"), (mine.Status, mine.Title));
        Assert.Empty((await reviews.ListAsync(number, 1)).Value.Items);
        await reviews.SubmitAsync("u-2", number, Input(2), "fa");

        var queue = await reviews.AdminListAsync(ReviewStatus.Pending, 1);
        Assert.Equal(2, queue.PendingCount);
        // The oldest waiting first.
        Assert.Equal([5, 2], queue.Items.Select(i => i.Rating));
        foreach (var item in queue.Items)
            Assert.True((await reviews.ModerateAsync(item.Id, ReviewStatus.Approved)).IsSuccess);

        var page = (await reviews.ListAsync(number, 1)).Value;
        Assert.Equal((3.5m, 2), (page.Summary.Average, page.Summary.Count));
        Assert.Equal([1, 0, 0, 1, 0], page.Summary.Stars);
        Assert.Equal(2, page.Total);
        Assert.Equal("fa", page.Items[0].Locale);
        var stored = await db.ProductReviews.AsNoTracking().FirstAsync(r => r.CustomerId == "u-1");
        Assert.Equal("staff-1", stored.ModeratedBy);

        // Writing again replaces the review and sends it back to moderation: it leaves the rating meanwhile.
        await reviews.SubmitAsync("u-1", number, Input(4), "en");
        Assert.Equal(1, await db.ProductReviews.CountAsync(r => r.CustomerId == "u-1"));
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.PublicNumber == number);
        Assert.Equal((2m, 1), (product.RatingAverage, product.RatingCount));

        var rejected = queue.Items.Single(i => i.Rating == 2).Id;
        await reviews.ModerateAsync(rejected, ReviewStatus.Rejected);
        product = await db.Products.AsNoTracking().SingleAsync(p => p.PublicNumber == number);
        Assert.Equal((0m, 0), (product.RatingAverage, product.RatingCount));
        Assert.Equal(ReviewErrors.StatusInvalid, (await reviews.ModerateAsync(rejected, ReviewStatus.Pending)).Error!.Code);
        Assert.Equal(ReviewErrors.NotFound, (await reviews.ModerateAsync(Guid.NewGuid(), ReviewStatus.Approved)).Error!.Code);
        Assert.Single((await reviews.AdminListAsync(ReviewStatus.Rejected, 1)).Items);
        Assert.Equal(2, (await reviews.AdminListAsync(null, 1)).Total);
    }

    [Fact]
    public async Task Unconfirmed_accounts_links_and_floods_are_refused()
    {
        await SeedDemoAsync();
        await AddCustomerAsync("u-1");
        await AddCustomerAsync("u-2", confirmed: false);
        var numbers = new List<long>();
        foreach (var sku in new[] { "DEMO-PHN-001", "DEMO-PHN-002", "DEMO-PHN-003", "DEMO-LAP-001", "DEMO-LAP-002", "DEMO-AUD-001" })
            numbers.Add(await NumberOfAsync(sku));
        await using var db = Database.CreateContext();
        var reviews = Service(db);

        Assert.Equal(ReviewErrors.UnverifiedAccount, (await reviews.SubmitAsync("u-2", numbers[0], Input(5), "en")).Error!.Code);
        Assert.False((await reviews.MineAsync("u-2", numbers[0])).Value.CanReview);
        Assert.Equal(ReviewErrors.LinksNotAllowed, (await reviews.SubmitAsync("u-1", numbers[0], Input(5, "Buy it at www.cheap.example now"), "en")).Error!.Code);
        Assert.Equal(ReviewErrors.NotFound, (await reviews.SubmitAsync("u-1", 999_999, Input(5), "en")).Error!.Code);

        for (var i = 0; i < ReviewService.MaxPerHour; i++)
            Assert.True((await reviews.SubmitAsync("u-1", numbers[i], Input(4), "en")).IsSuccess);
        Assert.Equal(ReviewErrors.TooMany, (await reviews.SubmitAsync("u-1", numbers[5], Input(4), "en")).Error!.Code);
        Database.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.True((await reviews.SubmitAsync("u-1", numbers[5], Input(4), "en")).IsSuccess);
    }

    [Theory]
    [InlineData("See https://example.com", true)]
    [InlineData("visit shop.ir today", true)]
    [InlineData("WWW.Example.org", true)]
    [InlineData("Battery lasts 1.5 days, version 5.3", false)]
    [InlineData("عالی بود، پیشنهاد می‌کنم", false)]
    public void Links_are_recognised(string text, bool link) => Assert.Equal(link, ReviewService.HasLink(text));

    [Fact]
    public void The_input_needs_one_to_five_stars_and_a_real_text()
    {
        var validator = new ReviewInputValidator();
        Assert.True(validator.Validate(Input(5)).IsValid);
        Assert.Contains(validator.Validate(Input(0)).Errors, e => e.ErrorCode == ReviewErrors.RatingInvalid);
        Assert.Contains(validator.Validate(Input(3, "too short")).Errors, e => e.ErrorCode == ReviewErrors.BodyRequired);
        Assert.Contains(validator.Validate(Input(3, new string('x', 2001))).Errors, e => e.ErrorCode == ReviewErrors.BodyTooLong);
        Assert.Contains(validator.Validate(Input(3, title: new string('x', 121))).Errors, e => e.ErrorCode == ReviewErrors.TitleTooLong);
    }

    [Fact]
    public async Task Buyers_are_marked_and_customers_manage_their_own_review()
    {
        await SeedDemoAsync();
        await AddCustomerAsync("u-1");
        var number = await NumberOfAsync("DEMO-PHN-001");
        await using var db = Database.CreateContext();
        var productId = await db.Products.Where(p => p.PublicNumber == number).Select(p => p.Id).SingleAsync();
        db.Orders.Add(new Order
        {
            CustomerId = "u-1", OrderedUtc = Database.Clock.GetUtcNow().UtcDateTime, Subtotal = 10, Total = 10, Status = OrderStatus.Paid,
            Items = { new OrderItem { ProductId = productId, ProductName = "Phone", UnitPrice = 10, Quantity = 1 } },
        });
        await db.SaveChangesAsync();
        var reviews = Service(db);

        Assert.Equal(new MyReviewState(true, true, null), (await reviews.MineAsync("u-1", number)).Value);
        await reviews.SubmitAsync("u-1", number, Input(5), "en");
        Assert.True((await db.ProductReviews.AsNoTracking().SingleAsync()).VerifiedBuyer);
        Assert.Equal(5, (await reviews.MineAsync("u-1", number)).Value.Review!.Rating);

        Assert.True((await reviews.DeleteMineAsync("u-1", number)).IsSuccess);
        Assert.Null((await reviews.MineAsync("u-1", number)).Value.Review);
        Assert.Equal(ReviewErrors.NotFound, (await reviews.DeleteMineAsync("u-1", number)).Error!.Code);
        Assert.Equal(ReviewErrors.NotFound, (await reviews.ListAsync(999_999, 1)).Error!.Code);
    }

    [Fact]
    public async Task Listings_sort_by_rating()
    {
        await SeedDemoAsync();
        await AddCustomerAsync("u-1");
        await AddCustomerAsync("u-2");
        await IndexSearchAsync();
        var low = await NumberOfAsync("DEMO-PHN-001");
        var high = await NumberOfAsync("DEMO-PHN-002");
        await using var db = Database.CreateContext();
        var reviews = Service(db);
        await reviews.SubmitAsync("u-1", low, Input(2), "en");
        await reviews.SubmitAsync("u-2", high, Input(5), "en");
        foreach (var item in (await reviews.AdminListAsync(ReviewStatus.Pending, 1)).Items)
            await reviews.ModerateAsync(item.Id, ReviewStatus.Approved);

        var browse = new ProductBrowseService(db, new ProductQueries(db), new ProductTextSearch(db, new SimpleProductSearch(db)), new SearchTermLog(db, Database.Clock), Options.Create(Store));
        var result = (await browse.BrowseAsync(new BrowseQuery { Sort = BrowseSort.Rating }, "en")).Value;
        Assert.Equal([high, low], result.Items.Take(2).Select(i => i.Number));
        Assert.Equal((5m, 1), (result.Items[0].RatingAverage, result.Items[0].RatingCount));
        Assert.Equal(BrowseSort.Rating, BrowseQueryParser.Parse([new("sort", "rating")]).Sort);
    }
}

public sealed class WishlistServiceTests : DemoCatalogTestBase
{
    private async Task AddUserAsync(string id)
    {
        await using var db = Database.CreateContext();
        db.Users.Add(new AppUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_customer_saves_published_products_of_their_own()
    {
        await SeedDemoAsync();
        await AddUserAsync("u-1");
        await AddUserAsync("u-2");
        var first = await NumberOfAsync("DEMO-PHN-001");
        var second = await NumberOfAsync("DEMO-FSH-003");
        await using var db = Database.CreateContext();
        var lists = new WishlistService(db, new ProductQueries(db), Database.Clock);

        await lists.AddAsync("u-1", first);
        Database.Clock.Advance(TimeSpan.FromMinutes(1));
        var ids = (await lists.AddAsync("u-1", second)).Value;
        // Newest first; adding twice changes nothing.
        Assert.Equal([second, first], ids.Numbers);
        Assert.Equal([second, first], (await lists.AddAsync("u-1", first)).Value.Numbers);
        Assert.Empty((await lists.IdsAsync("u-2")).Numbers);
        Assert.Equal(WishlistService.NotFoundCode, (await lists.AddAsync("u-1", 999_999)).Error!.Code);

        var cards = await lists.ListAsync("u-1", "en");
        Assert.Equal([second, first], cards.Select(c => c.Number));

        await db.Products.Where(p => p.PublicNumber == first).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPublished, false));
        Assert.Equal([second], (await lists.IdsAsync("u-1")).Numbers);
        Assert.Empty((await lists.RemoveAsync("u-1", second)).Numbers);
        Assert.Equal(1, await db.WishlistItems.CountAsync(w => w.CustomerId == "u-1"));
    }
}
