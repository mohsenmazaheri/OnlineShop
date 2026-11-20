import { beforeEach, describe, expect, it, vi } from "vitest";
import type { BrowseResult } from "./browse";

const mocks = vi.hoisted(() => ({
  browse: vi.fn(),
  store: { name: "Test shop", defaultLocale: "fa", currency: "EUR" },
}));
vi.mock("@/lib/api/storefront", () => ({ browseProducts: mocks.browse }));
vi.mock("@/lib/store/load", () => ({ loadStore: async () => mocks.store }));
vi.mock("@/lib/currency/load", () => ({
  loadDisplayCurrency: async () => ({ money: { code: "USD", symbol: null, decimals: 2, roundingStep: 0.01, rate: 2, isBase: false, base: "EUR" } }),
}));
vi.mock("@/lib/seo/config", () => ({ siteUrl: () => "https://shop.test" }));

import { decodeSegment, listingMetadata, loadListing } from "./listing-page";

const scope: BrowseResult["scope"] = {
  kind: "category",
  title: "Phones",
  description: null,
  imageUrl: null,
  path: "c/phones",
  breadcrumb: [],
  alternates: [
    { locale: "fa", path: "c/موبایل" },
    { locale: "en", path: "c/phones" },
  ],
};
const result = { scope, items: [], total: 0, page: 1, pageSize: 24, sort: "Newest", filtered: false, facets: {} } as unknown as BrowseResult;

beforeEach(() => mocks.browse.mockReset());

describe("listing pages", () => {
  it("decode non-Latin path segments and leave broken ones alone", () => {
    expect(decodeSegment("%D9%85%D9%88%D8%A8%D8%A7%DB%8C%D9%84")).toBe("موبایل");
    expect(decodeSegment("%E0%A4%A")).toBe("%E0%A4%A");
  });

  it("ask the API with base-currency prices and 404 for an unknown address or language", async () => {
    mocks.browse.mockResolvedValueOnce(result);
    const listing = await loadListing("en", { c: "phones" }, { max: "20", utm_source: "mail" });
    expect(mocks.browse).toHaveBeenCalledWith("locale=en&c=phones&max=10");
    expect(listing?.basePath).toBe("/en/c/phones");

    mocks.browse.mockResolvedValueOnce(null);
    expect(await loadListing("en", { c: "nope" }, {})).toBeNull();
    expect(await loadListing("xx", { c: "phones" }, {})).toBeNull();
  });

  it("index only the plain list in a translated language", async () => {
    mocks.browse.mockResolvedValue(result);
    const plain = await listingMetadata(await loadListing("en", { c: "phones" }, { page: "2" }));
    expect(plain.robots).toBeUndefined();
    expect(plain.alternates?.canonical).toBe("https://shop.test/en/c/phones?page=2");
    expect(plain.description).toBe("Phones — browse and buy online at Test shop");

    const filtered = await listingMetadata(await loadListing("en", { c: "phones" }, { brand: "orvia" }));
    expect(filtered.robots).toEqual({ index: false, follow: true });
    expect(filtered.alternates?.canonical).toBe("https://shop.test/en/c/phones");

    expect((await listingMetadata(await loadListing("de", { c: "phones" }, {}))).robots).toEqual({ index: false, follow: true });
    expect((await listingMetadata(null)).robots).toEqual({ index: false, follow: false });
  });
});
