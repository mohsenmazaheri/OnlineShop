import "server-only";
import type { components } from "@/lib/api/schema";
import { getForVisitor } from "@/lib/cart/server";

export type CheckoutView = components["schemas"]["CheckoutView"];

/** What the checkout page starts with (per visitor, never cached); null when the API cannot be reached. */
export function getCheckout(locale: string, fetchImpl: typeof fetch = fetch): Promise<CheckoutView | null> {
  return getForVisitor<CheckoutView>(`/api/v1/checkout?locale=${encodeURIComponent(locale)}`, fetchImpl);
}
