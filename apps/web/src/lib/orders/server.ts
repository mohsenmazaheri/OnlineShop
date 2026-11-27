import "server-only";
import { apiBaseUrl } from "@/lib/api/base-url";
import type { InvoiceView, OrderView } from "@/lib/account/session";

/** A guest's order through the signed link of its emails (never cached); null for a wrong number or key. */
export function getOrderByLink(number: number, key: string, fetchImpl: typeof fetch = fetch): Promise<OrderView | null> {
  return getByLink<OrderView>(`/api/v1/orders/${number}`, key, fetchImpl);
}

/** The invoice of a guest's paid order through its signed link; null when unpaid or the key is wrong. */
export function getInvoiceByLink(number: number, key: string, fetchImpl: typeof fetch = fetch): Promise<InvoiceView | null> {
  return getByLink<InvoiceView>(`/api/v1/orders/${number}/invoice`, key, fetchImpl);
}

async function getByLink<T>(path: string, key: string, fetchImpl: typeof fetch): Promise<T | null> {
  if (!key) return null;
  try {
    const response = await fetchImpl(`${apiBaseUrl()}${path}?key=${encodeURIComponent(key)}`, {
      headers: { Accept: "application/json" },
      cache: "no-store",
      signal: AbortSignal.timeout(5000),
    });
    return response.ok ? ((await response.json()) as T) : null;
  } catch {
    return null;
  }
}
