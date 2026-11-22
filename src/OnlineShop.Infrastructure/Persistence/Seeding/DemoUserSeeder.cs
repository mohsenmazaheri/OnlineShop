using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Authorization;
using StoreOptions = OnlineShop.Application.Common.StoreOptions;
using OnlineShop.Infrastructure.Identity;

namespace OnlineShop.Infrastructure.Persistence.Seeding;

/// <summary>
/// Fixed sign-in accounts for trying the pages locally (section <c>DemoUsers</c>, filled only in
/// appsettings.Development.json): a customer and an administrator with confirmed emails.
/// </summary>
public sealed class DemoUserOptions
{
    public const string SectionName = "DemoUsers";

    public bool Enabled { get; set; }
    public string? CustomerEmail { get; set; }
    public string? AdminEmail { get; set; }

    /// <summary>A development-only test password (both accounts); never used outside a local Development instance.</summary>
    public string? Password { get; set; }
}

/// <summary>
/// Creates the demo accounts — and only on a developer machine: the environment must be Development <b>and</b> the
/// shop's public address must be local (localhost, *.localhost, *.test). A production instance therefore never gets
/// them, even with the section copied over or the environment set wrongly. Safe to run on every start; an existing
/// account keeps its password and only gets its role and confirmed email back.
/// </summary>
public sealed partial class DemoUserSeeder(
    UserManager<AppUser> users,
    IHostEnvironment environment,
    IOptions<StoreOptions> store,
    IOptions<DemoUserOptions> options,
    ILogger<DemoUserSeeder> logger,
    TimeProvider clock)
{
    public async Task SeedAsync()
    {
        var demo = options.Value;
        if (!demo.Enabled)
            return;
        if (!IsLocalDevelopment(environment, store.Value.PublicUrl))
        {
            LogRefused(logger, environment.EnvironmentName, store.Value.PublicUrl);
            return;
        }
        if (string.IsNullOrWhiteSpace(demo.Password))
            return;

        if (!string.IsNullOrWhiteSpace(demo.CustomerEmail))
            await EnsureAsync(demo.CustomerEmail, "Test Customer", AppRoles.Customer, demo.Password);
        if (!string.IsNullOrWhiteSpace(demo.AdminEmail))
            await EnsureAsync(demo.AdminEmail, "Test Admin", AppRoles.Admin, demo.Password);
    }

    /// <summary>Development environment and a public address that can only be a developer's own machine.</summary>
    public static bool IsLocalDevelopment(IHostEnvironment environment, string publicUrl)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment() || !Uri.TryCreate(publicUrl, UriKind.Absolute, out var url))
            return false;
        var host = url.Host;
        return url.IsLoopback
               || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".test", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureAsync(string email, string displayName, string role, string password)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new AppUser
            {
                UserName = Guid.CreateVersion7().ToString("N"),
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName,
                PreferredLocale = null,
                CreatedUtc = clock.GetUtcNow().UtcDateTime,
                LockoutEnabled = true
            };
            Ensure(await users.CreateAsync(user, password), $"create demo user {email}");
            LogCreated(logger, email, role);
        }
        else if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            Ensure(await users.UpdateAsync(user), $"confirm demo user {email}");
        }

        foreach (var needed in role == AppRoles.Customer ? [AppRoles.Customer] : new[] { AppRoles.Customer, role })
        {
            if (!await users.IsInRoleAsync(user, needed))
                Ensure(await users.AddToRoleAsync(user, needed), $"add demo user {email} to {needed}");
        }
    }

    private static void Ensure(IdentityResult result, string what)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not {what}: " + string.Join("; ", result.Errors.Select(e => e.Code)));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo user {Email} created ({Role}). For local testing only.")]
    private static partial void LogCreated(ILogger logger, string email, string role);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "DemoUsers is enabled but ignored: demo accounts are created only in Development with a local public URL (environment {Environment}, public URL {PublicUrl}).")]
    private static partial void LogRefused(ILogger logger, string environment, string publicUrl);
}
