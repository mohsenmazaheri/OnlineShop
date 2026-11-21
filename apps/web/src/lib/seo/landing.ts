import type { BrowseResult, LandingPageContent } from "@/lib/api/storefront";
import { productPath } from "@/lib/catalog/product-path";

/** A landing page's address in a language ("/fa/l/گوشی-اورویا", encoded). */
export function landingPath(locale: string, slug: string): string {
  return `/${locale}/l/${encodeURIComponent(slug)}`;
}

/** The visitor's own choices on a landing page: only the sort and the page (its filters are fixed). */
export function landingChoices(searchParams: Record<string, string | string[] | undefined>): { sort: string | null; page: number } {
  const first = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v) ?? "";
  const sort = /^[a-z_]{1,20}$/.test(first(searchParams.sort)) ? first(searchParams.sort) : null;
  const page = Math.min(10_000, Math.max(1, Number.parseInt(first(searchParams.page), 10) || 1));
  return { sort, page };
}

/** The browse query: the page's fixed query, the language, and the visitor's sort and page. */
export function landingApiQuery(page: Pick<LandingPageContent, "query">, locale: string, choices: { sort: string | null; page: number }): string {
  const query = new URLSearchParams(page.query);
  query.set("locale", locale);
  if (choices.sort) query.set("sort", choices.sort);
  if (choices.page > 1) query.set("page", String(choices.page));
  return query.toString();
}

/**
 * canonical, hreflang and robots: indexed only in a translated language, unsorted; page 2 and on keep their own
 * canonical (?page=N). An untranslated language points at the default language's page.
 */
export function landingSeo(page: LandingPageContent, locale: string, defaultLocale: string, site: string, choices: { sort: string | null; page: number }) {
  const translated = page.alternates.some((a) => a.locale === locale);
  const languages: Record<string, string> = Object.fromEntries(page.alternates.map((a) => [a.locale, `${site}${landingPath(a.locale, a.slug)}`]));
  const fallback = page.alternates.find((a) => a.locale === defaultLocale) ?? page.alternates[0];
  if (fallback) languages["x-default"] = `${site}${landingPath(fallback.locale, fallback.slug)}`;
  const own = translated ? `${site}${landingPath(locale, page.slug)}` : languages["x-default"];
  const canonical = translated && choices.page > 1 && !choices.sort ? `${own}?page=${choices.page}` : own;
  return { canonical, languages, indexable: translated && !choices.sort };
}

/** BreadcrumbList and ItemList of the products shown (the same as on the screen). */
export function landingJsonLd(page: LandingPageContent, result: BrowseResult, locale: string, site: string, homeName: string, pageUrl: string) {
  const offset = (result.page - 1) * result.pageSize;
  return [
    {
      "@context": "https://schema.org",
      "@type": "BreadcrumbList",
      itemListElement: [
        { "@type": "ListItem", position: 1, name: homeName, item: `${site}/${locale}` },
        { "@type": "ListItem", position: 2, name: page.title, item: pageUrl },
      ],
    },
    {
      "@context": "https://schema.org",
      "@type": "ItemList",
      url: pageUrl,
      numberOfItems: result.total,
      itemListElement: result.items.map((p, i) => ({ "@type": "ListItem", position: offset + i + 1, url: `${site}${productPath(locale, p.number, p.slug)}`, name: p.name })),
    },
  ];
}
