using OnlineShopping.Application.Authorization;
using OnlineShopping.Application.Staff;
using OnlineShopping.Domain.Staff;

namespace OnlineShopping.UnitTests.Application;

public sealed class StaffRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);

    private static StaffInvitation Invitation() =>
        new() { Email = "a@b.test", Roles = "Manager, Warehouse", InvitedByUserId = "admin", ExpiresUtc = Now.AddDays(7) };

    [Fact]
    public void Status_follows_accepted_revoked_and_expiry()
    {
        Assert.Equal(StaffInvitationStatus.Pending, Invitation().StatusAt(Now));
        Assert.Equal(StaffInvitationStatus.Expired, Invitation().StatusAt(Now.AddDays(7)));
        var revoked = Invitation();
        revoked.RevokedUtc = Now;
        Assert.Equal(StaffInvitationStatus.Revoked, revoked.StatusAt(Now));
        var accepted = Invitation();
        accepted.AcceptedUtc = Now;
        accepted.RevokedUtc = Now; // accepted wins
        Assert.Equal(StaffInvitationStatus.Accepted, accepted.StatusAt(Now.AddDays(30)));
    }

    [Fact]
    public void Roles_are_read_from_the_stored_list() =>
        Assert.Equal([AppRoles.Manager, AppRoles.Warehouse], Invitation().RoleList);

    [Theory]
    [InlineData(new string[0], StaffErrors.RolesRequired)]
    [InlineData(new[] { "Customer" }, StaffErrors.RoleInvalid)]
    [InlineData(new[] { "Manager", "Root" }, StaffErrors.RoleInvalid)]
    public void Only_staff_roles_can_be_given(string[] roles, string code)
    {
        Assert.Contains(new InviteStaffRequestValidator().Validate(new InviteStaffRequest("a@b.test", roles, null)).Errors, e => e.ErrorCode == code);
        Assert.Contains(new ChangeStaffRolesRequestValidator().Validate(new ChangeStaffRolesRequest(roles)).Errors, e => e.ErrorCode == code);
    }

    [Fact]
    public void An_identifier_is_required() =>
        Assert.Contains(new InviteStaffRequestValidator().Validate(new InviteStaffRequest("", [AppRoles.Admin], null)).Errors,
            e => e.ErrorCode == StaffErrors.IdentifierRequired);

    [Fact]
    public void Staff_accounts_expose_only_their_staff_roles()
    {
        var account = new StaffAccount("u", null, "a@b.test", true, null, false, [AppRoles.Customer, AppRoles.Manager], false, false);

        Assert.True(account.IsStaff);
        Assert.Equal([AppRoles.Manager], account.StaffRoles);
        Assert.Equal("a@b.test", account.Name);
        Assert.False((account with { Roles = [AppRoles.Customer] }).IsStaff);
    }
}
