using System.Security.Claims;
using OnlineShop.Api.Common;
using OnlineShop.Api.Security;
using OnlineShop.Application.Authorization;
using OnlineShop.Application.Staff;

namespace OnlineShop.Api.Endpoints;

/// <summary>Staff management for administrators (permission <c>staff.manage</c>) and accepting an invitation.</summary>
public static class StaffEndpoints
{
    public static IEndpointRouteBuilder MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/staff").WithTags("Admin: staff")
            .RequireAuthorization(Permissions.Policy(Permissions.StaffManage));

        admin.MapGet("/", async (StaffService staff, CancellationToken ct) => TypedResults.Ok(await staff.GetOverviewAsync(ct)))
            .WithName("GetStaff")
            .WithSummary("Staff members with their roles and status, and recent invitations.");

        admin.MapPost("/invitations", async (InviteStaffRequest request, ClaimsPrincipal user, StaffService staff, CancellationToken ct) =>
                (await staff.InviteAsync(user.GetUserId(), request, ct)).ToHttpResult())
            .Validate<InviteStaffRequest>()
            .WithName("InviteStaff")
            .WithSummary("Invite someone by email or mobile number with one or more staff roles (valid 7 days).")
            .Produces<StaffInvitationView>();

        admin.MapDelete("/invitations/{id:guid}", async (Guid id, ClaimsPrincipal user, StaffService staff, CancellationToken ct) =>
                (await staff.RevokeInvitationAsync(user.GetUserId(), id, ct)).ToHttpResult())
            .WithName("RevokeStaffInvitation")
            .WithSummary("Withdraw an open invitation.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapPut("/{userId}/roles", async (string userId, ChangeStaffRolesRequest request, ClaimsPrincipal user, StaffService staff, CancellationToken ct) =>
                (await staff.ChangeRolesAsync(user.GetUserId(), userId, request, ct)).ToHttpResult())
            .Validate<ChangeStaffRolesRequest>()
            .WithName("ChangeStaffRoles")
            .WithSummary("Replace a staff member's roles (not your own; at least one active Admin stays).");

        admin.MapPost("/{userId}/suspend", async (string userId, ClaimsPrincipal user, StaffService staff, CancellationToken ct) =>
                (await staff.SuspendAsync(user.GetUserId(), userId, ct)).ToHttpResult())
            .WithName("SuspendStaff")
            .WithSummary("Block every sign-in of a staff member; open sessions end within a minute.");

        admin.MapPost("/{userId}/reactivate", async (string userId, ClaimsPrincipal user, StaffService staff, CancellationToken ct) =>
                (await staff.ReactivateAsync(user.GetUserId(), userId, ct)).ToHttpResult())
            .WithName("ReactivateStaff")
            .WithSummary("Allow a suspended staff member to sign in again.");

        admin.MapPost("/{userId}/remove", async (string userId, ClaimsPrincipal user, StaffService staff, CancellationToken ct) =>
                (await staff.RemoveFromStaffAsync(user.GetUserId(), userId, ct)).ToHttpResult())
            .WithName("RemoveFromStaff")
            .WithSummary("Take away every staff role; the account stays a customer account.");

        admin.MapGet("/audit", async (StaffService staff, CancellationToken ct) => TypedResults.Ok(await staff.GetAuditAsync(100, ct)))
            .WithName("GetStaffAudit")
            .WithSummary("The latest 100 staff changes: who did what, to whom, and when.");

        var mine = app.MapGroup("/api/v1/account/staff-invitations").WithTags("Account").RequireAuthorization();

        mine.MapGet("/", async (ClaimsPrincipal user, StaffService staff, CancellationToken ct) =>
                TypedResults.Ok(await staff.GetMyInvitationsAsync(user.GetUserId(), ct)))
            .WithName("GetMyStaffInvitations")
            .WithSummary("Open staff invitations for the signed-in user's verified email or mobile number.");

        mine.MapPost("/{id:guid}/accept", async (Guid id, ClaimsPrincipal user, StaffService staff, CancellationToken ct) =>
                (await staff.AcceptInvitationAsync(user.GetUserId(), id, ct)).ToHttpResult())
            .RequireRateLimiting(AccountEndpoints.AuthRateLimitPolicy)
            .WithName("AcceptStaffInvitation")
            .WithSummary("Accept an invitation: its roles are added and this session is refreshed.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
