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

/** The reviews shown on the page, for Review markup (only approved, real reviews ever reach the page). */
export type JsonLdReviews = { items: { author: string; rating: number; title: string | null; body: string; date: string }[] };

/**
 * schema.org Product + Offer for the page, or a ProductGroup with one Product per variant (Google's variant markup).
 * Prices, currency and availability are exactly what the page shows (the same API response, converted by
 * `priceOf` like the visible price), as search engines require; so are the rating and the reviews.
 */
export function productJsonLd(
  product: ProductDetail,
  pageUrl: string,
  currency: string,
  siteUrl: string,
  priceOf: (basePrice: number) => number = (p) => p,
  reviews: JsonLdReviews = { items: [] },
) {
  const brand = product.brand?.name ?? brandOf(product.specs);
  const image = product.images.map((i) => absoluteUrl(siteUrl, i.url));
  const offer = (price: number, inStock: boolean) => ({
    "@type": "Offer",
    url: pageUrl,
    price: String(priceOf(price)),
    priceCurrency: currency,
    availability: inStock ? "https://schema.org/InStock" : "https://schema.org/OutOfStock",
    itemCondition: "https://schema.org/NewCondition",
    // A special offer: the price is valid until the offer ends.
    ...(product.saleEndsUtc ? { priceValidUntil: product.saleEndsUtc.slice(0, 10) } : {}),
  });
  // Each video as a VideoObject about this product (thumbnail: its poster, else the main image).
  const videos = product.videos.map((video) => {
    const thumbnail = video.posterUrl ?? product.images[0]?.url;
    return {
      "@type": "VideoObject",
      name: video.title || product.name,
      description: product.description || video.title || product.name,
      contentUrl: absoluteUrl(siteUrl, video.url),
      uploadDate: video.uploadedUtc,
      ...(thumbnail ? { thumbnailUrl: absoluteUrl(siteUrl, thumbnail) } : {}),
      ...(video.durationSeconds ? { duration: isoDuration(video.durationSeconds) } : {}),
    };
  });
  const common = {
    "@context": "https://schema.org",
    name: product.name,
    ...(product.description ? { description: product.description } : {}),
    url: pageUrl,
    image,
    ...(brand ? { brand: { "@type": "Brand", name: brand } } : {}),
    ...(videos.length > 0 ? { subjectOf: videos } : {}),
    // The rating shown next to the title, and the reviews listed below (Google ignores ratings that are not visible).
    ...(product.ratingCount > 0
      ? { aggregateRating: { "@type": "AggregateRating", ratingValue: product.ratingAverage, reviewCount: product.ratingCount, bestRating: 5, worstRating: 1 } }
      : {}),
    ...(reviews.items.length > 0
      ? {
          review: reviews.items.map((r) => ({
            "@type": "Review",
            author: { "@type": "Person", name: r.author },
            reviewRating: { "@type": "Rating", ratingValue: r.rating, bestRating: 5, worstRating: 1 },
            ...(r.title ? { name: r.title } : {}),
            reviewBody: r.body,
            datePublished: r.date,
          })),
        }
      : {}),
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

/** ISO 8601 duration for schema.org ("PT1M5S"). */
export function isoDuration(seconds: number): string {
  const total = Math.max(0, Math.round(seconds));
  const [h, m, s] = [Math.floor(total / 3600), Math.floor((total % 3600) / 60), total % 60];
  return `PT${h ? `${h}H` : ""}${m ? `${m}M` : ""}${s || (!h && !m) ? `${s}S` : ""}`;
}

/** JSON for a <script type="application/ld+json">: "<" escaped so product text can never close the script tag. */
export function jsonLdScript(data: unknown): string {
  return JSON.stringify(data).replace(/</g, "\\u003c");
}
