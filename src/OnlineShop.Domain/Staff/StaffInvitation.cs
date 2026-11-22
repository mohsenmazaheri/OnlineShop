using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Staff;

public enum StaffInvitationStatus
{
    Pending,
    Accepted,
    Revoked,
    Expired
}

/// <summary>
/// An administrator invites someone, by email or mobile number, to work in the back office with the given roles.
/// The invitation is accepted by a signed-in account whose <em>verified</em> email or phone matches; nothing is
/// granted before that.
/// </summary>
public class StaffInvitation : BaseEntity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>Email as typed (trimmed); null for a phone invitation.</summary>
    public string? Email { get; set; }

    /// <summary>Upper-case email for case-insensitive matching (like Identity's NormalizedEmail).</summary>
    public string? NormalizedEmail { get; set; }

    /// <summary>E.164; null for an email invitation.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Staff role names, comma-separated (e.g. <c>Manager,Warehouse</c>).</summary>
    public required string Roles { get; set; }

    public required string InvitedByUserId { get; set; }

    public DateTime ExpiresUtc { get; set; }

    public DateTime? AcceptedUtc { get; set; }

    public string? AcceptedByUserId { get; set; }

    public DateTime? RevokedUtc { get; set; }

    public IReadOnlyList<string> RoleList => Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public StaffInvitationStatus StatusAt(DateTime nowUtc) =>
        AcceptedUtc is not null ? StaffInvitationStatus.Accepted
        : RevokedUtc is not null ? StaffInvitationStatus.Revoked
        : nowUtc >= ExpiresUtc ? StaffInvitationStatus.Expired
        : StaffInvitationStatus.Pending;
}
