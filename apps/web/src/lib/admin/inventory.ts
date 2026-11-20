import type { components } from "@/lib/api/schema";
import { formatNumber } from "@/lib/i18n/format-money";

export type MovementKind = components["schemas"]["StockMovementKind"];

/** Movements staff enter by hand (sales and returns come from orders, opening lines from the ledger itself). */
export const manualKinds = ["Receipt", "Issue", "Adjustment"] as const satisfies readonly MovementKind[];

export const allKinds: readonly MovementKind[] = ["Opening", "Receipt", "Issue", "Sale", "Return", "Adjustment", "TransferOut", "TransferIn"];

/** Prefix of the references the ledger writes itself ("auto:editor"); anything else was typed by staff. */
const autoPrefix = "auto:";

/** The text for a reference: automatic ones translated, typed ones as they are. */
export function referenceLabel(reference: string | null | undefined, texts: Record<string, string>): string | null {
  if (!reference) return null;
  if (!reference.startsWith(autoPrefix)) return reference;
  return texts[reference.slice(autoPrefix.length)] ?? reference;
}

/** "+5" / "−3" in the locale's digits; the sign is always shown. */
export function signedQuantity(quantity: number, locale: string): string {
  const number = formatNumber(Math.abs(quantity), locale);
  return quantity > 0 ? `+${number}` : quantity < 0 ? `−${number}` : number;
}

/** A movement kind from the address bar, or undefined when it is not one. */
export function parseKind(value: string): MovementKind | undefined {
  return (allKinds as readonly string[]).includes(value) ? (value as MovementKind) : undefined;
}

/** The address of the ledger with these filters (empty ones left out). */
export function movementsHref(locale: string, filters: { sku?: string; warehouseId?: string; kind?: string; page?: number }): string {
  const params = new URLSearchParams();
  if (filters.sku) params.set("sku", filters.sku);
  if (filters.warehouseId) params.set("warehouseId", filters.warehouseId);
  if (filters.kind) params.set("kind", filters.kind);
  if (filters.page && filters.page > 1) params.set("page", String(filters.page));
  const query = params.toString();
  return `/${locale}/admin/inventory/movements${query ? `?${query}` : ""}`;
}

/** A GUID from the address bar, or undefined (so a bad value is never sent to the API). */
export function parseId(value: string): string | undefined {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) ? value : undefined;
}

/** The page links to show for a long list: the first, the last and two either side of the current one, in order. */
export function pageWindow(current: number, pages: number): number[] {
  const wanted = new Set([1, pages, current - 2, current - 1, current, current + 1, current + 2]);
  return [...wanted].filter((p) => p >= 1 && p <= pages).sort((a, b) => a - b);
}

export type StocktakeFilter = NonNullable<components["schemas"]["StocktakeLineFilter"]>;

export const stocktakeFilters: readonly StocktakeFilter[] = ["All", "Uncounted", "Counted", "Differences"];

/** A line filter from the address bar; anything else means all lines. */
export function parseStocktakeFilter(value: string): StocktakeFilter {
  return (stocktakeFilters as readonly string[]).includes(value) ? (value as StocktakeFilter) : "All";
}

/** "ST-12", as the ledger references a stocktake. */
export function stocktakeLabel(number: number): string {
  return `ST-${number}`;
}

/** The address of a stocktake's page with these line filters (defaults left out). */
export function stocktakeHref(locale: string, number: number, view: { filter?: StocktakeFilter; search?: string; page?: number } = {}): string {
  const params = new URLSearchParams();
  if (view.filter && view.filter !== "All") params.set("filter", view.filter);
  if (view.search) params.set("search", view.search);
  if (view.page && view.page > 1) params.set("page", String(view.page));
  const query = params.toString();
  return `/${locale}/admin/stocktakes/${number}${query ? `?${query}` : ""}`;
}

/** "PR-12", as the ledger references a purchase receipt. */
export function purchaseLabel(number: number): string {
  return `PR-${number}`;
}
