namespace OnlineShop.Application.Notifications;

/// <summary>A notification as the service worker shows it: title, text and the page it opens (relative to the shop).</summary>
public sealed record PushMessage(string Title, string Body, string Url);

/// <summary>Where and how to encrypt for one browser.</summary>
public sealed record PushTarget(string Endpoint, string P256dh, string Auth);

public enum PushResult
{
    Sent,

    /// <summary>The push service says the browser withdrew the subscription (404/410): forget it.</summary>
    Gone,

    /// <summary>Anything else (the push service is down, refused the message): try again with the next message.</summary>
    Failed,
}

/// <summary>
/// Sends Web Push messages (RFC 8030) through the browser vendors' push services, encrypted for each browser (RFC 8291)
/// and signed with the shop's VAPID key (RFC 8292). Optional: off until the shop has VAPID keys.
/// </summary>
public interface IWebPushSender
{
    bool IsEnabled { get; }

    /// <summary>The shop's VAPID public key (base64url), which browsers need to subscribe; null while off.</summary>
    string? PublicKey { get; }

    Task<PushResult> SendAsync(PushTarget target, PushMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Default when nothing else is registered: no Web Push.</summary>
public sealed class NoWebPushSender : IWebPushSender
{
    public bool IsEnabled => false;

    public string? PublicKey => null;

    public Task<PushResult> SendAsync(PushTarget target, PushMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult(PushResult.Failed);
}
