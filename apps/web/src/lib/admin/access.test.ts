import { afterEach, describe, expect, it, vi } from "vitest";

const { getCurrentUser } = vi.hoisted(() => ({ getCurrentUser: vi.fn() }));
vi.mock("server-only", () => ({}));
vi.mock("@/lib/account/session", () => ({ getCurrentUser }));
vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`redirect:${url}`);
  },
  notFound: () => {
    throw new Error("notFound");
  },
}));

import { requirePermission, requireStaff } from "./access";

const user = (permissions: string[]) => ({ id: "u1", roles: [], permissions });

afterEach(() => getCurrentUser.mockReset());

describe("admin access", () => {
  it("sends signed-out visitors to sign in and back", async () => {
    getCurrentUser.mockResolvedValue(null);

    await expect(requirePermission("fa", "staff.manage", "/fa/admin/staff")).rejects.toThrow(
      "redirect:/fa/account/login?returnUrl=%2Ffa%2Fadmin%2Fstaff",
    );
  });

  it("answers 404 to users without the permission", async () => {
    getCurrentUser.mockResolvedValue(user(["catalog.manage"]));

    await expect(requirePermission("fa", "staff.manage", "/fa/admin/staff")).rejects.toThrow("notFound");
  });

  it("returns the user who holds the permission", async () => {
    getCurrentUser.mockResolvedValue(user(["staff.manage"]));

    expect((await requirePermission("en", "staff.manage", "/en/admin/staff")).id).toBe("u1");
  });

  it("keeps customers out of the panel's start page but lets any staff in", async () => {
    getCurrentUser.mockResolvedValue(user([]));
    await expect(requireStaff("en", "/en/admin")).rejects.toThrow("notFound");

    getCurrentUser.mockResolvedValue(user(["orders.view"]));
    expect((await requireStaff("en", "/en/admin")).id).toBe("u1");
  });
});
