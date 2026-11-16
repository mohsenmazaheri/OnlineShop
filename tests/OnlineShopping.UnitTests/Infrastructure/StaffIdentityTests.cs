using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OnlineShopping.Application.Accounts;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Infrastructure.Identity;
using OnlineShopping.UnitTests.TestSupport;

namespace OnlineShopping.UnitTests.Infrastructure;

public sealed class StaffIdentityTests : IDisposable
{
    private readonly IdentityTestHost _host = new(s => s.AddScoped<StaffIdentity>());

    public void Dispose() => _host.Dispose();

    private StaffIdentity Sut => _host.Get<StaffIdentity>();

    private async Task<AppUser> UserAsync(params string[] roles)
    {
        var roleManager = _host.Get<RoleManager<IdentityRole>>();
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
        var user = await _host.CreateUserAsync(phone: $"+98912{Random.Shared.Next(1_000_000, 9_999_999)}");
        await _host.Users.AddToRolesAsync(user, [AppRoles.Customer, .. roles]);
        return user;
    }

    [Fact]
    public async Task Only_accounts_with_a_staff_role_are_listed()
    {
        var manager = await UserAsync(AppRoles.Manager);
        await UserAsync();

        var staff = await Sut.ListStaffAsync();

        var member = Assert.Single(staff);
        Assert.Equal(manager.Id, member.UserId);
        Assert.Equal([AppRoles.Customer, AppRoles.Manager], member.Roles);
    }

    [Fact]
    public async Task Setting_staff_roles_keeps_other_roles_and_does_not_end_sessions()
    {
        var user = await UserAsync(AppRoles.Manager);
        var stamp = user.SecurityStamp;

        await Sut.SetStaffRolesAsync(user.Id, [AppRoles.Warehouse, AppRoles.Accountant]);

        var account = (await Sut.FindAsync(user.Id))!;
        Assert.Equal([AppRoles.Accountant, AppRoles.Customer, AppRoles.Warehouse], account.Roles.Order(StringComparer.Ordinal));
        Assert.Equal(stamp, (await _host.Users.FindByIdAsync(user.Id))!.SecurityStamp);

        await Sut.SetStaffRolesAsync(user.Id, []);
        Assert.Equal([AppRoles.Customer], (await Sut.FindAsync(user.Id))!.Roles);
    }

    [Fact]
    public async Task A_suspended_account_is_locked_out_until_reactivated()
    {
        var user = await UserAsync(AppRoles.Warehouse);

        await Sut.SetSuspendedAsync(user.Id, true);

        var locked = (await _host.Users.FindByIdAsync(user.Id))!;
        Assert.True(await _host.Users.IsLockedOutAsync(locked));
        Assert.NotNull(locked.SuspendedUtc);
        Assert.True((await Sut.FindAsync(user.Id))!.Suspended);

        await Sut.SetSuspendedAsync(user.Id, false);

        var reactivated = (await _host.Users.FindByIdAsync(user.Id))!;
        Assert.False(await _host.Users.IsLockedOutAsync(reactivated));
        Assert.Null(reactivated.SuspendedUtc);
    }

    [Fact]
    public async Task Active_admins_are_counted_without_suspended_ones()
    {
        await UserAsync(AppRoles.Admin);
        var second = await UserAsync(AppRoles.Admin);
        await UserAsync(AppRoles.Manager);
        Assert.Equal(2, await Sut.CountActiveAdminsAsync());

        await Sut.SetSuspendedAsync(second.Id, true);

        Assert.Equal(1, await Sut.CountActiveAdminsAsync());
    }

    [Fact]
    public async Task Accounts_are_found_by_email_or_phone()
    {
        var user = await UserAsync();

        Assert.Equal(user.Id, (await Sut.FindByIdentifierAsync(new LoginIdentifier(user.Email!.ToUpperInvariant(), null)))!.UserId);
        Assert.Equal(user.Id, (await Sut.FindByIdentifierAsync(new LoginIdentifier(null, user.PhoneNumber)))!.UserId);
        Assert.Null(await Sut.FindByIdentifierAsync(new LoginIdentifier("nobody@example.com", null)));
        Assert.Null(await Sut.FindAsync("missing"));
    }
}
