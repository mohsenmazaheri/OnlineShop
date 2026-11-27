import "server-only";
import { apiBaseUrl } from "@/lib/api/base-url";
import type { OrderView } from "@/lib/account/session";

/** A guest's order through the signed link of its emails (never cached); null for a wrong number or key. */
export async function getOrderByLink(number: number, key: string, fetchImpl: typeof fetch = fetch): Promise<OrderView | null> {
  if (!key) return null;
  try {
    const response = await fetchImpl(`${apiBaseUrl()}/api/v1/orders/${number}?key=${encodeURIComponent(key)}`, {
      headers: { Accept: "application/json" },
      cache: "no-store",
      signal: AbortSignal.timeout(5000),
    });
    return response.ok ? ((await response.json()) as OrderView) : null;
  } catch {
    return null;
  }
}
