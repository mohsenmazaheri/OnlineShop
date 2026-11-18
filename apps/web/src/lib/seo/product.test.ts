import { describe, expect, it } from "vitest";
import type { ProductDetail } from "@/lib/api/storefront";
import { absoluteUrl, jsonLdScript, productAlternates, productJsonLd } from "./product";

const site = "https://shop.example.com";

const product: ProductDetail = {
  number: 1001,
  name: "Orvia X1",
  slug: "orvia-x1",
  description: "A phone.",
  sku: "DEMO-PHN-001",
  price: 349,
  compareAtPrice: 399,
  stock: 3,
  inStock: true,
  images: ["/demo/products/phone-x1.svg"],
  category: { name: "Electronics", slug: "electronics" },
  subCategory: { name: "Phones", slug: "phones" },
  specs: [
    { code: "brand", group: "General", name: "Brand", kind: "Text", unit: null, number: null, boolean: null, value: "Orvia", inSummary: true },
  ],
  alternates: [
    { locale: "fa", slug: "گوشی" },
    { locale: "en", slug: "orvia-x1" },
    { locale: "de", slug: "orvia-x1-de" },
  ],
};

describe("productAlternates", () => {
  it("lists only translated languages the shop offers, with x-default on the default language", () => {
    const { alternates, indexable } = productAlternates(product, "en", ["fa", "en", "hy"], "fa", site);

    expect(indexable).toBe(true);
    expect(alternates.canonical).toBe(`${site}/en/p/1001/orvia-x1`);
    expect(alternates.languages).toEqual({
      fa: `${site}/fa/p/1001/%DA%AF%D9%88%D8%B4%DB%8C`,
      en: `${site}/en/p/1001/orvia-x1`,
      "x-default": `${site}/fa/p/1001/%DA%AF%D9%88%D8%B4%DB%8C`,
    });
  });

  it("makes an untranslated language noindex with the canonical on the default language", () => {
    const { alternates, indexable } = productAlternates(product, "hy", ["fa", "en", "hy"], "fa", site);

    expect(indexable).toBe(false);
    expect(alternates.canonical).toBe(`${site}/fa/p/1001/%DA%AF%D9%88%D8%B4%DB%8C`);
    expect(alternates.languages).not.toHaveProperty("hy");
  });
});

describe("productJsonLd", () => {
  it("matches the shown price, currency and stock", () => {
    const data = productJsonLd(product, `${site}/en/p/1001/orvia-x1`, "EUR", site);

    expect(data["@type"]).toBe("Product");
    expect(data.offers).toMatchObject({ price: "349", priceCurrency: "EUR", availability: "https://schema.org/InStock" });
    expect(data.image).toEqual([`${site}/demo/products/phone-x1.svg`]);
    expect(data.brand).toEqual({ "@type": "Brand", name: "Orvia" });
    expect(productJsonLd({ ...product, inStock: false }, "u", "EUR", site).offers.availability).toBe("https://schema.org/OutOfStock");
  });

  it("is safe to embed in a script tag", () => {
    const script = jsonLdScript(productJsonLd({ ...product, name: "</script><script>alert(1)</script>" }, "u", "EUR", site));

    expect(script).not.toContain("</script>");
    expect(JSON.parse(script).name).toBe("</script><script>alert(1)</script>");
  });
});

describe("absoluteUrl", () => {
  it("keeps absolute URLs and prefixes site-relative paths", () => {
    expect(absoluteUrl(site, "https://cdn.example.com/a.png")).toBe("https://cdn.example.com/a.png");
    expect(absoluteUrl(site, "/demo/a.svg")).toBe(`${site}/demo/a.svg`);
    expect(absoluteUrl(site, "demo/a.svg")).toBe(`${site}/demo/a.svg`);
  });
});
