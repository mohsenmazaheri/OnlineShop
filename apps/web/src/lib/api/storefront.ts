import "server-only";
import { cacheLife, cacheTag } from "next/cache";
import { apiGet } from "./client";
import type { components } from "./schema";

export type StoreInfo = components["schemas"]["StoreInfoResponse"];
export type CategorySummary = components["schemas"]["CategorySummary"];

/** Cache tags; the API (or admin panel) revalidates them when the data changes (POST /api/revalidate). */
export const cacheTags = { store: "store", catalog: "catalog" } as const;

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
