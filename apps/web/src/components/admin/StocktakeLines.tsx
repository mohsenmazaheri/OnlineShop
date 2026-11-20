import Link from "next/link";
import { pageWindow, signedQuantity, stocktakeFilters, stocktakeHref, type StocktakeFilter } from "@/lib/admin/inventory";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatNumber } from "@/lib/i18n/format-money";

type StocktakeView = components["schemas"]["StocktakeView"];
type Texts = Dictionary["admin"];

type Props = { locale: string; texts: Texts; stocktake: StocktakeView; filter: StocktakeFilter; search: string };

/** A stocktake's lines: filter tabs, a GET search form, one row per item with its difference, and page links. */
export function StocktakeLines({ locale, texts, stocktake, filter, search }: Props) {
  const pages = Math.max(1, Math.ceil(stocktake.total / stocktake.pageSize));
  const base = stocktakeHref(locale, stocktake.number);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-1">
        {stocktakeFilters.map((f) => (
          <Link
            key={f}
            href={stocktakeHref(locale, stocktake.number, { filter: f, search })}
            aria-current={f === filter ? "page" : undefined}
            className="rounded-lg border border-border px-3 py-1.5 text-sm aria-[current=page]:bg-primary aria-[current=page]:text-primary-foreground"
          >
            {texts.stocktakeFilters[f]}
          </Link>
        ))}
      </div>
      <form method="get" action={base} role="search" className="flex flex-wrap gap-2">
        {filter !== "All" && <input type="hidden" name="filter" value={filter} />}
        <input
          type="search"
          name="search"
          defaultValue={search}
          aria-label={texts.stockSearch}
          placeholder={texts.stockSearch}
          className="h-11 w-64 max-w-full rounded-xl border border-border bg-surface px-3"
        />
        <button type="submit" className="h-11 rounded-xl border border-border px-4 text-sm">
          {texts.stockShow}
        </button>
      </form>

      <div className="overflow-x-auto rounded-2xl border border-border bg-surface">
        <table className="w-full min-w-[36rem] text-sm">
          <thead className="bg-surface-muted text-xs text-muted">
            <tr>
              <th scope="col" className="p-3 text-start font-medium">{texts.stockItem}</th>
              <th scope="col" className="p-3 text-end font-medium">{texts.stocktakeSnapshot}</th>
              <th scope="col" className="p-3 text-end font-medium">{texts.stocktakeExpected}</th>
              <th scope="col" className="p-3 text-end font-medium">{texts.stocktakeCounted}</th>
              <th scope="col" className="p-3 text-end font-medium">{texts.stocktakeDifference}</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {stocktake.lines.map((l) => (
              <tr key={l.itemId}>
                <th scope="row" className="p-3 text-start font-normal">
                  <span className="block">{l.name}</span>
                  {l.variantLabel && <span className="block text-xs">{l.variantLabel}</span>}
                  <span dir="ltr" className="block text-xs text-muted">
                    {l.sku}
                  </span>
                </th>
                <td className="p-3 text-end tabular-nums text-muted">{formatNumber(l.snapshot, locale)}</td>
                <td className="p-3 text-end tabular-nums">{formatNumber(l.expected, locale)}</td>
                <td className="p-3 text-end font-bold tabular-nums">{l.counted === null ? "—" : formatNumber(l.counted, locale)}</td>
                <td
                  dir="ltr"
                  className={`p-3 text-end font-bold tabular-nums ${!l.difference ? "text-muted" : l.difference < 0 ? "text-danger" : "text-success"}`}
                >
                  {l.difference === null ? "—" : signedQuantity(l.difference, locale)}
                </td>
              </tr>
            ))}
            {stocktake.lines.length === 0 && (
              <tr>
                <td colSpan={5} className="p-4 text-muted">
                  {texts.stocktakeNoLines}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {pages > 1 && (
        <nav aria-label={texts.pages} className="flex flex-wrap gap-1">
          {pageWindow(stocktake.page, pages).map((p) => (
            <Link
              key={p}
              href={stocktakeHref(locale, stocktake.number, { filter, search, page: p })}
              aria-current={p === stocktake.page ? "page" : undefined}
              className="flex h-10 min-w-10 items-center justify-center rounded-lg border border-border px-2 text-sm aria-[current=page]:bg-primary aria-[current=page]:text-primary-foreground"
            >
              {formatNumber(p, locale)}
            </Link>
          ))}
        </nav>
      )}
    </div>
  );
}
