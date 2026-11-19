import { NextResponse, type NextFetchEvent, type NextRequest } from "next/server";
import { apiBaseUrl } from "@/lib/api/base-url";
import { sessionCookieName } from "@/lib/account/cookie";
import { fetchProductSlug, parseProductPath, productRedirectTarget } from "@/lib/catalog/product-path";
import { accountRedirectTarget, isApiPath, localeRedirectTarget } from "@/lib/routing";
import { securityHeaders } from "@/lib/security/headers";
import { indexingAllowed, siteUrl } from "@/lib/seo/config";
import { createRedirectCache, matchRedirect, redirectAnswer } from "@/lib/seo/redirects";
import { createStoreSettingsCache } from "@/lib/store/proxy-cache";

const storeSettings = createStoreSettingsCache(() => apiBaseUrl());
const redirects = createRedirectCache(() => apiBaseUrl());

/** Counts a used redirect in the API after the answer is sent; a failure only loses the count. */
function recordHit(event: NextFetchEvent, fromPath: string, origin: string) {
  event.waitUntil(
    fetch(`${apiBaseUrl()}/api/v1/redirects/hits`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Origin: origin },
      body: JSON.stringify({ fromPath }),
      signal: AbortSignal.timeout(2_000),
    }).catch(() => undefined),
  );
}

/**
 * Runs on every request at runtime (not at build), so one Docker image works for every shop:
 * API forwarding, locale redirects, security headers and the test-instance noindex all read live settings.
 */
export async function proxy(request: NextRequest, event: NextFetchEvent) {
  const { pathname, search } = request.nextUrl;

  // The API sets its own (stricter) headers.
  if (isApiPath(pathname)) return NextResponse.rewrite(new URL(`${pathname}${search}`, apiBaseUrl()));

  // An old address the admin redirected (for example a page of the shop's previous website).
  const rule = matchRedirect(await redirects(), pathname, search);
  if (rule) {
    recordHit(event, rule.fromPath, new URL(siteUrl()).origin);
    const { status, location } = redirectAnswer(rule, request.url);
    return location ? NextResponse.redirect(location, status) : new NextResponse(null, { status });
  }

  const target =
    localeRedirectTarget(pathname, search, await storeSettings()) ??
    accountRedirectTarget(pathname, search, request.cookies.has(sessionCookieName));
  // A product address with an old, wrong or missing slug: 301 to the current one. Here and not in the page, because
  // the page streams (its status is 200 before the product is loaded).
  const product = target ? null : parseProductPath(pathname);
  const productTarget = product ? productRedirectTarget(product, (await fetchProductSlug(apiBaseUrl(), product)) ?? "", search) : null;
  const response = target
    ? NextResponse.redirect(new URL(target, request.url), 307)
    : productTarget
      ? NextResponse.redirect(new URL(productTarget, request.url), 301)
      : NextResponse.next();

  const isDev = process.env.NODE_ENV !== "production";
  for (const { key, value } of securityHeaders({ isDev, https: siteUrl().startsWith("https://") })) {
    response.headers.set(key, value);
  }
  // Test/staging instances: keep every response out of search engines, whatever the page says.
  if (!indexingAllowed()) response.headers.set("X-Robots-Tag", "noindex, nofollow");

  return response;
}

export const config = {
  // Skip build assets and image optimization; everything else (pages, metadata routes, API) passes through.
  matcher: ["/((?!_next/static|_next/image).*)"],
};
