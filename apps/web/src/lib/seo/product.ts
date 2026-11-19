import type { Metadata } from "next";
import type { ProductDetail } from "@/lib/api/storefront";
import { brandOf } from "@/lib/catalog/specs";
import { optionOf, variantLabel } from "@/lib/catalog/variants";
import { productPath } from "@/lib/catalog/product-path";
import type { Locale } from "@/lib/i18n/config";

type Alternates = NonNullable<Metadata["alternates"]>;

/**
 * canonical + hreflang for a product page (docs/SEO-MARKETING.md §3): hreflang only for languages the product is
 * really translated into (and the shop offers), x-default = the default language. A page in a language without a
 * translation shows default-language content, so it is noindex with its canonical on the default-language page.
 */
export function productAlternates(
  product: Pick<ProductDetail, "number" | "slug" | "alternates">,
  locale: Locale,
  storeLocales: readonly Locale[],
  defaultLocale: Locale,
  siteUrl: string,
): { alternates: Alternates; indexable: boolean } {
  const url = (l: string, slug: string) => `${siteUrl}${productPath(l, product.number, slug)}`;
  const offered = product.alternates.filter((a) => (storeLocales as readonly string[]).includes(a.locale));

  const languages: Record<string, string> = {};
  for (const a of offered) languages[a.locale] = url(a.locale, a.slug);
  const fallback = offered.find((a) => a.locale === defaultLocale) ?? offered[0];
  if (fallback) languages["x-default"] = url(fallback.locale, fallback.slug);

  const translated = offered.some((a) => a.locale === locale);
  const canonical = translated || !fallback ? url(locale, product.slug) : url(fallback.locale, fallback.slug);
  return { alternates: { canonical, languages }, indexable: translated };
}

/** Absolute URL for a site-relative image path (JSON-LD and Open Graph need absolute URLs). */
export function absoluteUrl(siteUrl: string, path: string): string {
  return /^https?:\/\//.test(path) ? path : `${siteUrl}${path.startsWith("/") ? "" : "/"}${path}`;
}

/** Variant specs schema.org knows as Product properties (Google reads them in ProductGroup.variesBy). */
const schemaProperties: Record<string, string> = { color: "color", colour: "color", size: "size", material: "material", pattern: "pattern" };

/**
 * schema.org Product + Offer for the page, or a ProductGroup with one Product per variant (Google's variant markup).
 * Prices, currency and availability are exactly what the page shows (the same API response, converted by
 * `priceOf` like the visible price), as search engines require.
 */
export function productJsonLd(product: ProductDetail, pageUrl: string, currency: string, siteUrl: string, priceOf: (basePrice: number) => number = (p) => p) {
  const brand = product.brand?.name ?? brandOf(product.specs);
  const image = product.images.map((i) => absoluteUrl(siteUrl, i.url));
  const offer = (price: number, inStock: boolean) => ({
    "@type": "Offer",
    url: pageUrl,
    price: String(priceOf(price)),
    priceCurrency: currency,
    availability: inStock ? "https://schema.org/InStock" : "https://schema.org/OutOfStock",
    itemCondition: "https://schema.org/NewCondition",
  });
  const common = {
    "@context": "https://schema.org",
    name: product.name,
    ...(product.description ? { description: product.description } : {}),
    url: pageUrl,
    image,
    ...(brand ? { brand: { "@type": "Brand", name: brand } } : {}),
  };

  const codes = {
    ...(product.mpn ? { mpn: product.mpn } : {}),
    ...(product.weightGrams ? { weight: { "@type": "QuantitativeValue", value: product.weightGrams, unitCode: "GRM" } } : {}),
  };
  if (product.variants.length === 0)
    return { ...common, ...codes, "@type": "Product", sku: product.sku, ...(product.gtin ? { gtin: product.gtin } : {}), offers: offer(product.price, product.inStock) };

  const known = product.variantAxes.filter((a) => schemaProperties[a.code]);
  const other = product.variantAxes.filter((a) => !schemaProperties[a.code]);
  const valueOf = (axis: (typeof product.variantAxes)[number], variant: (typeof product.variants)[number]) =>
    axis.options.find((o) => o.code === optionOf(variant, axis.code))?.value ?? "";
  return {
    ...common,
    "@type": "ProductGroup",
    productGroupID: product.sku,
    ...(known.length > 0 ? { variesBy: known.map((a) => `https://schema.org/${schemaProperties[a.code]}`) } : {}),
    hasVariant: product.variants.map((variant) => ({
      "@type": "Product",
      name: `${product.name} – ${variantLabel(variant, product.variantAxes)}`,
      sku: variant.sku,
      ...(variant.gtin ? { gtin: variant.gtin } : {}),
      ...codes,
      image,
      ...Object.fromEntries(known.map((a) => [schemaProperties[a.code], valueOf(a, variant)])),
      ...(other.length > 0
        ? { additionalProperty: other.map((a) => ({ "@type": "PropertyValue", name: a.name, value: valueOf(a, variant) })) }
        : {}),
      offers: offer(variant.price, variant.inStock),
    })),
  };
}

/** JSON for a <script type="application/ld+json">: "<" escaped so product text can never close the script tag. */
export function jsonLdScript(data: unknown): string {
  return JSON.stringify(data).replace(/</g, "\\u003c");
}
