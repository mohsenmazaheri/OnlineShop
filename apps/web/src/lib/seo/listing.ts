import type { Metadata } from "next";
import type { BrowseResult } from "@/lib/catalog/browse";
import { productPath } from "@/lib/catalog/product-path";

type Alternates = NonNullable<Metadata["alternates"]>;

/**
 * canonical + hreflang of a category or brand listing: only the languages it is translated into (the API lists them),
 * x-default on the shop's default language. The canonical is the plain listing (filters and sort point to it), with
 * its page number from page 2 on.
 */
export function listingAlternates(scope: BrowseResult["scope"], locale: string, defaultLocale: string, siteUrl: string, canonical: string): Alternates {
  const languages: Record<string, string> = {};
  for (const a of scope.alternates) languages[a.locale] = `${siteUrl}/${a.locale}/${a.path}`;
  const fallback = scope.alternates.find((a) => a.locale === defaultLocale) ?? scope.alternates[0];
  if (fallback) languages["x-default"] = `${siteUrl}/${fallback.locale}/${fallback.path}`;
  return { canonical: `${siteUrl}${canonical}`, languages };
}

/** Whether the listing has a version in this language (otherwise it shows default-language names: not indexed). */
export function isTranslated(scope: BrowseResult["scope"], locale: string): boolean {
  return scope.alternates.some((a) => a.locale === locale);
}

/** schema.org BreadcrumbList (home first) and ItemList of the products on the page, in the order shown. */
export function listingJsonLd(result: BrowseResult, locale: string, siteUrl: string, homeName: string, pageUrl: string) {
  const crumbs = [{ name: homeName, url: `${siteUrl}/${locale}` }, ...result.scope.breadcrumb.map((c) => ({ name: c.name, url: `${siteUrl}/${locale}/${c.path}` }))];
  const offset = (result.page - 1) * result.pageSize;
  return [
    {
      "@context": "https://schema.org",
      "@type": "BreadcrumbList",
      itemListElement: crumbs.map((c, i) => ({ "@type": "ListItem", position: i + 1, name: c.name, item: c.url })),
    },
    {
      "@context": "https://schema.org",
      "@type": "ItemList",
      url: pageUrl,
      numberOfItems: result.total,
      itemListElement: result.items.map((p, i) => ({ "@type": "ListItem", position: offset + i + 1, url: `${siteUrl}${productPath(locale, p.number, p.slug)}`, name: p.name })),
    },
  ];
}
