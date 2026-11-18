/**
 * The same page in another language: /fa/account/addresses → /en/account/addresses. Paths without a locale (or
 * outside the site) go to that language's home page.
 */
export function switchLocalePath(pathname: string | null | undefined, target: string, locales: readonly string[]): string {
  const path = pathname ?? "";
  const [, first = "", ...rest] = path.split("/");
  if (!locales.includes(first)) return `/${target}`;
  const tail = rest.join("/");
  return tail ? `/${target}/${tail}` : `/${target}`;
}

export type NavTab = "home" | "categories" | "cart" | "account" | null;

/** Which bottom-bar tab a path belongs to (for aria-current). */
export function activeTab(pathname: string | null | undefined, locale: string): NavTab {
  const path = (pathname ?? "").replace(/\/$/, "");
  if (path === `/${locale}`) return "home";
  if (path === `/${locale}/cart` || path.startsWith(`/${locale}/cart/`)) return "cart";
  if (path === `/${locale}/account` || path.startsWith(`/${locale}/account/`)) return "account";
  return null;
}
