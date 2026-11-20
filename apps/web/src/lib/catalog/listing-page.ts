import "server-only";
import type { Metadata } from "next";
import { browseProducts } from "@/lib/api/storefront";
import { canonicalPath, isIndexable, listingParams, toApiQuery, type BrowseResult, type ListingParams, type SearchParams } from "@/lib/catalog/browse";
import type { DisplayCurrency } from "@/lib/currency/display";
import { loadDisplayCurrency } from "@/lib/currency/load";
import { isLocale } from "@/lib/i18n/config";
import { getDictionary } from "@/lib/i18n/dictionaries";
import { siteUrl } from "@/lib/seo/config";
import { isTranslated, listingAlternates } from "@/lib/seo/listing";
import { loadStore } from "@/lib/store/load";

export type ListingScope = { c?: string; s?: string; b?: string; q?: string };

export type LoadedListing = {
  locale: string;
  store: Awaited<ReturnType<typeof loadStore>>;
  money: DisplayCurrency;
  params: ListingParams;
  result: BrowseResult;
  basePath: string;
};

/** A path segment as typed (Next.js may hand over percent-encoded non-Latin slugs). */
export function decodeSegment(segment: string): string {
  try {
    return decodeURIComponent(segment);
  } catch {
    return segment;
  }
}

/**
 * Loads a listing per request (the fetch is cached for minutes): the store, the reader's currency (typed prices are
 * in it), the address bar's filters, and the API's page. Null for an unknown address (the page 404s).
 */
export async function loadListing(locale: string, scope: ListingScope, searchParams: SearchParams, basePath?: string): Promise<LoadedListing | null> {
  if (!isLocale(locale)) return null;
  const store = await loadStore();
  const { money } = await loadDisplayCurrency(locale, store.currency);
  const params = listingParams(searchParams);
  if (scope.q) params.q = scope.q;
  const result = await browseProducts(toApiQuery(scope, params, locale, money.rate)).catch(() => null);
  if (!result) return null;
  return { locale, store, money, params, result, basePath: basePath ?? `/${locale}/${result.scope.path ?? "search"}` };
}

/**
 * Title, description, canonical and hreflang of a category or brand listing. Filtered or re-sorted lists, and a
 * language the listing is not translated into, are not indexed (they point at the plain list).
 */
export async function listingMetadata(listing: LoadedListing | null): Promise<Metadata> {
  if (!listing) return { robots: { index: false, follow: false } };
  const { locale, store, params, result, basePath } = listing;
  const dict = await getDictionary(locale as Parameters<typeof getDictionary>[0]);
  const title = result.scope.title ?? dict.listing.allProducts;
  const description = result.scope.description?.slice(0, 160) || dict.listing.metaDescription.replace("{title}", title).replace("{store}", store.name);
  const indexable = isIndexable(params) && isTranslated(result.scope, locale);
  return {
    title,
    description,
    alternates: listingAlternates(result.scope, locale, store.defaultLocale, siteUrl(), canonicalPath(basePath, params)),
    robots: indexable ? undefined : { index: false, follow: true },
    openGraph: { siteName: store.name, locale, type: "website", title, description },
  };
}
