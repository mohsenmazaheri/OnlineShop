import { describe, expect, it } from "vitest";
import type { BrowseResult } from "@/lib/catalog/browse";
import { isTranslated, listingAlternates, listingJsonLd } from "./listing";

const scope: BrowseResult["scope"] = {
  kind: "category",
  title: "Phones",
  description: null,
  imageUrl: null,
  path: "c/phones",
  breadcrumb: [{ name: "Phones", path: "c/phones" }],
  alternates: [
    { locale: "fa", path: "c/موبایل" },
    { locale: "en", path: "c/phones" },
  ],
};

describe("listing SEO", () => {
  it("links only translated languages and points x-default at the default language", () => {
    const alternates = listingAlternates(scope, "en", "fa", "https://shop.test", "/en/c/phones?page=2");
    expect(alternates.canonical).toBe("https://shop.test/en/c/phones?page=2");
    expect(alternates.languages).toEqual({
      fa: "https://shop.test/fa/c/موبایل",
      en: "https://shop.test/en/c/phones",
      "x-default": "https://shop.test/fa/c/موبایل",
    });
    expect(isTranslated(scope, "en")).toBe(true);
    expect(isTranslated(scope, "de")).toBe(false);
  });

  it("describes the breadcrumb and the products of this page in order", () => {
    const result = {
      scope,
      items: [{ number: 1005, name: "Phone", slug: "phone", imageUrl: null, price: 10, compareAtPrice: null, inStock: true }],
      total: 30,
      page: 2,
      pageSize: 24,
      sort: "Newest",
      filtered: false,
      facets: {} as BrowseResult["facets"],
    } as BrowseResult;
    const [breadcrumb, list] = listingJsonLd(result, "en", "https://shop.test", "Home", "https://shop.test/en/c/phones?page=2") as unknown as [
      { itemListElement: { position: number; item: string }[] },
      { numberOfItems: number; itemListElement: { position: number; url: string }[] },
    ];
    expect(breadcrumb.itemListElement.map((i) => i.item)).toEqual(["https://shop.test/en", "https://shop.test/en/c/phones"]);
    expect(list.numberOfItems).toBe(30);
    expect(list.itemListElement[0]).toMatchObject({ position: 25, url: "https://shop.test/en/p/1005/phone" });
  });
});
