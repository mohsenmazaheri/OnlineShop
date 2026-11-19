import { beforeEach, describe, expect, it, vi } from "vitest";

const { user, revalidateTag } = vi.hoisted(() => ({ user: { current: null as null | { permissions: string[] } }, revalidateTag: vi.fn() }));
vi.mock("next/cache", () => ({ revalidateTag }));
vi.mock("@/lib/account/session", () => ({ getCurrentUser: async () => user.current }));

import { POST } from "./route";

const post = (body: unknown, origin = "http://localhost:3000") =>
  POST(new Request("http://localhost:3000/fa/admin/refresh", { method: "POST", headers: { origin }, body: JSON.stringify(body) }));

beforeEach(() => {
  revalidateTag.mockReset();
  user.current = { permissions: ["settings.manage"] };
});

describe("POST /{locale}/admin/refresh", () => {
  it("expires the allowed tags at once", async () => {
    const response = await post({ tags: ["store", "currencies"] });

    expect(response.status).toBe(200);
    expect(revalidateTag).toHaveBeenCalledWith("store", { expire: 0 });
    expect(revalidateTag).toHaveBeenCalledWith("currencies", { expire: 0 });
  });

  it("refuses other sites, signed-out visitors and users without the permission", async () => {
    expect((await post({ tags: ["store"] }, "https://evil.example")).status).toBe(403);
    user.current = null;
    expect((await post({ tags: ["store"] })).status).toBe(401);
    user.current = { permissions: ["catalog.manage"] };
    expect((await post({ tags: ["store"] })).status).toBe(403);
    expect(revalidateTag).not.toHaveBeenCalled();
  });
});
