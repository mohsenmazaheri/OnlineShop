// @vitest-environment jsdom
import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import en from "@/lib/i18n/dictionaries/en.json";
import type { BrowseResult } from "@/lib/catalog/browse";

vi.mock("next/image", () => ({
  // eslint-disable-next-line @next/next/no-img-element
  default: ({ src, alt }: { src: string; alt: string }) => <img src={src} alt={alt} />,
}));

import { ProductListing } from "./ProductListing";

afterEach(cleanup);

const money = { code: "EUR", symbol: null, decimals: 2, roundingStep: 0.01, rate: 1, isBase: true, base: "EUR" };
const product = (number: number, name: string) => ({ number, name, slug: name.toLowerCase(), imageUrl: null, price: 10, compareAtPrice: null, inStock: true });

function result(over: Partial<BrowseResult> = {}): BrowseResult {
  return {
    scope: { kind: "category", title: "Phones", description: null, imageUrl: null, path: "c/phones", breadcrumb: [], alternates: [] },
    items: [product(1001, "Alpha"), product(1002, "Beta")],
    total: 50,
    page: 2,
    pageSize: 24,
    sort: "Newest",
    filtered: true,
    search: null,
    relaxations: [],
    facets: {
      subCategories: [{ name: "Android", path: "c/phones/android", count: 30 }],
      groups: [],
      brands: [
        { value: "orvia", label: "Orvia", count: 12, selected: true },
        { value: "ranna", label: "Ranna", count: 0, selected: false },
      ],
      price: { min: 5, max: 500 },
      selectedMin: null,
      selectedMax: null,
      inStock: 40,
      inStockSelected: false,
      discounted: 3,
      discountedSelected: false,
      specs: [
        { code: "screen_size", name: "Screen size", group: null, kind: "Number", unit: "in", options: [], min: 5, max: 7, selectedMin: null, selectedMax: null, yesCount: 0, yesSelected: false },
        { code: "network_5g", name: "5G", group: null, kind: "Boolean", unit: null, options: [], min: null, max: null, selectedMin: null, selectedMax: null, yesCount: 8, yesSelected: false },
      ],
    },
    ...over,
  };
}

const render_ = (r: BrowseResult, params: Record<string, string> = { brand: "orvia", page: "2" }) =>
  render(<ProductListing locale="en" dict={{ listing: en.listing, product: en.product }} result={r} basePath="/en/c/phones" params={params} money={money} />);

describe("ProductListing", () => {
  it("shows the products, the count, sorts, chips and page links", () => {
    render_(result());

    expect(screen.getByText("50 products")).toBeTruthy();
    expect(screen.getByRole("link", { name: /Alpha/ }).getAttribute("href")).toBe("/en/p/1001/alpha");
    const sorts = screen.getByRole("navigation", { name: en.listing.sortLabel });
    expect(within(sorts).getByRole("link", { name: en.listing.sorts.newest }).getAttribute("aria-current")).toBe("true");
    expect(within(sorts).getByRole("link", { name: en.listing.sorts.price_asc }).getAttribute("href")).toBe("/en/c/phones?brand=orvia&sort=price_asc");
    expect(within(sorts).queryByRole("link", { name: en.listing.sorts.relevance })).toBeNull();

    expect(screen.getByRole("link", { name: en.listing.removeFilter.replace("{filter}", "Orvia") }).getAttribute("href")).toBe("/en/c/phones");
    const pages = screen.getByRole("navigation", { name: en.listing.pages });
    expect(within(pages).getByRole("link", { name: "2" }).getAttribute("aria-current")).toBe("page");
    expect(within(pages).getByRole("link", { name: "3" }).getAttribute("href")).toBe("/en/c/phones?brand=orvia&page=3");
  });

  it("offers facets as nofollow links with counts, disables empty ones and keeps other filters in range forms", () => {
    render_(result());
    const aside = screen.getByRole("complementary", { name: en.listing.filters });

    const orvia = within(aside).getByRole("link", { name: /Orvia/ });
    expect(orvia.getAttribute("rel")).toBe("nofollow");
    expect(orvia.getAttribute("aria-current")).toBe("true");
    expect(orvia.getAttribute("href")).toBe("/en/c/phones");
    expect(within(aside).queryByRole("link", { name: /Ranna/ })).toBeNull();
    expect(within(aside).getByText("Ranna")).toBeTruthy();
    expect(within(aside).getByRole("link", { name: /Android/ }).getAttribute("href")).toBe("/en/c/phones/android");
    expect(within(aside).getByRole("link", { name: new RegExp(en.listing.inStock) }).getAttribute("href")).toBe("/en/c/phones?brand=orvia&stock=1");
    expect(within(aside).getByRole("link", { name: /yes/ }).getAttribute("href")).toBe("/en/c/phones?brand=orvia&y.network_5g=1");

    const forms = aside.querySelectorAll("form");
    expect(forms).toHaveLength(2);
    const hidden = [...forms[0].querySelectorAll("input[type=hidden]")].map((i) => `${i.getAttribute("name")}=${i.getAttribute("value")}`);
    expect(hidden).toEqual(["brand=orvia"]);
    expect(forms[1].querySelector("input[name='nmin.screen_size']")).toBeTruthy();
  });

  it("explains an empty result and offers to drop the filter that hides most", () => {
    render_(result({ items: [], total: 0, relaxations: [{ filter: "brand", count: 7 }], facets: { ...result().facets, price: null } }));
    expect(screen.getByText(en.listing.empty)).toBeTruthy();
    expect(screen.getByRole("link", { name: en.listing.relaxFilter.replace("{filter}", en.listing.brands).replace("{count}", "7") }).getAttribute("href")).toBe("/en/c/phones");
    cleanup();
    render_(result({ items: [], total: 0, filtered: false }), {});
    expect(screen.getByText(en.listing.emptyScope)).toBeTruthy();
    expect(screen.queryByRole("navigation", { name: en.listing.pages })).toBeNull();
  });

  it("offers relevance first for a search", () => {
    render_(result({ scope: { ...result().scope, kind: "search" }, sort: "Relevance" }), { q: "phone" });
    const sorts = screen.getByRole("navigation", { name: en.listing.sortLabel });
    expect(within(sorts).getByRole("link", { name: en.listing.sorts.relevance }).getAttribute("href")).toBe("/en/c/phones?q=phone");
  });
});
