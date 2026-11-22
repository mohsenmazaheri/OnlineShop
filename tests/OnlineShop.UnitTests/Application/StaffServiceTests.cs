using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Authorization;
using OnlineShop.Application.Common;
using OnlineShop.Application.Staff;
using OnlineShop.Domain.Staff;
using OnlineShop.Infrastructure.Identity;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class StaffServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly AppDbContext _db;
    private readonly FakeStaffIdentity _identity = new();
    private readonly CapturingEmailSender _email = new();
    private readonly StaffService _service;
    private readonly string _admin;

    public StaffServiceTests()
    {
        _db = _database.CreateContext();
        var store = new StoreOptions { Name = "Shop", Locales = "fa,en", DefaultLocale = "fa", PublicUrl = "https://shop.test", PhoneRegion = "IR" };
        _service = new StaffService(_db, _identity, new LoginIdentifierParser(new PhoneNumberNormalizer()), _email, Options.Create(store), _database.Clock);

        // Invitations reference the inviting user (FK), so the admin also exists in the database.
        var admin = TestData.User("owner@shop.test");
        _db.Users.Add(admin);
        _db.SaveChanges();
        _admin = admin.Id;
        _identity.Add(_admin, "owner@shop.test", null, AppRoles.Admin);
    }

    public void Dispose()
    {
        _db.Dispose();
        _database.Dispose();
    }

    private Task<Result<StaffInvitationView>> InviteAsync(string identifier, params string[] roles) =>
        _service.InviteAsync(_admin, new InviteStaffRequest(identifier, roles, "en"));

    [Fact]
    public async Task An_email_invitation_is_stored_audited_and_announced_by_email()
    {
        var result = await InviteAsync(" Sara@Example.com ", AppRoles.Warehouse, AppRoles.Manager);

        var view = result.Value!;
        Assert.Equal("Sara@Example.com", view.Email);
        Assert.Equal([AppRoles.Manager, AppRoles.Warehouse], view.Roles);
        Assert.Equal(StaffInvitationStatus.Pending, view.Status);
        Assert.Equal(_database.Clock.GetUtcNow().UtcDateTime + StaffInvitation.Lifetime, view.ExpiresUtc);

        var mail = Assert.Single(_email.Sent);
        Assert.Equal("Sara@Example.com", mail.To);
        Assert.Contains("https://shop.test/en/account", mail.TextBody, StringComparison.Ordinal);
        Assert.Equal(StaffAuditAction.Invited, (await _db.StaffAuditEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task A_mobile_invitation_is_normalized_and_sends_no_email()
    {
        var view = (await InviteAsync("۰۹۱۲ ۱۲۳ ۴۵۶۷", AppRoles.Accountant)).Value!;

        Assert.Equal("+989121234567", view.PhoneNumber);
        Assert.Null(view.Email);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task Invitations_are_refused_for_bad_identifiers_existing_staff_and_duplicates()
    {
        _identity.Add("u-staff", "staff@shop.test", null, AppRoles.Manager);

        Assert.Equal(StaffErrors.IdentifierInvalid, (await InviteAsync("not an email", AppRoles.Manager)).Error!.Code);
        Assert.Equal(StaffErrors.AlreadyStaff, (await InviteAsync("STAFF@shop.test", AppRoles.Manager)).Error!.Code);

        Assert.True((await InviteAsync("new@shop.test", AppRoles.Manager)).IsSuccess);
        Assert.Equal(StaffErrors.InvitationExists, (await InviteAsync("NEW@shop.test", AppRoles.Warehouse)).Error!.Code);
    }

    [Fact]
    public async Task An_expired_invitation_does_not_block_a_new_one()
    {
        await InviteAsync("new@shop.test", AppRoles.Manager);
        _database.Clock.Advance(StaffInvitation.Lifetime + TimeSpan.FromMinutes(1));

        Assert.True((await InviteAsync("new@shop.test", AppRoles.Manager)).IsSuccess);
    }

    [Fact]
    public async Task The_invitee_sees_and_accepts_it_with_a_verified_email()
    {
        var invitation = (await InviteAsync("sara@example.com", AppRoles.Manager)).Value!;
        _identity.Add("sara", "SARA@example.com");

        var mine = Assert.Single(await _service.GetMyInvitationsAsync("sara"));
        Assert.Equal(invitation.Id, mine.Id);

        Assert.True((await _service.AcceptInvitationAsync("sara", invitation.Id)).IsSuccess);

        Assert.Contains(AppRoles.Manager, _identity.Accounts.Single(a => a.Id == "sara").Roles);
        Assert.Contains(AppRoles.Customer, _identity.Accounts.Single(a => a.Id == "sara").Roles);
        Assert.Equal(["sara"], _identity.RefreshedSessions);
        Assert.Empty(await _service.GetMyInvitationsAsync("sara"));
        Assert.Equal(StaffInvitationStatus.Accepted, (await _service.GetOverviewAsync()).Invitations.Single().Status);
    }

    [Fact]
    public async Task Accepting_adds_roles_to_the_ones_already_held()
    {
        var invitation = (await InviteAsync("09121234567", AppRoles.Accountant)).Value!;
        _identity.Add("ali", null, "+989121234567");

        await _service.AcceptInvitationAsync("ali", invitation.Id);

        Assert.Equal([AppRoles.Accountant], (await _service.GetOverviewAsync()).Members.Single(m => m.UserId == "ali").Roles);
    }

    [Fact]
    public async Task Unverified_email_or_someone_else_cannot_accept()
    {
        var invitation = (await InviteAsync("sara@example.com", AppRoles.Manager)).Value!;
        _identity.Add("unverified", "sara@example.com").EmailConfirmed = false;
        _identity.Add("other", "other@example.com");

        Assert.Empty(await _service.GetMyInvitationsAsync("unverified"));
        Assert.Equal(StaffErrors.InvitationNotFound, (await _service.AcceptInvitationAsync("unverified", invitation.Id)).Error!.Code);
        Assert.Equal(StaffErrors.InvitationNotFound, (await _service.AcceptInvitationAsync("other", invitation.Id)).Error!.Code);
        Assert.DoesNotContain(_identity.Accounts, a => a.Id != _admin && a.Roles.Contains(AppRoles.Manager));
    }

    [Fact]
    public async Task Expired_and_revoked_invitations_cannot_be_accepted()
    {
        var expired = (await InviteAsync("a@example.com", AppRoles.Manager)).Value!;
        var revoked = (await InviteAsync("b@example.com", AppRoles.Manager)).Value!;
        _identity.Add("a", "a@example.com");
        _identity.Add("b", "b@example.com");

        Assert.True((await _service.RevokeInvitationAsync(_admin, revoked.Id)).IsSuccess);
        _database.Clock.Advance(StaffInvitation.Lifetime);

        Assert.Equal(StaffErrors.InvitationNotFound, (await _service.AcceptInvitationAsync("a", expired.Id)).Error!.Code);
        Assert.Equal(StaffErrors.InvitationNotFound, (await _service.AcceptInvitationAsync("b", revoked.Id)).Error!.Code);
        Assert.Equal(StaffErrors.InvitationNotFound, (await _service.RevokeInvitationAsync(_admin, revoked.Id)).Error!.Code);
    }

    [Fact]
    public async Task Roles_can_be_changed_but_not_your_own()
    {
        _identity.Add("m", "m@shop.test", null, AppRoles.Manager);

        Assert.True((await _service.ChangeRolesAsync(_admin, "m", new ChangeStaffRolesRequest([AppRoles.Warehouse, AppRoles.Accountant]))).IsSuccess);
        Assert.Equal([AppRoles.Customer, AppRoles.Warehouse, AppRoles.Accountant], _identity.Accounts.Single(a => a.Id == "m").Roles);

        var self = await _service.ChangeRolesAsync(_admin, _admin, new ChangeStaffRolesRequest([AppRoles.Manager]));
        Assert.Equal(StaffErrors.NotOnYourself, self.Error!.Code);
        Assert.Equal(ErrorKind.Forbidden, self.Error.Kind);
    }

    [Fact]
    public async Task Customers_are_not_staff_members_here()
    {
        _identity.Add("c", "c@shop.test");

        Assert.Equal(StaffErrors.NotFound, (await _service.SuspendAsync(_admin, "c")).Error!.Code);
        Assert.Equal(StaffErrors.NotFound, (await _service.ChangeRolesAsync(_admin, "ghost", new ChangeStaffRolesRequest([AppRoles.Manager]))).Error!.Code);
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_suspended_demoted_or_removed()
    {
        // Another admin manages (e.g. a future role with staff.manage); the target is the only active Admin.
        _identity.Accounts.Single(a => a.Id == _admin).Suspended = true;
        _identity.Add("owner2", "o2@shop.test", null, AppRoles.Admin);

        Assert.Equal(StaffErrors.LastAdmin, (await _service.SuspendAsync(_admin, "owner2")).Error!.Code);
        Assert.Equal(StaffErrors.LastAdmin, (await _service.RemoveFromStaffAsync(_admin, "owner2")).Error!.Code);
        Assert.Equal(StaffErrors.LastAdmin, (await _service.ChangeRolesAsync(_admin, "owner2", new ChangeStaffRolesRequest([AppRoles.Manager]))).Error!.Code);
        Assert.True((await _service.ChangeRolesAsync(_admin, "owner2", new ChangeStaffRolesRequest([AppRoles.Admin, AppRoles.Manager]))).IsSuccess);
    }

    [Fact]
    public async Task Suspend_reactivate_and_remove_are_audited()
    {
        _identity.Add("w", "w@shop.test", null, AppRoles.Warehouse);

        Assert.True((await _service.SuspendAsync(_admin, "w")).IsSuccess);
        Assert.Equal(StaffErrors.AlreadySuspended, (await _service.SuspendAsync(_admin, "w")).Error!.Code);
        Assert.True((await _service.ReactivateAsync(_admin, "w")).IsSuccess);
        Assert.Equal(StaffErrors.NotSuspended, (await _service.ReactivateAsync(_admin, "w")).Error!.Code);
        Assert.True((await _service.RemoveFromStaffAsync(_admin, "w")).IsSuccess);

        Assert.Equal([AppRoles.Customer], _identity.Accounts.Single(a => a.Id == "w").Roles);
        Assert.DoesNotContain((await _service.GetOverviewAsync()).Members, m => m.UserId == "w");

        var audit = await _service.GetAuditAsync();
        Assert.Equal([StaffAuditAction.RemovedFromStaff, StaffAuditAction.Reactivated, StaffAuditAction.Suspended], audit.Select(a => a.Action));
        Assert.All(audit, a => Assert.Equal("owner@shop.test", a.ActorName));
        Assert.All(audit, a => Assert.Equal("w@shop.test", a.TargetName));
    }

    [Fact]
    public async Task Nobody_suspends_or_removes_themselves()
    {
        Assert.Equal(StaffErrors.NotOnYourself, (await _service.SuspendAsync(_admin, _admin)).Error!.Code);
        Assert.Equal(StaffErrors.NotOnYourself, (await _service.RemoveFromStaffAsync(_admin, _admin)).Error!.Code);
    }
}
