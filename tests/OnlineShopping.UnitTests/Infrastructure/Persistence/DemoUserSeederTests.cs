using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Infrastructure.Identity;
using OnlineShopping.Infrastructure.Persistence.Seeding;
using OnlineShopping.UnitTests.TestSupport;
using StoreOptions = OnlineShopping.Application.Common.StoreOptions;

namespace OnlineShopping.UnitTests.Infrastructure.Persistence;

public sealed class DemoUserSeederTests : IDisposable
{
    private const string Password = "demo pass 2026";
    private readonly IdentityTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static HostingEnvironment Env(string name) => new() { EnvironmentName = name };

    private async Task SeedAsync(string environment = "Development", string publicUrl = "http://localhost:3000", bool enabled = true)
    {
        var roles = _host.Get<RoleManager<IdentityRole>>();
        foreach (var role in AppRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));
        }
        var seeder = new DemoUserSeeder(
            _host.Users,
            Env(environment),
            Options.Create(new StoreOptions { Locales = "fa", PublicUrl = publicUrl }),
            Options.Create(new DemoUserOptions { Enabled = enabled, CustomerEmail = "customer@shop.localhost", AdminEmail = "admin@shop.localhost", Password = Password }),
            NullLogger<DemoUserSeeder>.Instance,
            _host.Database.Clock);
        await seeder.SeedAsync();
    }

    [Fact]
    public async Task Creates_a_confirmed_customer_and_admin_that_can_sign_in()
    {
        await SeedAsync();

        var customer = (await _host.Users.FindByEmailAsync("customer@shop.localhost"))!;
        var admin = (await _host.Users.FindByEmailAsync("admin@shop.localhost"))!;
        Assert.True(customer.EmailConfirmed && admin.EmailConfirmed);
        Assert.Equal([AppRoles.Customer], await _host.Users.GetRolesAsync(customer));
        Assert.Contains(AppRoles.Admin, await _host.Users.GetRolesAsync(admin));
        Assert.True(await _host.Users.CheckPasswordAsync(admin, Password));
    }

    [Fact]
    public async Task Running_again_keeps_one_account_each_and_a_changed_password()
    {
        await SeedAsync();
        var admin = (await _host.Users.FindByEmailAsync("admin@shop.localhost"))!;
        await _host.Users.ChangePasswordAsync(admin, Password, "changed pass 2026");

        await SeedAsync();

        Assert.Equal(2, _host.Users.Users.Count());
        Assert.True(await _host.Users.CheckPasswordAsync((await _host.Users.FindByEmailAsync("admin@shop.localhost"))!, "changed pass 2026"));
    }

    [Theory]
    [InlineData("Production", "http://localhost:3000")]
    [InlineData("Staging", "http://localhost:3000")]
    [InlineData("Development", "https://shop.example.com")]
    [InlineData("Development", "https://localhost.example.com")]
    public async Task Never_runs_outside_a_local_development_instance(string environment, string publicUrl)
    {
        await SeedAsync(environment, publicUrl);

        Assert.Empty(_host.Users.Users);
    }

    [Fact]
    public async Task Does_nothing_unless_enabled()
    {
        await SeedAsync(enabled: false);

        Assert.Empty(_host.Users.Users);
    }

    [Theory]
    [InlineData("http://localhost:3000", true)]
    [InlineData("http://127.0.0.1:3300", true)]
    [InlineData("http://shop.localhost", true)]
    [InlineData("http://shop.test", true)]
    [InlineData("https://shop.example.com", false)]
    [InlineData("not a url", false)]
    public void Local_addresses_are_recognised(string url, bool local) =>
        Assert.Equal(local, DemoUserSeeder.IsLocalDevelopment(Env(Environments.Development), url));
}
