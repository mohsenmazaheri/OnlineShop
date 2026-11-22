namespace OnlineShop.Application.Common;

/// <summary>
/// The signed-in user of the current request, for records that note who did something (a stock movement). Taken from
/// the session's claims, never from input. Null outside a request (startup seeding, background jobs, tests).
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
}

/// <summary>No user: the default outside the web API.</summary>
public sealed class NoCurrentUser : ICurrentUser
{
    public string? UserId => null;
}
