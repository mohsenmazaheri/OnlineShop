import { describe, expect, it } from "vitest";
import type { ProductDetail } from "@/lib/api/storefront";
import { absoluteUrl, isoDuration, jsonLdScript, productAlternates, productJsonLd } from "./product";

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
  images: [{ url: "/demo/products/phone-x1.svg", alt: null }],
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
  brand: null,
  variantAxes: [],
  variants: [],
  gtin: "4006381333931",
  mpn: "X1-BLK",
  weightGrams: 189,
  metaTitle: null,
  metaDescription: null,
  videos: [],
};

const offers = (data: ReturnType<typeof productJsonLd>) => ("offers" in data ? data.offers : undefined);

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
    expect(offers(data)).toMatchObject({ price: "349", priceCurrency: "EUR", availability: "https://schema.org/InStock" });
    expect(data.image).toEqual([`${site}/demo/products/phone-x1.svg`]);
    expect(data.brand).toEqual({ "@type": "Brand", name: "Orvia" });
    expect(data).toMatchObject({ gtin: "4006381333931", mpn: "X1-BLK", weight: { "@type": "QuantitativeValue", value: 189, unitCode: "GRM" } });
    expect(offers(productJsonLd({ ...product, inStock: false }, "u", "EUR", site))?.availability).toBe("https://schema.org/OutOfStock");
  });

  it("describes variants as a ProductGroup with converted prices, the brand itself winning over a brand spec", () => {
    const withVariants: ProductDetail = {
      ...product,
      brand: { name: "Orvia Inc.", slug: "orvia", logoUrl: null },
      variantAxes: [
        { code: "color", name: "Colour", options: [{ code: "black", value: "Black" }] },
        { code: "storage", name: "Storage", options: [{ code: "s128", value: "128 GB" }, { code: "s256", value: "256 GB" }] },
      ],
      variants: [
        { sku: "X1-128", gtin: "96385074", price: 349, compareAtPrice: 399, inStock: true, options: [{ axis: "color", option: "black" }, { axis: "storage", option: "s128" }] },
        { sku: "X1-256", gtin: null, price: 399, compareAtPrice: null, inStock: false, options: [{ axis: "color", option: "black" }, { axis: "storage", option: "s256" }] },
      ],
    };

    const data = productJsonLd(withVariants, "u", "USD", site, (p) => p * 2) as Record<string, unknown>;

    expect(data["@type"]).toBe("ProductGroup");
    expect(data).toMatchObject({ productGroupID: "DEMO-PHN-001", variesBy: ["https://schema.org/color"], brand: { name: "Orvia Inc." } });
    const [first, second] = data.hasVariant as Record<string, unknown>[];
    expect(first).toMatchObject({
      "@type": "Product",
      name: "Orvia X1 – Black / 128 GB",
      sku: "X1-128",
      gtin: "96385074",
      mpn: "X1-BLK",
      color: "Black",
      additionalProperty: [{ "@type": "PropertyValue", name: "Storage", value: "128 GB" }],
      offers: { price: "698", priceCurrency: "USD", availability: "https://schema.org/InStock" },
    });
    expect(second).toMatchObject({ offers: { price: "798", availability: "https://schema.org/OutOfStock" } });
  });

  it("describes videos as VideoObjects about the product", () => {
    const data = productJsonLd(
      { ...product, videos: [{ url: "/api/v1/media/" + "a".repeat(32) + "/video.mp4", posterUrl: null, title: "Unboxing", durationSeconds: 65.4, uploadedUtc: "2026-11-19T10:00:00Z" }] },
      "u",
      "EUR",
      site,
    ) as Record<string, unknown>;

    expect(data.subjectOf).toEqual([
      {
        "@type": "VideoObject",
        name: "Unboxing",
        description: "A phone.",
        contentUrl: `${site}/api/v1/media/${"a".repeat(32)}/video.mp4`,
        uploadDate: "2026-11-19T10:00:00Z",
        thumbnailUrl: `${site}/demo/products/phone-x1.svg`,
        duration: "PT1M5S",
      },
    ]);
    expect(isoDuration(3600)).toBe("PT1H");
    expect(isoDuration(0)).toBe("PT0S");
    expect(isoDuration(3725)).toBe("PT1H2M5S");
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
