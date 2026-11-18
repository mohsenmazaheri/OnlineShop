using OnlineShopping.Application.Accounts;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Application.Staff;

namespace OnlineShopping.UnitTests.TestSupport;

/// <summary>In-memory accounts with roles for <see cref="StaffService"/> tests.</summary>
internal sealed class FakeStaffIdentity : IStaffIdentity
{
    public sealed class Account
    {
        public required string Id { get; init; }
        public string? Email { get; set; }
        public bool EmailConfirmed { get; set; } = true;
        public string? Phone { get; set; }
        public List<string> Roles { get; } = [AppRoles.Customer];
        public bool Suspended { get; set; }
    }

    public List<Account> Accounts { get; } = [];
    public List<string> RefreshedSessions { get; } = [];

    public Account Add(string id, string? email = null, string? phone = null, params string[] staffRoles)
    {
        var account = new Account { Id = id, Email = email, Phone = phone };
        account.Roles.AddRange(staffRoles);
        Accounts.Add(account);
        return account;
    }

    public Task<IReadOnlyList<StaffAccount>> ListStaffAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StaffAccount>>(Accounts.Select(ToStaffAccount).Where(a => a.IsStaff).ToList());

    public Task<StaffAccount?> FindAsync(string userId) =>
        Task.FromResult(Accounts.SingleOrDefault(a => a.Id == userId) is { } a ? ToStaffAccount(a) : null);

    public Task<StaffAccount?> FindByIdentifierAsync(LoginIdentifier identifier) =>
        Task.FromResult(Accounts.SingleOrDefault(a =>
            (identifier.Email is not null && string.Equals(a.Email, identifier.Email, StringComparison.OrdinalIgnoreCase))
            || (identifier.PhoneE164 is not null && a.Phone == identifier.PhoneE164)) is { } a ? ToStaffAccount(a) : null);

    public Task SetStaffRolesAsync(string userId, IReadOnlyList<string> staffRoles)
    {
        var account = Accounts.Single(a => a.Id == userId);
        account.Roles.RemoveAll(r => AppRoles.Staff.Contains(r));
        account.Roles.AddRange(staffRoles);
        return Task.CompletedTask;
    }

    public Task SetSuspendedAsync(string userId, bool suspended)
    {
        Accounts.Single(a => a.Id == userId).Suspended = suspended;
        return Task.CompletedTask;
    }

    public Task<int> CountActiveAdminsAsync() => Task.FromResult(Accounts.Count(a => !a.Suspended && a.Roles.Contains(AppRoles.Admin)));

    public Task RefreshCurrentSessionAsync(string userId)
    {
        RefreshedSessions.Add(userId);
        return Task.CompletedTask;
    }

    private static StaffAccount ToStaffAccount(Account a) =>
        new(a.Id, null, a.Email, a.EmailConfirmed, a.Phone, a.Phone is not null, a.Roles.ToList(), false, a.Suspended);
}
