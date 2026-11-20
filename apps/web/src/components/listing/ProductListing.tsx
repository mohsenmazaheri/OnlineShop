import Link from "next/link";
import { ProductCard } from "@/components/product/ProductCard";
import { appliedFilters, clearFiltersHref, listingHref, relaxationLinks, sortName, sortsFor, type BrowseResult, type ListingParams } from "@/lib/catalog/browse";
import { convertAmount, type DisplayCurrency } from "@/lib/currency/display";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatMoney, formatNumber } from "@/lib/i18n/format-money";
import { pageWindow } from "@/lib/pagination";
import { ListingFacets } from "./ListingFacets";

type Props = {
  locale: string;
  dict: Pick<Dictionary, "listing" | "product">;
  result: BrowseResult;
  basePath: string;
  params: ListingParams;
  money: DisplayCurrency;
};

/**
 * A product listing: the filters (a side column from lg; a folded panel above the list on phones, which opens without
 * JavaScript), the count and sort links, the filters in effect as removable chips, the product grid and real page
 * links. Everything is server-rendered and works without JavaScript.
 */
export function ProductListing({ locale, dict, result, basePath, params, money }: Props) {
  const texts = dict.listing;
  const isSearch = result.scope.kind === "search";
  const current = sortName(result.sort);
  // Typed prices are already in the display currency: only formatted.
  const chips = appliedFilters(basePath, params, result.facets, texts, (amount) =>
    formatMoney(amount, money.code, locale, { symbol: money.symbol, decimals: money.decimals }),
  );
  const pages = Math.max(1, Math.ceil(result.total / result.pageSize));
  const facets = (
    <ListingFacets locale={locale} texts={texts} facets={result.facets} basePath={basePath} params={params} displayPrice={(amount) => convertAmount(amount, money)} />
  );

  return (
    <div className="grid gap-6 lg:grid-cols-[16rem_1fr]">
      <aside className="hidden lg:block" aria-label={texts.filters}>
        {facets}
      </aside>
      <details className="rounded-2xl border border-border bg-surface lg:hidden">
        <summary className="cursor-pointer p-3 font-medium">{texts.filters}</summary>
        <div className="border-t border-border p-3">{facets}</div>
      </details>

      <div className="flex min-w-0 flex-col gap-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="text-sm text-muted">{texts.productCount.replace("{count}", formatNumber(result.total, locale))}</p>
          <nav aria-label={texts.sortLabel} className="flex flex-wrap items-center gap-1 text-sm">
            <span className="text-muted">{texts.sortLabel}:</span>
            {sortsFor(isSearch).map((s) => (
              <Link
                key={s}
                href={listingHref(basePath, params, { sort: s === (isSearch ? "relevance" : "newest") ? null : s })}
                rel="nofollow"
                aria-current={s === current ? "true" : undefined}
                className="rounded-lg px-2 py-1 hover:bg-surface-muted aria-[current=true]:bg-primary/10 aria-[current=true]:font-bold aria-[current=true]:text-primary"
              >
                {texts.sorts[s]}
              </Link>
            ))}
          </nav>
        </div>

        {chips.length > 0 && (
          <ul className="flex flex-wrap items-center gap-2">
            {chips.map((chip) => (
              <li key={chip.href}>
                <Link
                  href={chip.href}
                  rel="nofollow"
                  aria-label={texts.removeFilter.replace("{filter}", chip.label)}
                  className="flex items-center gap-1 rounded-full border border-primary bg-primary/5 px-3 py-1 text-sm text-primary"
                >
                  {chip.label} <span aria-hidden>×</span>
                </Link>
              </li>
            ))}
            <li>
              <Link href={clearFiltersHref(basePath, params)} rel="nofollow" className="text-sm text-muted underline-offset-2 hover:underline">
                {texts.clearAll}
              </Link>
            </li>
          </ul>
        )}

        {result.items.length === 0 ? (
          <div className="flex flex-col items-center gap-3 rounded-2xl border border-dashed border-border p-6 text-center">
            <p className="text-muted">{result.filtered ? texts.empty : texts.emptyScope}</p>
            {result.relaxations.length > 0 && (
              <>
                <p className="text-sm">{texts.relaxTitle}</p>
                <ul className="flex flex-wrap justify-center gap-2">
                  {relaxationLinks(basePath, params, result, texts, (n) => formatNumber(n, locale)).map((link) => (
                    <li key={link.href}>
                      <Link href={link.href} rel="nofollow" className="rounded-full border border-border px-3 py-1 text-sm hover:border-primary">
                        {link.label}
                      </Link>
                    </li>
                  ))}
                </ul>
              </>
            )}
          </div>
        ) : (
          <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4">
            {result.items.map((product, i) => (
              <li key={product.number}>
                <ProductCard product={product} locale={locale} money={money} texts={dict.product} priority={i < 4} />
              </li>
            ))}
          </ul>
        )}

        {pages > 1 && (
          <nav aria-label={texts.pages} className="flex flex-wrap justify-center gap-1">
            {pageWindow(result.page, pages).map((p) => (
              <Link
                key={p}
                href={listingHref(basePath, params, { page: String(p) })}
                aria-current={p === result.page ? "page" : undefined}
                className="flex h-10 min-w-10 items-center justify-center rounded-lg border border-border px-2 text-sm aria-[current=page]:bg-primary aria-[current=page]:text-primary-foreground"
              >
                {formatNumber(p, locale)}
              </Link>
            ))}
          </nav>
        )}
      </div>
    </div>
  );
}
