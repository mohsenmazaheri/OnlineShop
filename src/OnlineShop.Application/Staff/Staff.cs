using FluentValidation;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Authorization;
using OnlineShop.Domain.Staff;

namespace OnlineShop.Application.Staff;

/// <summary>Invite by email or mobile number (as the administrator types it).</summary>
public sealed record InviteStaffRequest(string Identifier, IReadOnlyList<string> Roles, string? Locale);

public sealed record ChangeStaffRolesRequest(IReadOnlyList<string> Roles);

public sealed record StaffMember(
    string UserId,
    string? DisplayName,
    string? Email,
    string? PhoneNumber,
    IReadOnlyList<string> Roles,
    bool TwoFactorEnabled,
    bool Suspended);

public sealed record StaffInvitationView(
    Guid Id,
    string? Email,
    string? PhoneNumber,
    IReadOnlyList<string> Roles,
    DateTime CreatedUtc,
    DateTime ExpiresUtc,
    StaffInvitationStatus Status);

public sealed record StaffOverview(IReadOnlyList<StaffMember> Members, IReadOnlyList<StaffInvitationView> Invitations);

/// <summary>An invitation waiting for the signed-in user (matched by their verified email or phone).</summary>
public sealed record MyStaffInvitation(Guid Id, IReadOnlyList<string> Roles, DateTime ExpiresUtc);

public sealed record StaffAuditView(
    Guid Id,
    DateTime CreatedUtc,
    string ActorUserId,
    string? ActorName,
    StaffAuditAction Action,
    string? TargetUserId,
    string? TargetName,
    string? TargetIdentifier,
    string? Details);

/// <summary>An account as staff management sees it (any account, staff or not).</summary>
public sealed record StaffAccount(
    string UserId,
    string? DisplayName,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    IReadOnlyList<string> Roles,
    bool TwoFactorEnabled,
    bool Suspended)
{
    public bool IsStaff => Roles.Any(r => AppRoles.Staff.Contains(r, StringComparer.Ordinal));

    public IReadOnlyList<string> StaffRoles => Roles.Where(r => AppRoles.Staff.Contains(r, StringComparer.Ordinal)).ToList();

    public string? Name => DisplayName ?? Email ?? PhoneNumber;
}

/// <summary>Identity operations for staff management (implemented with ASP.NET Core Identity in Infrastructure).</summary>
public interface IStaffIdentity
{
    /// <summary>Every account that has at least one staff role (erased accounts excluded).</summary>
    Task<IReadOnlyList<StaffAccount>> ListStaffAsync(CancellationToken cancellationToken = default);

    Task<StaffAccount?> FindAsync(string userId);

    /// <summary>Accounts whose email (any confirmation state) or phone equals the identifier.</summary>
    Task<StaffAccount?> FindByIdentifierAsync(LoginIdentifier identifier);

    /// <summary>
    /// Replaces the account's staff roles (other roles such as Customer stay). Open sessions keep going and pick up
    /// the new roles at their next periodic check (within a minute).
    /// </summary>
    Task SetStaffRolesAsync(string userId, IReadOnlyList<string> staffRoles);

    /// <summary>Suspended accounts cannot sign in by any method; their sessions end at the next check.</summary>
    Task SetSuspendedAsync(string userId, bool suspended);

    /// <summary>Accounts with the Admin role that are not suspended.</summary>
    Task<int> CountActiveAdminsAsync();

    /// <summary>Re-issues the current user's session so newly granted roles apply immediately.</summary>
    Task RefreshCurrentSessionAsync(string userId);
}

/// <summary>Stable error codes (translated by the web app under <c>admin.errors</c>).</summary>
public static class StaffErrors
{
    public const string IdentifierRequired = "staff.identifier_required";
    public const string IdentifierInvalid = "staff.identifier_invalid";
    public const string RolesRequired = "staff.roles_required";
    public const string RoleInvalid = "staff.role_invalid";
    public const string NotFound = "staff.not_found";
    public const string InvitationNotFound = "staff.invitation_not_found";
    public const string InvitationExists = "staff.invitation_exists";
    public const string AlreadyStaff = "staff.already_staff";
    public const string NotOnYourself = "staff.not_on_yourself";
    public const string LastAdmin = "staff.last_admin";
    public const string AlreadySuspended = "staff.already_suspended";
    public const string NotSuspended = "staff.not_suspended";
}

internal static class StaffRoleRules
{
    public static IRuleBuilderOptions<T, IReadOnlyList<string>> ValidStaffRoles<T>(this IRuleBuilder<T, IReadOnlyList<string>> rule) =>
        rule.NotNull().WithErrorCode(StaffErrors.RolesRequired)
            .Must(r => r is { Count: > 0 }).WithErrorCode(StaffErrors.RolesRequired)
            .Must(r => r is null || r.All(role => AppRoles.Staff.Contains(role, StringComparer.Ordinal))).WithErrorCode(StaffErrors.RoleInvalid);
}

public sealed class InviteStaffRequestValidator : AbstractValidator<InviteStaffRequest>
{
    public InviteStaffRequestValidator()
    {
        RuleFor(r => r.Identifier).NotEmpty().WithErrorCode(StaffErrors.IdentifierRequired)
            .MaximumLength(EmailRules.MaxLength).WithErrorCode(StaffErrors.IdentifierInvalid);
        RuleFor(r => r.Roles).ValidStaffRoles();
    }
}

public sealed class ChangeStaffRolesRequestValidator : AbstractValidator<ChangeStaffRolesRequest>
{
    public ChangeStaffRolesRequestValidator()
    {
        RuleFor(r => r.Roles).ValidStaffRoles();
    }
}
