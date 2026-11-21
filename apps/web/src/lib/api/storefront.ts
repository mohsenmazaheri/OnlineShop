import "server-only";
import { cacheLife, cacheTag } from "next/cache";
import { ApiError, apiGet } from "./client";
import type { CurrencySettings } from "@/lib/currency/display";
import { saleAwareLife } from "@/lib/catalog/sale";
import type { components } from "./schema";

export type StoreInfo = components["schemas"]["StoreInfoResponse"];
export type CategorySummary = components["schemas"]["CategorySummary"];

/** Cache tags; the API (or admin panel) revalidates them when the data changes (POST /api/revalidate). */
export const cacheTags = { store: "store", catalog: "catalog", currencies: "currencies", reviews: "reviews" } as const;

export async function getStoreInfo(): Promise<StoreInfo> {
  "use cache";
  cacheTag(cacheTags.store);
  cacheLife("hours");
  return apiGet<StoreInfo>("/api/v1/store");
}

/** Categories named in the given language (the API falls back to the default language); cached per locale. */
export async function getCategoryMenu(locale: string): Promise<CategorySummary[]> {
  "use cache";
  cacheTag(cacheTags.catalog);
  cacheLife("hours");
  return apiGet<CategorySummary[]>(`/api/v1/catalog/categories?locale=${encodeURIComponent(locale)}`);
}

export type ProductCard = components["schemas"]["ProductCard"];
export type ProductDetail = components["schemas"]["ProductDetail"];
export type ProductListName = components["schemas"]["ProductList"];

/** Cache tag of one product (revalidated when it changes). */
export const productTag = (number: number) => `product-${number}`;

/** Published products for a home-page rail, named in the given language. */
export async function getProducts(list: ProductListName, locale: string, take = 12): Promise<ProductCard[]> {
  "use cache";
  cacheTag(cacheTags.catalog, cacheTags.reviews);
  const products = await apiGet<ProductCard[]>(`/api/v1/catalog/products?list=${list}&locale=${encodeURIComponent(locale)}&take=${take}`);
  // An offer price must not outlive its offer.
  cacheForOffers(products.map((p) => p.saleEndsUtc), "hours");
  return products;
}

/** One published product, or null when there is none with this number (unpublished, deleted, never existed). */
export async function getProduct(number: number, locale: string): Promise<ProductDetail | null> {
  "use cache";
  cacheTag(cacheTags.catalog, cacheTags.reviews, productTag(number));
  try {
    const product = await apiGet<ProductDetail>(`/api/v1/catalog/products/${number}?locale=${encodeURIComponent(locale)}`);
    cacheForOffers([product.saleEndsUtc], "hours");
    return product;
  } catch (error) {
    cacheLife("minutes");
    if (error instanceof ApiError && error.status === 404) return null;
    throw error;
  }
}

/** Display currencies with their rates; short-lived so the admin's changes and new rates show within minutes. */
export async function getCurrencySettings(): Promise<CurrencySettings> {
  "use cache";
  cacheTag(cacheTags.currencies);
  cacheLife("minutes");
  return apiGet<CurrencySettings>("/api/v1/currencies");
}

export type BrowseResult = components["schemas"]["BrowseResult"];

/**
 * A product listing (category, brand, search) with facets, or null when the address names nothing (404). Cached for
 * minutes per query: availability follows stock, which changes without a catalog event.
 */
export async function browseProducts(query: string): Promise<BrowseResult | null> {
  "use cache";
  cacheTag(cacheTags.catalog, cacheTags.reviews);
  try {
    const result = await apiGet<BrowseResult>(`/api/v1/catalog/browse?${query}`);
    cacheForOffers(result.items.map((p) => p.saleEndsUtc), "minutes");
    return result;
  } catch (error) {
    cacheLife("minutes");
    if (error instanceof ApiError && error.status === 404) return null;
    throw error;
  }
}

export type ProductComparison = components["schemas"]["ProductComparison"];

/**
 * Products side by side. Not cached: the table shows the current price and stock. The numbers are already parsed
 * (digits only, at most four).
 */
export async function getComparison(numbers: readonly number[], locale: string): Promise<ProductComparison> {
  return apiGet<ProductComparison>(`/api/v1/catalog/compare?numbers=${numbers.join(",")}&locale=${encodeURIComponent(locale)}`);
}

export type ProductReviews = components["schemas"]["ProductReviews"];

/** A page of a product's approved reviews (moderation clears the "reviews" tag). Null for an unknown product. */
export async function getProductReviews(number: number, page: number): Promise<ProductReviews | null> {
  "use cache";
  cacheTag(cacheTags.reviews, productTag(number));
  cacheLife("minutes");
  try {
    return await apiGet<ProductReviews>(`/api/v1/catalog/products/${number}/reviews?page=${page}`);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) return null;
    throw error;
  }
}

export type SitemapEntry = components["schemas"]["SitemapEntry"];
export type SitemapKind = components["schemas"]["SitemapKind"];

/** The pages of one sitemap with their address in every translated language. */
export async function getSitemapEntries(kind: SitemapKind): Promise<SitemapEntry[]> {
  "use cache";
  cacheTag(cacheTags.catalog);
  cacheLife("hours");
  return apiGet<SitemapEntry[]>(`/api/v1/catalog/sitemap/${kind}`);
}

export type LandingPageContent = components["schemas"]["LandingPageContent"];

/** A published landing page by its slug (any language), in the given language; null when there is none. */
export async function getLandingPage(slug: string, locale: string): Promise<LandingPageContent | null> {
  "use cache";
  cacheTag(cacheTags.catalog);
  cacheLife("hours");
  try {
    return await apiGet<LandingPageContent>(`/api/v1/catalog/landing/${encodeURIComponent(slug)}?locale=${encodeURIComponent(locale)}`);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) return null;
    throw error;
  }
}

/** cacheLife for data with offer end times (one call per cached function; see saleAwareLife). */
function cacheForOffers(ends: readonly (string | null | undefined)[], profile: "minutes" | "hours") {
  const life = saleAwareLife(ends, profile);
  if (life === "hours") cacheLife("hours");
  else if (life === "minutes") cacheLife("minutes");
  else cacheLife(life);
}
