import { isLocale, type Locale } from "@/lib/i18n/config";

/** A product page's address: /{locale}/p/{number}/{slug} (docs/SEO-MARKETING.md §2). */
export function productPath(locale: string, number: number, slug: string): string {
  return slug ? `/${locale}/p/${number}/${encodeURIComponent(slug)}` : `/${locale}/p/${number}`;
}

export type ProductPathParts = { locale: Locale; number: number; slug: string };

/** The parts of a product address, or null for any other path. The slug comes back decoded ("" when missing). */
export function parseProductPath(pathname: string): ProductPathParts | null {
  const match = /^\/([a-z]{2})\/p\/(\d{1,15})(?:\/([^/]+))?\/?$/.exec(pathname);
  if (!match || !isLocale(match[1])) return null;
  let slug = match[3] ?? "";
  try {
    slug = decodeURIComponent(slug);
  } catch {
    // A broken escape is just a wrong slug: the redirect fixes it.
  }
  return { locale: match[1], number: Number(match[2]), slug };
}

/**
 * Where a product address must permanently redirect to: the same product with its current slug in that language
 * (renamed product, old or mistyped slug, missing slug). Null when the address is already right.
 */
export function productRedirectTarget(parts: ProductPathParts, currentSlug: string, search: string): string | null {
  if (!currentSlug || parts.slug === currentSlug) return null;
  return `${productPath(parts.locale, parts.number, currentSlug)}${search}`;
}

/**
 * Asks the API for the product's slug in a language. Null when the product does not exist or the API cannot be
 * reached in time — the page itself then decides (404 or the content), so a slow API never blocks the shop.
 */
export async function fetchProductSlug(
  apiBase: string,
  parts: ProductPathParts,
  fetchImpl: typeof fetch = fetch,
): Promise<string | null> {
  try {
    const response = await fetchImpl(`${apiBase}/api/v1/catalog/products/${parts.number}/slug?locale=${parts.locale}`, {
      headers: { Accept: "application/json" },
      signal: AbortSignal.timeout(2000),
    });
    if (!response.ok) return null;
    const body = (await response.json()) as { slug?: unknown };
    return typeof body.slug === "string" ? body.slug : null;
  } catch {
    return null;
  }
}
