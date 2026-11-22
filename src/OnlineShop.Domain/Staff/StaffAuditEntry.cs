using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Staff;

public enum StaffAuditAction
{
    Invited,
    InvitationRevoked,
    InvitationAccepted,
    RolesChanged,
    Suspended,
    Reactivated,
    RemovedFromStaff
}

/// <summary>Append-only record of a change to who may work in the back office (who did what, to whom, when).</summary>
public class StaffAuditEntry : BaseEntity
{
    public required string ActorUserId { get; set; }

    public StaffAuditAction Action { get; set; }

    /// <summary>The account the change applies to; null while it is still an invitation to an email/phone.</summary>
    public string? TargetUserId { get; set; }

    /// <summary>Email or phone of an invitation (what the administrator typed).</summary>
    public string? TargetIdentifier { get; set; }

    /// <summary>Short machine-readable detail, e.g. the roles after the change.</summary>
    public string? Details { get; set; }
}
