import { describe, expect, it, vi } from "vitest";
import { createRedirectCache, matchRedirect, redirectAnswer, type RedirectRule, type RedirectTable } from "./redirects";

const rule = (fromPath: string, toPath: string | null, kind: RedirectRule["kind"] = "Permanent"): RedirectRule => ({ fromPath, toPath, kind });
const table: RedirectTable = new Map(
  [rule("/old", "/fa"), rule("/shop/item.php?id=12", "/fa/p/1001"), rule("/محصول", "/fa/p/1002", "Temporary"), rule("/gone", null, "Gone")].map((r) => [r.fromPath, r]),
);

describe("matchRedirect", () => {
  it("matches the path with its query first, then the path alone, ignoring a trailing slash", () => {
    expect(matchRedirect(table, "/shop/item.php", "?id=12")?.toPath).toBe("/fa/p/1001");
    expect(matchRedirect(table, "/old/", "?utm=x")?.toPath).toBe("/fa");
    expect(matchRedirect(table, "/%D9%85%D8%AD%D8%B5%D9%88%D9%84", "")?.kind).toBe("Temporary");
    expect(matchRedirect(table, "/new", "")).toBeUndefined();
    expect(matchRedirect(table, "/%E0%A4%A", "")).toBeUndefined();
    expect(matchRedirect(new Map(), "/old", "")).toBeUndefined();
  });
});

describe("redirectAnswer", () => {
  it("answers 301, 302 or 410 and resolves site paths", () => {
    expect(redirectAnswer(rule("/old", "/fa"), "https://shop.test/old")).toEqual({ status: 301, location: "https://shop.test/fa" });
    expect(redirectAnswer(rule("/a", "https://new.test/x", "Temporary"), "https://shop.test/a")).toEqual({ status: 302, location: "https://new.test/x" });
    expect(redirectAnswer(rule("/gone", null, "Gone"), "https://shop.test/gone")).toEqual({ status: 410, location: null });
  });
});

describe("createRedirectCache", () => {
  it("loads the rules once a minute and treats a failure as no redirects", async () => {
    let now = 0;
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(JSON.stringify([rule("/old", "/fa")]), { status: 200 }))
      .mockRejectedValueOnce(new Error("down"));
    const get = createRedirectCache(() => "http://api", fetchMock as unknown as typeof fetch, () => now);

    expect((await get()).get("/old")?.toPath).toBe("/fa");
    now = 30_000;
    await get();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    now = 61_000;
    expect((await get()).size).toBe(0);
    expect(fetchMock.mock.calls[0][0]).toBe("http://api/api/v1/redirects");
  });
});
