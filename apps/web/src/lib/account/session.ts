import "server-only";
import { cookies } from "next/headers";
import { apiBaseUrl } from "@/lib/api/base-url";
import type { components } from "@/lib/api/schema";
import { sessionCookieName } from "./cookie";

export type CurrentUser = components["schemas"]["CurrentUser"];
export type ExternalLoginsView = components["schemas"]["ExternalLoginsView"];
export type TwoFactorStatus = components["schemas"]["TwoFactorStatus"];
export type Address = components["schemas"]["AddressView"];
export type StaffOverview = components["schemas"]["StaffOverview"];
export type StaffAuditEntry = components["schemas"]["StaffAuditView"];
export type MyStaffInvitation = components["schemas"]["MyStaffInvitation"];

/**
 * GET an account or admin endpoint for server rendering, as the signed-in user. Forwards only the session cookie.
 * Never cached: the answer is per user and must not leak between visitors. Null without a session, on any
 * error status, or when the API cannot be reached.
 */
async function getForUser<T>(path: `/api/v1/${"account" | "admin"}${string}`, fetchImpl: typeof fetch): Promise<T | null> {
  const session = (await cookies()).get(sessionCookieName);
  if (!session) return null;
  try {
    const response = await fetchImpl(`${apiBaseUrl()}${path}`, {
      headers: { Accept: "application/json", Cookie: `${sessionCookieName}=${session.value}` },
      cache: "no-store",
      signal: AbortSignal.timeout(5000),
    });
    return response.ok ? ((await response.json()) as T) : null;
  } catch {
    return null;
  }
}

/** The signed-in user, or null. */
export function getCurrentUser(fetchImpl: typeof fetch = fetch): Promise<CurrentUser | null> {
  return getForUser<CurrentUser>("/api/v1/account/me", fetchImpl);
}

/** Providers the shop offers and the ones linked to the signed-in account. */
export function getExternalLogins(fetchImpl: typeof fetch = fetch): Promise<ExternalLoginsView | null> {
  return getForUser<ExternalLoginsView>("/api/v1/account/external", fetchImpl);
}

/** Two-step sign-in status of the signed-in account. */
export function getTwoFactorStatus(fetchImpl: typeof fetch = fetch): Promise<TwoFactorStatus | null> {
  return getForUser<TwoFactorStatus>("/api/v1/account/2fa", fetchImpl);
}

/** The signed-in customer's address book (default first). */
export function getAddresses(fetchImpl: typeof fetch = fetch): Promise<Address[] | null> {
  return getForUser<Address[]>("/api/v1/account/addresses", fetchImpl);
}

/** Open staff invitations for the signed-in user (matched by their verified email or mobile number). */
export function getMyStaffInvitations(fetchImpl: typeof fetch = fetch): Promise<MyStaffInvitation[] | null> {
  return getForUser<MyStaffInvitation[]>("/api/v1/account/staff-invitations", fetchImpl);
}

/** Staff members and recent invitations (needs the staff.manage permission; null otherwise). */
export function getStaffOverview(fetchImpl: typeof fetch = fetch): Promise<StaffOverview | null> {
  return getForUser<StaffOverview>("/api/v1/admin/staff", fetchImpl);
}

/** The latest staff changes (needs the staff.manage permission; null otherwise). */
export function getStaffAudit(fetchImpl: typeof fetch = fetch): Promise<StaffAuditEntry[] | null> {
  return getForUser<StaffAuditEntry[]>("/api/v1/admin/staff/audit", fetchImpl);
}

export type CurrencyAdminOverview = components["schemas"]["CurrencyAdminOverview"];

/** Currencies, rates and language defaults (needs the settings.manage permission; null otherwise). */
export function getCurrencyAdmin(fetchImpl: typeof fetch = fetch): Promise<CurrencyAdminOverview | null> {
  return getForUser<CurrencyAdminOverview>("/api/v1/admin/currencies", fetchImpl);
}

export type BrandingAdmin = components["schemas"]["BrandingView"];

/** The shop's look as saved (needs the settings.manage permission; null otherwise). */
export function getBrandingAdmin(fetchImpl: typeof fetch = fetch): Promise<BrandingAdmin | null> {
  return getForUser<BrandingAdmin>("/api/v1/admin/branding", fetchImpl);
}

export type CatalogTree = components["schemas"]["CatalogTree"];
export type ProductPage = components["schemas"]["ProductPage"];
export type ProductEditView = components["schemas"]["ProductEditView"];
export type SpecChoice = components["schemas"]["SpecChoice"];

