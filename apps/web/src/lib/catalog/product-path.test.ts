import { describe, expect, it, vi } from "vitest";
import { fetchProductSlug, parseProductPath, productPath, productRedirectTarget } from "./product-path";

describe("productPath", () => {
  it("encodes the slug, in any script", () => {
    expect(productPath("en", 1001, "orvia-x1")).toBe("/en/p/1001/orvia-x1");
    expect(productPath("fa", 1001, "گوشی")).toBe("/fa/p/1001/%DA%AF%D9%88%D8%B4%DB%8C");
    expect(productPath("fa", 1001, "")).toBe("/fa/p/1001");
  });
});

describe("parseProductPath", () => {
  it("reads locale, number and the decoded slug", () => {
    expect(parseProductPath("/fa/p/1001/%DA%AF%D9%88%D8%B4%DB%8C")).toEqual({ locale: "fa", number: 1001, slug: "گوشی" });
    expect(parseProductPath("/en/p/1001")).toEqual({ locale: "en", number: 1001, slug: "" });
    expect(parseProductPath("/en/p/1001/x/")).toEqual({ locale: "en", number: 1001, slug: "x" });
  });

  it("ignores every other path", () => {
    for (const path of ["/en", "/en/p", "/en/p/abc/x", "/xx/p/1/x", "/en/p/1/x/y", "/en/product/1/x"]) {
      expect(parseProductPath(path)).toBeNull();
    }
  });

  it("keeps a broken escape as a (wrong) slug", () => {
    expect(parseProductPath("/en/p/7/%E0%A4%A")?.slug).toBe("%E0%A4%A");
  });
});

describe("productRedirectTarget", () => {
  const parts = { locale: "en" as const, number: 1001, slug: "old-name" };

  it("redirects a wrong or missing slug to the current one, keeping the query", () => {
    expect(productRedirectTarget(parts, "new-name", "?utm_source=x")).toBe("/en/p/1001/new-name?utm_source=x");
    expect(productRedirectTarget({ ...parts, slug: "" }, "new-name", "")).toBe("/en/p/1001/new-name");
  });

  it("leaves the right address alone", () => {
    expect(productRedirectTarget(parts, "old-name", "")).toBeNull();
    expect(productRedirectTarget(parts, "", "")).toBeNull();
  });
});

describe("fetchProductSlug", () => {
  const parts = { locale: "fa" as const, number: 1001, slug: "" };

  it("returns the slug from the API", async () => {
    const fetchImpl = vi.fn(async () => Response.json({ slug: "گوشی" }));

    expect(await fetchProductSlug("http://api", parts, fetchImpl as unknown as typeof fetch)).toBe("گوشی");
    expect(fetchImpl).toHaveBeenCalledWith("http://api/api/v1/catalog/products/1001/slug?locale=fa", expect.anything());
  });

  it("returns null for a missing product or an unreachable API", async () => {
    const notFound = vi.fn(async () => new Response(null, { status: 404 }));
    const down = vi.fn(async () => {
      throw new Error("ECONNREFUSED");
    });

    expect(await fetchProductSlug("http://api", parts, notFound as unknown as typeof fetch)).toBeNull();
    expect(await fetchProductSlug("http://api", parts, down as unknown as typeof fetch)).toBeNull();
  });
});
