import { describe, expect, it } from "vitest";
import {
  appliedFilters,
  canonicalPath,
  clearFiltersHref,
  hasFilters,
  isIndexable,
  listHas,
  listingHref,
  listingParams,
  relaxationLinks,
  sortName,
  sortsFor,
  toApiQuery,
  toggled,
  type BrowseFacets,
} from "./browse";

describe("listing parameters", () => {
  it("keep only the listing's keys, join repeated ones and drop bad sort and page values", () => {
    expect(
      listingParams({ brand: ["orvia", "ranna"], "o.color": "black, white", utm_source: "x", sort: "random", page: "0", stock: "1", q: "phone", "o.": "x" }),
    ).toEqual({ brand: "orvia,ranna", "o.color": "black,white", stock: "1" });
    expect(listingParams({ sort: "price_asc", page: "3" })).toEqual({ sort: "price_asc", page: "3" });
  });

  it("decide indexing and the canonical address", () => {
    expect(hasFilters({ sort: "newest", page: "2" })).toBe(false);
    expect(isIndexable({ page: "2" })).toBe(true);
    expect(isIndexable({ sort: "newest" })).toBe(false);
    expect(isIndexable({ "y.network_5g": "1" })).toBe(false);
    expect(canonicalPath("/en/c/phones", { page: "2" })).toBe("/en/c/phones?page=2");
    expect(canonicalPath("/en/c/phones", { page: "2", brand: "orvia" })).toBe("/en/c/phones");
    expect(canonicalPath("/en/c/phones", {})).toBe("/en/c/phones");
  });

  it("build addresses: a filter change goes back to page 1, a page change keeps the filters", () => {
    expect(listingHref("/en/c/x", { brand: "a", page: "3" }, { stock: "1" })).toBe("/en/c/x?brand=a&stock=1");
    expect(listingHref("/en/c/x", { brand: "a" }, { page: "2" })).toBe("/en/c/x?brand=a&page=2");
    expect(listingHref("/en/c/x", { brand: "a" }, { page: "1" })).toBe("/en/c/x?brand=a");
    expect(listingHref("/en/c/x", { brand: "a" }, { brand: null })).toBe("/en/c/x");
    expect(clearFiltersHref("/en/c/x", { brand: "a", sort: "price_asc", page: "2" })).toBe("/en/c/x?sort=price_asc");
    expect(clearFiltersHref("/en/search", { q: "phone", brand: "a" })).toBe("/en/search?q=phone");
    expect(toggled("a,b", "a")).toBe("b");
    expect(toggled("a", "a")).toBeNull();
    expect(toggled(undefined, "c")).toBe("c");
    expect(listHas("a,b", "b")).toBe(true);
  });

  it("turn displayed prices back into the base currency and number ranges into the API form", () => {
    const query = new URLSearchParams(
      toApiQuery({ c: "phones", q: "" }, { min: "10", max: "21", "nmin.screen_size": "6", "nmax.ram": "8", "nmin.bad": "x", brand: "orvia" }, "en", 0.5),
    );
    expect(query.get("locale")).toBe("en");
    expect(query.get("c")).toBe("phones");
    expect(query.has("q")).toBe(false);
    expect([query.get("min"), query.get("max")]).toEqual(["20", "42"]);
    expect(query.get("n.screen_size")).toBe("6-");
    expect(query.get("n.ram")).toBe("-8");
    expect(query.has("n.bad")).toBe(false);
    expect(query.get("brand")).toBe("orvia");
  });

  it("offer relevance only for searches and map the API's sort names", () => {
    expect(sortsFor(false)).not.toContain("relevance");
    expect(sortsFor(true)[0]).toBe("relevance");
    expect(sortName("PriceDesc")).toBe("price_desc");
  });
});

describe("applied filters", () => {
  const facets: BrowseFacets = {
    subCategories: [],
    groups: [],
    brands: [{ value: "orvia", label: "Orvia", count: 3, selected: true }],
    price: { min: 1, max: 100 },
    selectedMin: null,
    selectedMax: null,
    inStock: 2,
    inStockSelected: true,
    discounted: 1,
    discountedSelected: false,
    specs: [
      { code: "color", name: "Colour", group: null, kind: "Option", unit: null, options: [{ value: "black", label: "Black", count: 1, selected: true }], min: null, max: null, selectedMin: null, selectedMax: null, yesCount: 0, yesSelected: false },
      { code: "screen_size", name: "Screen", group: null, kind: "Number", unit: "in", options: [], min: 5, max: 7, selectedMin: 6, selectedMax: null, yesCount: 0, yesSelected: false },
      { code: "network_5g", name: "5G", group: null, kind: "Boolean", unit: null, options: [], min: null, max: null, selectedMin: null, selectedMax: null, yesCount: 1, yesSelected: true },
    ],
  };
  const texts = { inStock: "In stock", onSale: "On sale", priceFrom: "from {price}", priceTo: "up to {price}", yes: "yes" };

  it("lists each filter with the address that removes only it", () => {
    const params = { brand: "orvia", "o.color": "black", "nmin.screen_size": "6", "y.network_5g": "1", stock: "1", min: "5", sort: "newest" };
    const chips = appliedFilters("/en/c/x", params, facets, texts, (a) => `€${a}`);

    expect(chips.map((c) => c.label)).toEqual(["Orvia", "from €5", "In stock", "Black", "Screen: 6– in", "5G: yes"]);
    expect(chips[0].href).toBe("/en/c/x?min=5&nmin.screen_size=6&o.color=black&sort=newest&stock=1&y.network_5g=1");
    expect(chips[4].href).not.toContain("screen_size");
  });
});

describe("relaxing filters", () => {
  it("offers a link per hiding filter, ranges dropping both inputs", () => {
    const result = {
      relaxations: [
        { filter: "price", count: 12 },
        { filter: "n.ram", count: 3 },
        { filter: "o.color", count: 2 },
        { filter: "stock", count: 1 },
      ],
      facets: { specs: [{ code: "ram", name: "RAM" }, { code: "color", name: "Colour" }] },
    } as unknown as Parameters<typeof relaxationLinks>[2];
    const texts = { brands: "Brand", groups: "Type", price: "Price", inStock: "In stock", onSale: "On sale", relaxFilter: "Without {filter} ({count})" };
    const params = { min: "5", max: "9", "nmin.ram": "8", "o.color": "red", stock: "1" };

    const links = relaxationLinks("/en/search", params, result, texts, String);

    expect(links.map((l) => l.label)).toEqual(["Without Price (12)", "Without RAM (3)", "Without Colour (2)", "Without In stock (1)"]);
    expect(links[0].href).toBe("/en/search?nmin.ram=8&o.color=red&stock=1");
    expect(links[1].href).toBe("/en/search?max=9&min=5&o.color=red&stock=1");
  });
});
