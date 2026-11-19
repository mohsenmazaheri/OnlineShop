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
