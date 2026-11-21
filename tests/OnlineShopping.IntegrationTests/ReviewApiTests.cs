using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineShopping.Application.Accounts;
using OnlineShopping.Application.Catalog;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.Application.Catalog.Reviews;
using OnlineShopping.Application.Customers;
using OnlineShopping.Domain.Catalog;

namespace OnlineShopping.IntegrationTests;

/// <summary>Reviews (write, moderate, list, rating) and the wishlist on PostgreSQL.</summary>
public sealed class ReviewApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [Fact]
    public async Task A_review_appears_after_approval_and_the_wishlist_keeps_products()
    {
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/v1/account/login",
            new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        var word = Guid.NewGuid().ToString("N")[..6];
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories",
            new SaveCatalogNodeRequest([new("fa", $"نظر {word}", null, null), new("en", $"Reviews {word}", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories",
            new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups",
            new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        var product = await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
            $"REV-{word}", 10, null, 1, true, group.Id, [new("fa", $"کالا {word}", null, null), new("en", $"Item {word}", null, null)], [])));
        var number = product.Number;

        using var visitor = factory.CreateClient();
        Assert.False((await ReadAsync<MyReviewState>(await visitor.GetAsync($"/api/v1/account/reviews/{number}"))).SignedIn);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PutAsJsonAsync($"/api/v1/account/reviews/{number}", new ReviewInput(5, null, "Very good item indeed."))).StatusCode);
        var invalid = await admin.PutAsJsonAsync($"/api/v1/account/reviews/{number}", new ReviewInput(9, null, "short"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var mine = await ReadAsync<MyReview>(await admin.PutAsJsonAsync($"/api/v1/account/reviews/{number}?locale=en", new ReviewInput(4, "Nice", "Very good item indeed.")));
        Assert.Equal(ReviewStatus.Pending, mine.Status);
        Assert.Empty((await ReadAsync<ProductReviews>(await visitor.GetAsync($"/api/v1/catalog/products/{number}/reviews"))).Items);

        var queue = await ReadAsync<ReviewAdminList>(await admin.GetAsync("/api/v1/admin/reviews?status=Pending"));
        var waiting = Assert.Single(queue.Items, i => i.ProductNumber == number);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/v1/admin/reviews")).StatusCode);
        Assert.Equal(ReviewStatus.Approved,
            (await ReadAsync<ReviewAdminRow>(await admin.PutAsJsonAsync($"/api/v1/admin/reviews/{waiting.Id}", new ModerateReviewRequest(ReviewStatus.Approved)))).Status);

        var reviews = await ReadAsync<ProductReviews>(await visitor.GetAsync($"/api/v1/catalog/products/{number}/reviews"));
        Assert.Equal((4m, 1), (reviews.Summary.Average, reviews.Summary.Count));
        Assert.Equal([0, 1, 0, 0, 0], reviews.Summary.Stars);
        Assert.Equal("Nice", Assert.Single(reviews.Items).Title);
        var detail = await ReadAsync<ProductDetail>(await visitor.GetAsync($"/api/v1/catalog/products/{number}?locale=en"));
        Assert.Equal((4m, 1), (detail.RatingAverage, detail.RatingCount));

        Assert.False((await ReadAsync<WishlistIds>(await visitor.GetAsync("/api/v1/account/wishlist/ids"))).SignedIn);
        Assert.Equal([number], (await ReadAsync<WishlistIds>(await admin.PutAsync($"/api/v1/account/wishlist/{number}", null))).Numbers);
        Assert.Equal($"Item {word}", Assert.Single(await ReadAsync<List<ProductCard>>(await admin.GetAsync("/api/v1/account/wishlist?locale=en"))).Name);
        Assert.Empty((await ReadAsync<WishlistIds>(await admin.DeleteAsync($"/api/v1/account/wishlist/{number}"))).Numbers);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/v1/account/reviews/{number}")).StatusCode);
        Assert.Equal(0, (await ReadAsync<ProductDetail>(await visitor.GetAsync($"/api/v1/catalog/products/{number}?locale=en"))).RatingCount);
    }
}
