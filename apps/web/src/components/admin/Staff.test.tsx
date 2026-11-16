// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";

const { router } = vi.hoisted(() => ({ router: { replace: vi.fn(), refresh: vi.fn() } }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));
vi.mock("next/link", () => ({
  default: ({ href, children, ...rest }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { StaffInvitations } from "@/components/account/StaffInvitations";
import { AdminShell } from "./AdminShell";
import { InviteStaffForm } from "./InviteStaffForm";
import { RoleCheckboxes } from "./RoleCheckboxes";
import { StaffAuditLog } from "./StaffAuditLog";
import { StaffInvitationsList } from "./StaffInvitationsList";
import { StaffMembers } from "./StaffMembers";

const texts = en.admin;
const fetchMock = vi.fn();
const respond = (status: number, body?: unknown) =>
  fetchMock.mockResolvedValueOnce(new Response(body === undefined ? null : JSON.stringify(body), { status }));
const call = (n: number) => ({ url: fetchMock.mock.calls[n][0] as string, init: fetchMock.mock.calls[n][1] as RequestInit });
const bodyOf = (n: number) => JSON.parse(call(n).init.body as string);

const owner = { userId: "me", displayName: "Owner", email: "owner@shop.test", phoneNumber: null, roles: ["Admin"], twoFactorEnabled: true, suspended: false };
const sara = { userId: "sara", displayName: null, email: "sara@shop.test", phoneNumber: "+37477123456", roles: ["Manager", "Warehouse"], twoFactorEnabled: false, suspended: false };

beforeEach(() => vi.stubGlobal("fetch", fetchMock));
afterEach(() => {
  cleanup();
  fetchMock.mockReset();
  router.refresh.mockReset();
  vi.unstubAllGlobals();
});

describe("AdminShell", () => {
  it("shows only the sections the user may open", () => {
    const { rerender } = render(<AdminShell locale="en" texts={texts} current="home" permissions={["orders.view"]}>x</AdminShell>);
    expect(screen.queryByRole("link", { name: texts.navStaff })).toBeNull();

    rerender(<AdminShell locale="en" texts={texts} current="staff" permissions={["staff.manage"]}>x</AdminShell>);
    expect(screen.getByRole("link", { name: texts.navStaff }).getAttribute("aria-current")).toBe("page");
    expect(screen.getByRole("link", { name: texts.backToShop }).getAttribute("href")).toBe("/en");
  });
});

describe("RoleCheckboxes", () => {
  it("lists every staff role with its description and ticks the given ones", () => {
    render(<RoleCheckboxes texts={texts} name="roles" defaultChecked={["Warehouse"]} />);

    expect(screen.getAllByRole("checkbox")).toHaveLength(4);
    expect((screen.getByRole("checkbox", { name: new RegExp(texts.roles.Warehouse) }) as HTMLInputElement).checked).toBe(true);
    expect(screen.getByText(texts.roleHelp.Accountant)).toBeTruthy();
  });
});

describe("InviteStaffForm", () => {
  it("sends the identifier, the ticked roles and the page language", async () => {
    render(<InviteStaffForm locale="de" texts={texts} />);
    respond(200, { id: "i1" });
    fireEvent.change(screen.getByLabelText(texts.inviteIdentifier), { target: { value: " new@shop.test " } });
    fireEvent.click(screen.getByRole("checkbox", { name: new RegExp(texts.roles.Manager) }));
    fireEvent.click(screen.getByRole("checkbox", { name: new RegExp(texts.roles.Accountant) }));
    fireEvent.click(screen.getByRole("button", { name: texts.invite }));

    expect(await screen.findByText(texts.invited)).toBeTruthy();
    expect(call(0).url).toBe("/api/v1/admin/staff/invitations");
    expect(bodyOf(0)).toEqual({ identifier: "new@shop.test", roles: ["Manager", "Accountant"], locale: "de" });
    expect(router.refresh).toHaveBeenCalled();
  });

  it("shows a translated business error", async () => {
    render(<InviteStaffForm locale="en" texts={texts} />);
    respond(409, { status: 409, code: "staff.already_staff" });
    fireEvent.click(screen.getByRole("button", { name: texts.invite }));

    expect(await screen.findByText(texts.errors.already_staff)).toBeTruthy();
  });
});

describe("StaffMembers", () => {
  it("lists members with roles and status, without actions on yourself", () => {
    render(<StaffMembers locale="en" texts={texts} members={[owner, sara]} currentUserId="me" />);

    const [mine, hers] = screen.getAllByRole("listitem");
    expect(within(mine).getByText(texts.you)).toBeTruthy();
    expect(within(mine).queryByRole("button")).toBeNull();
    expect(within(hers).getByText(`${texts.roles.Manager}, ${texts.roles.Warehouse}`)).toBeTruthy();
    expect(within(hers).getByText(texts.twoFactorOff)).toBeTruthy();
  });

  it("suspends after confirming and refreshes", async () => {
    const confirm = vi.fn<(message?: string) => boolean>(() => true);
    vi.stubGlobal("confirm", confirm);
    render(<StaffMembers locale="en" texts={texts} members={[sara]} currentUserId="me" />);
    respond(200, true);

    fireEvent.click(screen.getByRole("button", { name: texts.suspend }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(confirm.mock.calls[0][0]).toContain("sara@shop.test");
    expect(call(0)).toMatchObject({ url: "/api/v1/admin/staff/sara/suspend", init: { method: "POST" } });
  });

  it("does nothing when the removal is not confirmed, and offers reactivation for suspended members", () => {
    vi.stubGlobal("confirm", vi.fn(() => false));
    render(<StaffMembers locale="en" texts={texts} members={[{ ...sara, suspended: true }]} currentUserId="me" />);

    fireEvent.click(screen.getByRole("button", { name: texts.remove }));

    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: texts.reactivate })).toBeTruthy();
    expect(screen.getByText(texts.suspended)).toBeTruthy();
  });

  it("changes roles with PUT", async () => {
    render(<StaffMembers locale="en" texts={texts} members={[sara]} currentUserId="me" />);
    fireEvent.click(screen.getByRole("button", { name: texts.editRoles }));
    fireEvent.click(screen.getByRole("checkbox", { name: new RegExp(texts.roles.Warehouse) }));
    respond(200, true);
    fireEvent.click(screen.getByRole("button", { name: texts.saveRoles }));

    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(call(0)).toMatchObject({ url: "/api/v1/admin/staff/sara/roles", init: { method: "PUT" } });
    expect(bodyOf(0)).toEqual({ roles: ["Manager"] });
  });
});

describe("StaffInvitationsList", () => {
  const pending = { id: "i1", email: null, phoneNumber: "+989121234567", roles: ["Accountant"], createdUtc: "2026-11-16T09:00:00Z", expiresUtc: "2026-11-23T09:00:00Z", status: "Pending" as const };

  it("shows status and withdraws open invitations with DELETE", async () => {
    vi.stubGlobal("confirm", vi.fn(() => true));
    render(<StaffInvitationsList locale="en" texts={texts} invitations={[pending, { ...pending, id: "i2", status: "Accepted" as const }]} />);
    expect(screen.getByText(texts.invitationStatus.Accepted)).toBeTruthy();
    expect(screen.getAllByRole("button", { name: texts.revoke })).toHaveLength(1);
    respond(200, true);

    fireEvent.click(screen.getByRole("button", { name: texts.revoke }));

    await waitFor(() => expect(call(0)).toMatchObject({ url: "/api/v1/admin/staff/invitations/i1", init: { method: "DELETE" } }));
  });
});

describe("StaffAuditLog", () => {
  it("reads as who did what to whom, with the resulting roles", () => {
    render(
      <StaffAuditLog
        locale="en"
        texts={texts}
        entries={[
          { id: "a1", createdUtc: "2026-11-16T09:00:00Z", actorUserId: "me", actorName: "Owner", action: "Invited", targetUserId: null, targetName: null, targetIdentifier: "new@shop.test", details: "Manager" },
          { id: "a2", createdUtc: "2026-11-16T10:00:00Z", actorUserId: "sara", actorName: "Sara", action: "InvitationAccepted", targetUserId: "sara", targetName: "Sara", targetIdentifier: null, details: "Manager" },
        ]}
      />,
    );

    const [invited, accepted] = screen.getAllByRole("listitem");
    expect(invited.textContent).toContain(`Owner${texts.auditActions.Invited}new@shop.test(${texts.roles.Manager})`);
    expect(accepted.textContent).not.toContain("SaraSara");
  });

  it("says so when there is nothing yet", () => {
    render(<StaffAuditLog locale="en" texts={texts} entries={[]} />);
    expect(screen.getByText(texts.noAudit)).toBeTruthy();
  });
});

describe("StaffInvitations (account page)", () => {
  it("accepts an invitation and links to the admin panel", async () => {
    render(
      <StaffInvitations locale="en" texts={en.account} adminTexts={texts} invitations={[{ id: "i1", roles: ["Manager"], expiresUtc: "2026-11-23T09:00:00Z" }]} />,
    );
    expect(screen.getByText(new RegExp(texts.roles.Manager))).toBeTruthy();
    respond(200, true);

    fireEvent.click(screen.getByRole("button", { name: en.account.acceptInvitation }));

    expect(await screen.findByRole("link", { name: en.account.adminPanel })).toBeTruthy();
    expect(call(0).url).toBe("/api/v1/account/staff-invitations/i1/accept");
  });

  it("renders nothing without invitations", () => {
    const { container } = render(<StaffInvitations locale="en" texts={en.account} adminTexts={texts} invitations={[]} />);
    expect(container.innerHTML).toBe("");
  });
});
