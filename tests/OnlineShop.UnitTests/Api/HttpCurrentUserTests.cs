using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OnlineShop.Api.Security;

namespace OnlineShop.UnitTests.Api;

public sealed class HttpCurrentUserTests
{
    [Fact]
    public void Reads_the_user_id_only_from_a_signed_in_session()
    {
        var accessor = new HttpContextAccessor();
        Assert.Null(new HttpCurrentUser(accessor).UserId);

        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u-1")])),
        };
        Assert.Null(new HttpCurrentUser(accessor).UserId);

        accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u-1")], "cookie"));
        Assert.Equal("u-1", new HttpCurrentUser(accessor).UserId);
    }
}
