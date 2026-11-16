using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineShopping.Application.Accounts;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Application.Staff;
using OnlineShopping.Infrastructure.Persistence;

namespace OnlineShopping.Infrastructure.Identity;

/// <summary><see cref="IStaffIdentity"/> on ASP.NET Core Identity (roles, lockout, security stamp, session refresh).</summary>
public sealed class StaffIdentity(
    UserManager<AppUser> users,
    AppSignInManager signIn,
    AppDbContext db,
    TimeProvider clock) : IStaffIdentity
{
    public async Task<IReadOnlyList<StaffAccount>> ListStaffAsync(CancellationToken cancellationToken = default)
    {
        var staffRoles = AppRoles.Staff.ToList();
        var roleRows = await (
                from userRole in db.UserRoles
                join role in db.Roles on userRole.RoleId equals role.Id
                join user in db.Users on userRole.UserId equals user.Id
                where user.ErasedUtc == null
                select new { userRole.UserId, Role = role.Name! })
            .ToListAsync(cancellationToken);

        var rolesByUser = roleRows.GroupBy(r => r.UserId)
            .Where(g => g.Any(r => staffRoles.Contains(r.Role)))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Role).Order(StringComparer.Ordinal).ToList());

        var staffUsers = await db.Users.AsNoTracking()
            .Where(u => rolesByUser.Keys.Contains(u.Id))
            .ToListAsync(cancellationToken);

        return staffUsers
            .Select(u => ToAccount(u, rolesByUser[u.Id]))
            .OrderBy(a => a.Suspended)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<StaffAccount?> FindAsync(string userId)
    {
        var user = await users.FindByIdAsync(userId);
        return user is null || user.ErasedUtc is not null ? null : ToAccount(user, (await users.GetRolesAsync(user)).ToList());
    }

    public async Task<StaffAccount?> FindByIdentifierAsync(LoginIdentifier identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        var user = identifier.Email is not null
            ? await users.FindByEmailAsync(identifier.Email)
            : await users.Users.SingleOrDefaultAsync(u => u.PhoneNumber == identifier.PhoneE164);
        return user is null || user.ErasedUtc is not null ? null : ToAccount(user, (await users.GetRolesAsync(user)).ToList());
    }

    public async Task SetStaffRolesAsync(string userId, IReadOnlyList<string> staffRoles)
    {
        ArgumentNullException.ThrowIfNull(staffRoles);
        var user = await RequireAsync(userId);
        var current = await users.GetRolesAsync(user);

        var remove = current.Where(r => AppRoles.Staff.Contains(r, StringComparer.Ordinal) && !staffRoles.Contains(r, StringComparer.Ordinal)).ToList();
        var add = staffRoles.Where(r => !current.Contains(r, StringComparer.Ordinal)).ToList();
        if (remove.Count > 0)
            Ensure(await users.RemoveFromRolesAsync(user, remove), "remove roles");
        if (add.Count > 0)
            Ensure(await users.AddToRolesAsync(user, add), "add roles");
        // No new security stamp: that would sign the person out everywhere. The periodic session re-check
        // (SecurityStampValidatorOptions.ValidationInterval, one minute) rebuilds the claims with the new roles.
    }

    public async Task SetSuspendedAsync(string userId, bool suspended)
    {
        var user = await RequireAsync(userId);
        user.SuspendedUtc = suspended ? clock.GetUtcNow().UtcDateTime : null;
        user.LockoutEnabled = true;
        // Identity's lockout blocks every sign-in path (password, SMS code, provider, second step).
        user.LockoutEnd = suspended ? DateTimeOffset.MaxValue : null;
        user.AccessFailedCount = 0;
        Ensure(await users.UpdateAsync(user), "update suspension");
        Ensure(await users.UpdateSecurityStampAsync(user), "renew security stamp");
    }

    public async Task<int> CountActiveAdminsAsync() =>
        (await users.GetUsersInRoleAsync(AppRoles.Admin)).Count(u => u.SuspendedUtc is null && u.ErasedUtc is null);

    public async Task RefreshCurrentSessionAsync(string userId) =>
        await signIn.RefreshSignInAsync(await RequireAsync(userId));

    private static StaffAccount ToAccount(AppUser user, IReadOnlyList<string> roles) =>
        new(user.Id, user.DisplayName, user.Email, user.EmailConfirmed, user.PhoneNumber, user.PhoneNumberConfirmed,
            roles, user.TwoFactorEnabled, user.SuspendedUtc is not null);

    private async Task<AppUser> RequireAsync(string userId) =>
        await users.FindByIdAsync(userId) is { ErasedUtc: null } user
            ? user
            : throw new InvalidOperationException($"User {userId} does not exist.");

    private static void Ensure(IdentityResult result, string what)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not {what}: " + string.Join("; ", result.Errors.Select(e => e.Code)));
    }
}
