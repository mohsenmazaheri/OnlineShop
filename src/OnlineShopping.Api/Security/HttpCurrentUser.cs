using System.Security.Claims;
using OnlineShopping.Application.Common;

namespace OnlineShopping.Api.Security;

/// <summary>The signed-in user of the current request, from the session's claims (null when signed out).</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
        ? user.FindFirstValue(ClaimTypes.NameIdentifier)
        : null;
}
