import { describe, expect, it, vi } from "vitest";
import { allowedTags, refreshStorefront } from "./refresh";

describe("allowedTags", () => {
  it("keeps known tags the user may clear", () => {
    expect(allowedTags({ tags: ["store", "currencies", "store"] }, ["settings.manage"])).toEqual(["store", "currencies"]);
    expect(allowedTags({ tags: ["reviews", "catalog"] }, ["reviews.moderate"])).toEqual(["reviews"]);
  });

  it("drops unknown tags, missing permissions and malformed bodies", () => {
    expect(allowedTags({ tags: ["catalog", "x"] }, ["settings.manage"])).toEqual([]);
    expect(allowedTags({ tags: ["store"] }, ["catalog.manage"])).toEqual([]);
    expect(allowedTags({ tags: "store" }, ["settings.manage"])).toEqual([]);
    expect(allowedTags(null, ["settings.manage"])).toEqual([]);
  });
});

describe("refreshStorefront", () => {
  it("posts the tags to the locale's refresh route and never throws", async () => {
    const fetchImpl = vi.fn(async () => new Response(null));
    await refreshStorefront("fa", ["store"], fetchImpl as unknown as typeof fetch);

    expect(fetchImpl).toHaveBeenCalledWith("/fa/admin/refresh", expect.objectContaining({ method: "POST", body: '{"tags":["store"]}' }));
    await expect(refreshStorefront("fa", ["store"], (async () => { throw new Error("offline"); }) as unknown as typeof fetch)).resolves.toBeUndefined();
  });
});
