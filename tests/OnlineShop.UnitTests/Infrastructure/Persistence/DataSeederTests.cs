using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using StoreOptions = OnlineShop.Application.Common.StoreOptions;
using Microsoft.Extensions.Options;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.Infrastructure.Persistence.Seeding;
using OnlineShop.Infrastructure.Persistence.Seeding.Demo;
using OnlineShop.Infrastructure.Persistence.Interceptors;
using OnlineShop.Application.Authorization;
using OnlineShop.Infrastructure.Identity;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Infrastructure.Persistence;

public sealed class DataSeederTests : IDisposable
{
    private const string AdminEmail = "admin@test.local";
    private const string ValidPassword = "Test-Only-Pa55word";

    private readonly SqliteTestDatabase _database = new();
    private readonly ServiceProvider _services;

    public DataSeederTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o
            .UseSqlite(_database.Connection)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditableEntityInterceptor(_database.Clock)));
        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequireDigit = true;
                o.Password.RequiredLength = 6;
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task Creates_every_application_role()
    {
        await SeedAsync(new SeedOptions());

        using var scope = _services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in AppRoles.All)
            Assert.True(await roles.RoleExistsAsync(role), $"Role {role} missing");
    }

    [Fact]
    public async Task Running_twice_does_not_duplicate_roles_or_admin()
    {
        var options = new SeedOptions { AdminEmail = AdminEmail, AdminPassword = ValidPassword };

        await SeedAsync(options);
        await SeedAsync(options);

        await using var db = _database.CreateContext();
        Assert.Equal(AppRoles.All.Count, await db.Roles.CountAsync());
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Skips_the_admin_when_no_credentials_are_configured()
    {
        await SeedAsync(new SeedOptions { AdminEmail = AdminEmail });

        await using var db = _database.CreateContext();
        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Creates_a_confirmed_admin_in_the_Admin_role()
    {
        await SeedAsync(new SeedOptions { AdminEmail = AdminEmail, AdminPassword = ValidPassword });

        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = await users.FindByEmailAsync(AdminEmail);
        Assert.NotNull(admin);
        Assert.True(admin.EmailConfirmed);
        Assert.True(await users.IsInRoleAsync(admin, AppRoles.Admin));
        Assert.True(await users.CheckPasswordAsync(admin, ValidPassword));
    }

    [Fact]
    public async Task Adds_an_existing_user_to_the_Admin_role()
    {
        using (var scope = _services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.CreateAsync(new AppUser { UserName = AdminEmail, Email = AdminEmail }, ValidPassword);
        }

        await SeedAsync(new SeedOptions { AdminEmail = AdminEmail, AdminPassword = ValidPassword });

        using var verify = _services.CreateScope();
        var userManager = verify.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.True(await userManager.IsInRoleAsync((await userManager.FindByEmailAsync(AdminEmail))!, AppRoles.Admin));
    }

    [Fact]
    public async Task Fails_loudly_when_Identity_rejects_the_admin_password()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SeedAsync(new SeedOptions { AdminEmail = AdminEmail, AdminPassword = "short" }));

        Assert.Contains("PasswordTooShort", ex.Message);
    }

    [Fact]
    public async Task Also_creates_the_demo_users_when_they_are_enabled_on_a_local_development_instance()
    {
        await SeedAsync(new SeedOptions(), new DemoUserOptions
        {
            Enabled = true, CustomerEmail = "customer@shop.localhost", AdminEmail = "admin@shop.localhost", Password = ValidPassword
        });

        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.True(await users.IsInRoleAsync((await users.FindByEmailAsync("admin@shop.localhost"))!, AppRoles.Admin));
        Assert.True(await users.IsInRoleAsync((await users.FindByEmailAsync("customer@shop.localhost"))!, AppRoles.Customer));
    }

    private async Task SeedAsync(SeedOptions options, DemoUserOptions? demo = null)
    {
        using var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var demoUsers = new DemoUserSeeder(
            users,
            new HostingEnvironment { EnvironmentName = Environments.Development },
            Options.Create(new StoreOptions { Locales = "fa", PublicUrl = "http://localhost:3000" }),
            Options.Create(demo ?? new DemoUserOptions()),
            NullLogger<DemoUserSeeder>.Instance,
            _database.Clock);
        var db = sp.GetRequiredService<AppDbContext>();
        var demoCatalog = new DemoCatalogSeeder(
            db,
            Options.Create(new StoreOptions { Locales = "fa", PublicUrl = "http://localhost:3000" }),
            Options.Create(new DemoCatalogOptions()),
            NullLogger<DemoCatalogSeeder>.Instance);
        var seeder = new DataSeeder(
            db,
            sp.GetRequiredService<RoleManager<IdentityRole>>(),
            users,
            Options.Create(options),
            NullLogger<DataSeeder>.Instance,
            _database.Clock,
            demoUsers,
            demoCatalog,
            new CurrencySeeder(db, Options.Create(new StoreOptions { Locales = "fa,en", Currency = "IRR", PublicUrl = "http://localhost:3000" })),
            TestLedger.For(db, new StoreOptions { Locales = "fa", PublicUrl = "http://localhost:3000" }));

        await seeder.SeedAsync();
    }
}
