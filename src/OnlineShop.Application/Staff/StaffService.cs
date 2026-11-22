using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Authorization;
using OnlineShop.Application.Common;
using OnlineShop.Application.Notifications;
using OnlineShop.Domain.Staff;

namespace OnlineShop.Application.Staff;

/// <summary>
/// Who works in the back office. Administrators invite people by email or mobile number, change their roles, suspend
/// or remove them; the invitee accepts with an account whose verified email/phone matches. Nobody acts on their own
/// account here, and the shop always keeps at least one active Admin. Every change is written to the staff audit log.
/// </summary>
public sealed class StaffService(
    IApplicationDbContext db,
    IStaffIdentity staff,
    LoginIdentifierParser identifiers,
    IEmailSender email,
    IOptions<StoreOptions> store,
    TimeProvider clock)
{
    public async Task<StaffOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var members = (await staff.ListStaffAsync(cancellationToken))
            .Select(a => new StaffMember(a.UserId, a.DisplayName, a.Email, a.PhoneNumber, a.StaffRoles, a.TwoFactorEnabled, a.Suspended))
            .ToList();

        var now = Now;
        var invitations = (await db.StaffInvitations.AsNoTracking()
                .OrderByDescending(i => i.CreatedUtc)
                .Take(100)
                .ToListAsync(cancellationToken))
            .Select(i => ToView(i, now))
            .ToList();
        return new StaffOverview(members, invitations);
    }

    public async Task<Result<StaffInvitationView>> InviteAsync(string actorId, InviteStaffRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var identifier = identifiers.Parse(request.Identifier, store.Value.PhoneRegion);
        if (identifier is null)
            return AppError.Validation(StaffErrors.IdentifierInvalid, "Enter a valid email address or mobile number.");

        if (await staff.FindByIdentifierAsync(identifier) is { IsStaff: true })
            return AppError.Conflict(StaffErrors.AlreadyStaff, "This person already works in the back office.");

        var now = Now;
        var normalizedEmail = identifier.Email?.ToUpperInvariant();
        var pending = await db.StaffInvitations.AnyAsync(i =>
                i.AcceptedUtc == null && i.RevokedUtc == null && i.ExpiresUtc > now
                && (normalizedEmail != null ? i.NormalizedEmail == normalizedEmail : i.PhoneNumber == identifier.PhoneE164),
            cancellationToken);
        if (pending)
            return AppError.Conflict(StaffErrors.InvitationExists, "There is already an open invitation for this person.");

        var invitation = new StaffInvitation
        {
            Email = identifier.Email,
            NormalizedEmail = normalizedEmail,
            PhoneNumber = identifier.PhoneE164,
            Roles = string.Join(',', Distinct(request.Roles)),
            InvitedByUserId = actorId,
            ExpiresUtc = now + StaffInvitation.Lifetime
        };
        db.StaffInvitations.Add(invitation);
        Audit(actorId, StaffAuditAction.Invited, null, identifier.Email ?? identifier.PhoneE164, invitation.Roles);
        await db.SaveChangesAsync(cancellationToken);

        // Mobile invitations get no SMS: the providers are set up for one-time codes only. The administrator tells the
        // person to sign in with the number; the invitation waits on their account page.
        if (identifier.Email is not null)
            await SendInvitationEmailAsync(identifier.Email, request.Locale, cancellationToken);

        return ToView(invitation, now);
    }

    public async Task<Result<bool>> RevokeInvitationAsync(string actorId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var invitation = await db.StaffInvitations.SingleOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        if (invitation is null || invitation.StatusAt(Now) != StaffInvitationStatus.Pending)
            return AppError.NotFound(StaffErrors.InvitationNotFound, "No open invitation with this id.");

        invitation.RevokedUtc = Now;
        Audit(actorId, StaffAuditAction.InvitationRevoked, null, invitation.Email ?? invitation.PhoneNumber, invitation.Roles);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Open invitations for the signed-in user's verified email or phone.</summary>
    public async Task<IReadOnlyList<MyStaffInvitation>> GetMyInvitationsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var account = await staff.FindAsync(userId);
        if (account is null)
            return [];
        return (await PendingForAsync(account, cancellationToken))
            .Select(i => new MyStaffInvitation(i.Id, i.RoleList, i.ExpiresUtc))
            .ToList();
    }

    public async Task<Result<bool>> AcceptInvitationAsync(string userId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var account = await staff.FindAsync(userId);
        // Someone else's invitation answers exactly like a missing one.
        var invitation = account is null ? null : (await PendingForAsync(account, cancellationToken)).SingleOrDefault(i => i.Id == invitationId);
        if (account is null || invitation is null)
            return AppError.NotFound(StaffErrors.InvitationNotFound, "No open invitation with this id.");

        var roles = Distinct(account.StaffRoles.Concat(invitation.RoleList));
        await staff.SetStaffRolesAsync(userId, roles);

        invitation.AcceptedUtc = Now;
        invitation.AcceptedByUserId = userId;
        Audit(userId, StaffAuditAction.InvitationAccepted, userId, invitation.Email ?? invitation.PhoneNumber, string.Join(',', roles));
        await db.SaveChangesAsync(cancellationToken);

        await staff.RefreshCurrentSessionAsync(userId);
        return true;
    }

    public async Task<Result<bool>> ChangeRolesAsync(string actorId, string userId, ChangeStaffRolesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var roles = Distinct(request.Roles);
        var check = await CheckTargetAsync(actorId, userId, losesAdmin: _ => !roles.Contains(AppRoles.Admin));
        if (check.Error is not null)
            return check.Error;

        await staff.SetStaffRolesAsync(userId, roles);
        Audit(actorId, StaffAuditAction.RolesChanged, userId, null, string.Join(',', roles));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<bool>> SuspendAsync(string actorId, string userId, CancellationToken cancellationToken = default)
    {
        var check = await CheckTargetAsync(actorId, userId, losesAdmin: _ => true);
        if (check.Error is not null)
            return check.Error;
        if (check.Target!.Suspended)
            return AppError.Conflict(StaffErrors.AlreadySuspended, "The account is already suspended.");

        await staff.SetSuspendedAsync(userId, true);
        Audit(actorId, StaffAuditAction.Suspended, userId, null, null);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<bool>> ReactivateAsync(string actorId, string userId, CancellationToken cancellationToken = default)
    {
        var check = await CheckTargetAsync(actorId, userId, losesAdmin: _ => false);
        if (check.Error is not null)
            return check.Error;
        if (!check.Target!.Suspended)
            return AppError.Conflict(StaffErrors.NotSuspended, "The account is not suspended.");

        await staff.SetSuspendedAsync(userId, false);
        Audit(actorId, StaffAuditAction.Reactivated, userId, null, null);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Takes away every staff role; the account stays a normal customer account.</summary>
    public async Task<Result<bool>> RemoveFromStaffAsync(string actorId, string userId, CancellationToken cancellationToken = default)
    {
        var check = await CheckTargetAsync(actorId, userId, losesAdmin: _ => true);
        if (check.Error is not null)
            return check.Error;

        await staff.SetStaffRolesAsync(userId, []);
        Audit(actorId, StaffAuditAction.RemovedFromStaff, userId, null, null);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<StaffAuditView>> GetAuditAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        var entries = await db.StaffAuditEntries.AsNoTracking()
            .OrderByDescending(e => e.CreatedUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        var names = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var id in entries.SelectMany(e => new[] { e.ActorUserId, e.TargetUserId }).OfType<string>().Distinct())
            names[id] = (await staff.FindAsync(id))?.Name;

        return entries.Select(e => new StaffAuditView(e.Id, e.CreatedUtc, e.ActorUserId, names.GetValueOrDefault(e.ActorUserId),
                e.Action, e.TargetUserId, e.TargetUserId is null ? null : names.GetValueOrDefault(e.TargetUserId), e.TargetIdentifier, e.Details))
            .ToList();
    }

    /// <param name="losesAdmin">Whether the change would leave the target without active Admin rights.</param>
    private async Task<(StaffAccount? Target, AppError? Error)> CheckTargetAsync(string actorId, string userId, Func<StaffAccount, bool> losesAdmin)
    {
        if (string.Equals(actorId, userId, StringComparison.Ordinal))
            return (null, AppError.Forbidden(StaffErrors.NotOnYourself, "You cannot change your own staff access."));

        var target = await staff.FindAsync(userId);
        if (target is not { IsStaff: true })
            return (null, AppError.NotFound(StaffErrors.NotFound, "No staff member with this id."));

        var isActiveAdmin = !target.Suspended && target.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal);
        if (isActiveAdmin && losesAdmin(target) && await staff.CountActiveAdminsAsync() <= 1)
            return (null, AppError.Conflict(StaffErrors.LastAdmin, "The shop needs at least one active Admin."));

        return (target, null);
    }

    private async Task<IReadOnlyList<StaffInvitation>> PendingForAsync(StaffAccount account, CancellationToken cancellationToken)
    {
        // Only verified contact details count: an unconfirmed email could belong to someone else.
        var email = account.EmailConfirmed ? account.Email?.ToUpperInvariant() : null;
        var phone = account.PhoneNumberConfirmed ? account.PhoneNumber : null;
        if (email is null && phone is null)
            return [];

        var now = Now;
        return await db.StaffInvitations
            .Where(i => i.AcceptedUtc == null && i.RevokedUtc == null && i.ExpiresUtc > now
                        && ((email != null && i.NormalizedEmail == email) || (phone != null && i.PhoneNumber == phone)))
            .OrderBy(i => i.CreatedUtc)
            .ToListAsync(cancellationToken);
    }

    private async Task SendInvitationEmailAsync(string to, string? requestedLocale, CancellationToken cancellationToken)
    {
        var options = store.Value;
        var locale = requestedLocale is not null && options.LocaleList.Contains(requestedLocale, StringComparer.Ordinal)
            ? requestedLocale
            : options.DefaultLocale;
        var link = new Uri($"{options.PublicUrl.TrimEnd('/')}/{locale}/account");
        await email.SendAsync(EmailTemplates.Render(EmailTemplate.StaffInvitation, locale, options.DefaultLocale, to, options.Name, link), cancellationToken);
    }

    private void Audit(string actorId, StaffAuditAction action, string? targetUserId, string? targetIdentifier, string? details) =>
        db.StaffAuditEntries.Add(new StaffAuditEntry
        {
            ActorUserId = actorId,
            Action = action,
            TargetUserId = targetUserId,
            TargetIdentifier = targetIdentifier,
            Details = details
        });

    /// <summary>Distinct staff roles in the fixed order of <see cref="AppRoles.Staff"/>.</summary>
    private static List<string> Distinct(IEnumerable<string> roles)
    {
        var set = roles.ToHashSet(StringComparer.Ordinal);
        return AppRoles.Staff.Where(set.Contains).ToList();
    }

    private static StaffInvitationView ToView(StaffInvitation i, DateTime now) =>
        new(i.Id, i.Email, i.PhoneNumber, i.RoleList, i.CreatedUtc, i.ExpiresUtc, i.StatusAt(now));

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
