using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using OnlineShopping.Application.Accounts;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Application.Staff;
using OnlineShopping.Domain.Staff;
using OnlineShopping.IntegrationTests.TestSupport;

namespace OnlineShopping.IntegrationTests;

/// <summary>Staff management end to end: a seeded Admin invites, people accept, get suspended and removed.</summary>
public sealed class StaffTests(StaffTests.StaffFactory factory) : IClassFixture<StaffTests.StaffFactory>
{
    public sealed class StaffFactory : ApiFactory
    {
        public const string AdminEmail = "owner@shop.test";
        public const string AdminPassword = "owner pass 2026"; // test-only value for this throwaway database

        /// <summary>Controllable clock, to pass the one-minute session re-check without waiting.</summary>
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

        protected override IReadOnlyDictionary<string, string?> ConfigurationOverrides => new Dictionary<string, string?>
        {
            ["Database:AdminEmail"] = AdminEmail,
            ["Database:AdminPassword"] = AdminPassword
        };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        }
    }

    private const string Password = "correct horse 42";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/account/login", new LoginRequest(StaffFactory.AdminEmail, StaffFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Registers a customer and confirms the email through the link in the captured email.</summary>
    private async Task<(HttpClient Client, string Email, string UserId)> CustomerAsync(bool confirm = true)
    {
        var client = factory.CreateClient();
        var email = $"person-{Guid.NewGuid():N}@example.com";
        var registered = await client.PostAsJsonAsync("/api/v1/account/register", new RegisterRequest(email, Password, null, "en"));
        var user = (await registered.Content.ReadFromJsonAsync<CurrentUser>(Json))!;
        if (confirm)
        {
            var (userId, token) = factory.Emails.LinkParametersFor(email);
            (await client.PostAsJsonAsync("/api/v1/account/confirm-email", new ConfirmEmailRequest(userId, token))).EnsureSuccessStatusCode();
        }
        return (client, email, user.Id);
    }

    private static async Task<StaffInvitationView> InviteAsync(HttpClient admin, string identifier, params string[] roles)
    {
        using var response = await admin.PostAsJsonAsync("/api/v1/admin/staff/invitations", new InviteStaffRequest(identifier, roles, "en"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StaffInvitationView>(Json))!;
    }

    private async Task<(HttpClient Client, string Email, string UserId)> StaffMemberAsync(string role)
    {
        using var admin = await AdminAsync();
        var member = await CustomerAsync();
        var invitation = await InviteAsync(admin, member.Email, role);
        (await member.Client.PostAsync($"/api/v1/account/staff-invitations/{invitation.Id}/accept", null)).EnsureSuccessStatusCode();
        return member;
    }

    [Fact]
    public async Task Only_administrators_reach_staff_management()
    {
        using var anonymous = factory.CreateClient();
        var (customer, _, _) = await CustomerAsync();
        using (customer)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/staff")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/admin/staff")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync("/api/v1/admin/staff/invitations",
                new InviteStaffRequest("x@example.com", [AppRoles.Admin], null))).StatusCode);
        }
    }

    [Fact]
    public async Task An_invited_customer_accepts_and_gets_the_role_at_once()
    {
        using var admin = await AdminAsync();
        var (client, email, userId) = await CustomerAsync();
        using (client)
        {
            var invitation = await InviteAsync(admin, email.ToUpperInvariant(), AppRoles.Manager);
            Assert.Contains(factory.Emails.Sent, m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase) && m.TextBody.Contains("/en/account", StringComparison.Ordinal));

            var mine = await client.GetFromJsonAsync<List<MyStaffInvitation>>("/api/v1/account/staff-invitations", Json);
            Assert.Equal(invitation.Id, Assert.Single(mine!).Id);

            using var accepted = await client.PostAsync($"/api/v1/account/staff-invitations/{invitation.Id}/accept", null);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

            // The refreshed session already carries the new role and its permissions.
            var me = await client.GetFromJsonAsync<CurrentUser>("/api/v1/account/me", Json);
            Assert.Contains(AppRoles.Manager, me!.Roles);
            Assert.Contains(Permissions.CatalogManage, me.Permissions);

            var overview = await admin.GetFromJsonAsync<StaffOverview>("/api/v1/admin/staff", Json);
            Assert.Contains(overview!.Members, m => m.UserId == userId && m.Roles.SequenceEqual([AppRoles.Manager]));
            Assert.Equal(StaffInvitationStatus.Accepted, overview.Invitations.Single(i => i.Id == invitation.Id).Status);

            var audit = await admin.GetFromJsonAsync<List<StaffAuditView>>("/api/v1/admin/staff/audit", Json);
            Assert.Contains(audit!, a => a.Action == StaffAuditAction.InvitationAccepted && a.TargetUserId == userId);
        }
    }

    [Fact]
    public async Task An_unconfirmed_email_does_not_see_the_invitation()
    {
        using var admin = await AdminAsync();
        var (client, email, _) = await CustomerAsync(confirm: false);
        using (client)
        {
            var invitation = await InviteAsync(admin, email, AppRoles.Manager);

            Assert.Empty((await client.GetFromJsonAsync<List<MyStaffInvitation>>("/api/v1/account/staff-invitations", Json))!);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/account/staff-invitations/{invitation.Id}/accept", null)).StatusCode);
        }
    }

    [Fact]
    public async Task Suspension_ends_open_sessions_and_blocks_sign_in_until_reactivated()
    {
        var (member, email, userId) = await StaffMemberAsync(AppRoles.Warehouse);
        using (member)
        using (var admin = await AdminAsync())
        {
            (await admin.PostAsync($"/api/v1/admin/staff/{userId}/suspend", null)).EnsureSuccessStatusCode();

            // Past the one-minute session re-check: the member's open session is gone.
            factory.Clock.Advance(TimeSpan.FromMinutes(2));
            Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/v1/account/me")).StatusCode);

            using var fresh = factory.CreateClient();
            using var refused = await fresh.PostAsJsonAsync("/api/v1/account/login", new LoginRequest(email, Password, false));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

            (await admin.PostAsync($"/api/v1/admin/staff/{userId}/reactivate", null)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, (await fresh.PostAsJsonAsync("/api/v1/account/login", new LoginRequest(email, Password, false))).StatusCode);
        }
    }

    [Fact]
    public async Task Removed_staff_lose_their_permissions_but_keep_the_account()
    {
        var (member, _, userId) = await StaffMemberAsync(AppRoles.Manager);
        using (member)
        using (var admin = await AdminAsync())
        {
            (await admin.PostAsync($"/api/v1/admin/staff/{userId}/remove", null)).EnsureSuccessStatusCode();
            factory.Clock.Advance(TimeSpan.FromMinutes(2));

            var me = await member.GetFromJsonAsync<CurrentUser>("/api/v1/account/me", Json);
            Assert.Equal([AppRoles.Customer], me!.Roles);
            Assert.Empty(me.Permissions);
        }
    }

    [Fact]
    public async Task An_administrator_cannot_act_on_their_own_account()
    {
        using var admin = await AdminAsync();
        var me = await admin.GetFromJsonAsync<CurrentUser>("/api/v1/account/me", Json);

        using var response = await admin.PostAsync($"/api/v1/admin/staff/{me!.Id}/suspend", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(StaffErrors.NotOnYourself, body.RootElement.GetProperty("code").GetString());
    }
}
