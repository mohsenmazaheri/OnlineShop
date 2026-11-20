import "server-only";
import { cacheLife, cacheTag } from "next/cache";
import { ApiError, apiGet } from "./client";
import type { CurrencySettings } from "@/lib/currency/display";
import type { components } from "./schema";

export type StoreInfo = components["schemas"]["StoreInfoResponse"];
export type CategorySummary = components["schemas"]["CategorySummary"];

/** Cache tags; the API (or admin panel) revalidates them when the data changes (POST /api/revalidate). */
export const cacheTags = { store: "store", catalog: "catalog", currencies: "currencies" } as const;

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
  cacheTag(cacheTags.catalog);
  cacheLife("hours");
  return apiGet<ProductCard[]>(`/api/v1/catalog/products?list=${list}&locale=${encodeURIComponent(locale)}&take=${take}`);
}

/** One published product, or null when there is none with this number (unpublished, deleted, never existed). */
export async function getProduct(number: number, locale: string): Promise<ProductDetail | null> {
  "use cache";
  cacheTag(cacheTags.catalog, productTag(number));
  cacheLife("hours");
  try {
    return await apiGet<ProductDetail>(`/api/v1/catalog/products/${number}?locale=${encodeURIComponent(locale)}`);
  } catch (error) {
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
  cacheTag(cacheTags.catalog);
  cacheLife("minutes");
  try {
    return await apiGet<BrowseResult>(`/api/v1/catalog/browse?${query}`);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) return null;
    throw error;
  }
}
