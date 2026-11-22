import "server-only";
import { cookies } from "next/headers";
import { sessionCookieName } from "@/lib/account/cookie";
import { apiBaseUrl } from "@/lib/api/base-url";
import type { components } from "@/lib/api/schema";

export type CartView = components["schemas"]["CartView"];
export type CartLine = components["schemas"]["CartLine"];

/** The guest cart's cookie (HttpOnly, set by the API on the first add). */
export const cartCookieName = "os_cart";

/** The cookies the cart answer depends on, as a Cookie header (the session and the guest cart only). */
export function cartCookieHeader(values: { session?: string; cart?: string }): string {
  return [values.session && `${sessionCookieName}=${values.session}`, values.cart && `${cartCookieName}=${values.cart}`].filter(Boolean).join("; ");
}

/**
 * GET an endpoint that answers per visitor (the cart, checkout) for server rendering, forwarding only the session and
 * guest-cart cookies. Never cached. Null when the API cannot be reached or refuses.
 */
export async function getForVisitor<T>(path: `/api/v1/${string}`, fetchImpl: typeof fetch = fetch): Promise<T | null> {
  const jar = await cookies();
  const cookie = cartCookieHeader({ session: jar.get(sessionCookieName)?.value, cart: jar.get(cartCookieName)?.value });
  try {
    const response = await fetchImpl(`${apiBaseUrl()}${path}`, {
      headers: cookie ? { Accept: "application/json", Cookie: cookie } : { Accept: "application/json" },
      cache: "no-store",
      signal: AbortSignal.timeout(5000),
    });
    return response.ok ? ((await response.json()) as T) : null;
  } catch {
    return null;
  }
}

/** The visitor's cart with current prices and stock (null: the API cannot be reached, so the page says so). */
export function getCart(locale: string, fetchImpl: typeof fetch = fetch): Promise<CartView | null> {
  return getForVisitor<CartView>(`/api/v1/cart?locale=${encodeURIComponent(locale)}`, fetchImpl);
}