/** The category tree with texts in every shop language (needs catalog.manage; null otherwise). */
export function getCatalogTree(fetchImpl: typeof fetch = fetch): Promise<CatalogTree | null> {
  return getForUser<CatalogTree>("/api/v1/admin/catalog", fetchImpl);
}

/** One page of the admin product list. */
export function getAdminProducts(query: { search?: string; groupId?: string; page?: number; locale: string }, fetchImpl: typeof fetch = fetch): Promise<ProductPage | null> {
  const params = new URLSearchParams({ locale: query.locale, page: String(query.page ?? 1) });
  if (query.search) params.set("search", query.search);
  if (query.groupId) params.set("groupId", query.groupId);
  return getForUser<ProductPage>(`/api/v1/admin/products?${params.toString()}`, fetchImpl);
}

/** A product for the editor, or null (unknown number or no permission). */
export function getAdminProduct(number: number, fetchImpl: typeof fetch = fetch): Promise<ProductEditView | null> {
  return getForUser<ProductEditView>(`/api/v1/admin/products/${number}`, fetchImpl);
}

/** The specs the product editor offers, named in the admin's language. */
export function getSpecChoices(locale: string, fetchImpl: typeof fetch = fetch): Promise<SpecChoice[] | null> {
  return getForUser<SpecChoice[]>(`/api/v1/admin/products/specs?locale=${encodeURIComponent(locale)}`, fetchImpl);
}

export type BrandList = components["schemas"]["BrandList"];
export type SpecList = components["schemas"]["SpecList"];

/** Every brand with its texts in every shop language (admin). */
export function getBrands(fetchImpl: typeof fetch = fetch): Promise<BrandList | null> {
  return getForUser<BrandList>("/api/v1/admin/brands", fetchImpl);
}

/** The spec library: every spec with its texts, options and usage (admin). */
export function getSpecLibrary(fetchImpl: typeof fetch = fetch): Promise<SpecList | null> {
  return getForUser<SpecList>("/api/v1/admin/specs", fetchImpl);
}

export type SynonymList = components["schemas"]["SynonymList"];
export type RedirectView = components["schemas"]["RedirectView"];

/** Every search synonym group (admin). */
export function getSynonyms(fetchImpl: typeof fetch = fetch): Promise<SynonymList | null> {
  return getForUser<SynonymList>("/api/v1/admin/seo/synonyms", fetchImpl);
}

/** Every redirect with its usage (admin). */
export function getRedirects(fetchImpl: typeof fetch = fetch): Promise<RedirectView[] | null> {
  return getForUser<RedirectView[]>("/api/v1/admin/seo/redirects", fetchImpl);
}

export type WarehouseView = components["schemas"]["WarehouseView"];
export type StockPage = components["schemas"]["StockPage"];
export type MovementPage = components["schemas"]["MovementPage"];

/** The shop's warehouses, the default first (inventory.view). */
export function getWarehouses(fetchImpl: typeof fetch = fetch): Promise<WarehouseView[] | null> {
  return getForUser<WarehouseView[]>("/api/v1/admin/inventory/warehouses", fetchImpl);
}

/** One page of stock per item and warehouse (inventory.view). */
export function getStock(
  query: { search?: string; onlyOut?: boolean; onlyLow?: boolean; page?: number; locale: string },
  fetchImpl: typeof fetch = fetch,
): Promise<StockPage | null> {
  const params = new URLSearchParams({ locale: query.locale, page: String(query.page ?? 1) });
  if (query.search) params.set("search", query.search);
  if (query.onlyOut) params.set("onlyOut", "true");
  if (query.onlyLow) params.set("onlyLow", "true");
  return getForUser<StockPage>(`/api/v1/admin/inventory/stock?${params.toString()}`, fetchImpl);
}

/** One page of the stock ledger, newest first (inventory.view). */
export function getMovements(
  query: { sku?: string; warehouseId?: string; kind?: string; page?: number; locale: string },
  fetchImpl: typeof fetch = fetch,
): Promise<MovementPage | null> {
  const params = new URLSearchParams({ locale: query.locale, page: String(query.page ?? 1) });
  if (query.sku) params.set("sku", query.sku);
  if (query.warehouseId) params.set("warehouseId", query.warehouseId);
  if (query.kind) params.set("kind", query.kind);
  return getForUser<MovementPage>(`/api/v1/admin/inventory/movements?${params.toString()}`, fetchImpl);
}

export type StocktakeSummary = components["schemas"]["StocktakeSummary"];
export type StocktakeView = components["schemas"]["StocktakeView"];

/** Every stocktake, newest first (inventory.view). */
export function getStocktakes(fetchImpl: typeof fetch = fetch): Promise<StocktakeSummary[] | null> {
  return getForUser<StocktakeSummary[]>("/api/v1/admin/inventory/stocktakes", fetchImpl);
}

