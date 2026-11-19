import type { Metadata } from "next";
import type { ProductDetail } from "@/lib/api/storefront";
import { brandOf } from "@/lib/catalog/specs";
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

/**
 * schema.org Product + Offer for the page. Price, currency and availability are exactly what the page shows
 * (the same API response), as search engines require.
 */
export function productJsonLd(product: ProductDetail, pageUrl: string, currency: string, siteUrl: string) {
  const brand = brandOf(product.specs);
  return {
    "@context": "https://schema.org",
    "@type": "Product",
    name: product.name,
    ...(product.description ? { description: product.description } : {}),
    sku: product.sku,
    url: pageUrl,
    image: product.images.map((i) => absoluteUrl(siteUrl, i.url)),
    ...(brand ? { brand: { "@type": "Brand", name: brand } } : {}),
    offers: {
      "@type": "Offer",
      url: pageUrl,
      price: String(product.price),
      priceCurrency: currency,
      availability: product.inStock ? "https://schema.org/InStock" : "https://schema.org/OutOfStock",
      itemCondition: "https://schema.org/NewCondition",
    },
  };
}

/** JSON for a <script type="application/ld+json">: "<" escaped so product text can never close the script tag. */
export function jsonLdScript(data: unknown): string {
  return JSON.stringify(data).replace(/</g, "\\u003c");
}