/** One stocktake with a page of its lines, or null (unknown number or no permission). */
export function getStocktake(
  number: number,
  query: { filter?: string; search?: string; page?: number; locale: string },
  fetchImpl: typeof fetch = fetch,
): Promise<StocktakeView | null> {
  const params = new URLSearchParams({ locale: query.locale, page: String(query.page ?? 1) });
  if (query.filter) params.set("filter", query.filter);
  if (query.search) params.set("search", query.search);
  return getForUser<StocktakeView>(`/api/v1/admin/inventory/stocktakes/${number}?${params.toString()}`, fetchImpl);
}

export type SupplierView = components["schemas"]["SupplierView"];
export type ReceiptSummary = components["schemas"]["ReceiptSummary"];
export type ReceiptView = components["schemas"]["ReceiptView"];

/** Every supplier, active first (inventory.view). */
export function getSuppliers(fetchImpl: typeof fetch = fetch): Promise<SupplierView[] | null> {
  return getForUser<SupplierView[]>("/api/v1/admin/inventory/suppliers", fetchImpl);
}

/** Purchase receipts, newest first (inventory.view). */
export function getPurchases(fetchImpl: typeof fetch = fetch): Promise<ReceiptSummary[] | null> {
  return getForUser<ReceiptSummary[]>("/api/v1/admin/inventory/purchases", fetchImpl);
}

/** One purchase receipt with its lines, or null. */
export function getPurchase(number: number, locale: string, fetchImpl: typeof fetch = fetch): Promise<ReceiptView | null> {
  return getForUser<ReceiptView>(`/api/v1/admin/inventory/purchases/${number}?locale=${encodeURIComponent(locale)}`, fetchImpl);
}

export type ActiveReservation = components["schemas"]["ActiveReservationView"];

/** Units held for unpaid checkouts, the oldest first (inventory.view). */
export function getReservations(locale: string, fetchImpl: typeof fetch = fetch): Promise<ActiveReservation[] | null> {
  return getForUser<ActiveReservation[]>(`/api/v1/admin/inventory/reservations?locale=${encodeURIComponent(locale)}`, fetchImpl);
}

export type SearchIndexStatus = components["schemas"]["SearchIndexStatus"];

/** How many search documents exist and how many catalog changes wait (catalog.manage). */
export function getSearchIndexStatus(fetchImpl: typeof fetch = fetch): Promise<SearchIndexStatus | null> {
  return getForUser<SearchIndexStatus>("/api/v1/admin/seo/search-index", fetchImpl);
}

export type CompareList = components["schemas"]["CompareListView"];

/** The signed-in customer's comparison list (product numbers), for the comparison page opened without ?ids. */
export function getCompareList(fetchImpl: typeof fetch = fetch): Promise<CompareList | null> {
  return getForUser<CompareList>("/api/v1/account/compare", fetchImpl);
}

export type WishlistCard = components["schemas"]["ProductCard"];

/** The signed-in customer's wishlist as product cards (most recently added first), or null without a session. */
export function getWishlist(locale: string, fetchImpl: typeof fetch = fetch): Promise<WishlistCard[] | null> {
  return getForUser<WishlistCard[]>(`/api/v1/account/wishlist?locale=${encodeURIComponent(locale)}`, fetchImpl);
}

export type ReviewAdminList = components["schemas"]["ReviewAdminList"];
export type ReviewStatusName = components["schemas"]["ReviewStatus"];

/** Reviews for moderation (reviews.moderate): one status (all when null), a page of them. */
export function getReviewsForModeration(status: ReviewStatusName | null, page: number, fetchImpl: typeof fetch = fetch): Promise<ReviewAdminList | null> {
  const query = new URLSearchParams({ page: String(page) });
  if (status) query.set("status", status);
  return getForUser<ReviewAdminList>(`/api/v1/admin/reviews?${query}`, fetchImpl);
}

export type LandingPageList = components["schemas"]["LandingPageList"];

/** Every landing page with its texts (content.manage), or null without the permission. */
export function getLandingPages(fetchImpl: typeof fetch = fetch): Promise<LandingPageList | null> {
  return getForUser<LandingPageList>("/api/v1/admin/landing-pages", fetchImpl);
}

export type ContentList = components["schemas"]["ContentList"];

/** Every blog post and content page (content.manage), or null without the permission. */
export function getContentList(fetchImpl: typeof fetch = fetch): Promise<ContentList | null> {
  return getForUser<ContentList>("/api/v1/admin/content", fetchImpl);
}
