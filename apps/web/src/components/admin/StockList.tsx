import Link from "next/link";
import { movementsHref, pageWindow } from "@/lib/admin/inventory";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatNumber } from "@/lib/i18n/format-money";

type StockPage = components["schemas"]["StockPage"];
type Texts = Dictionary["admin"];

type Props = { locale: string; texts: Texts; page: StockPage; search: string; onlyOut: boolean; canManage: boolean };

/**
 * Stock per item and warehouse: a GET search form (works without JavaScript, bookmarkable), one row per product or
 * variant with a column per warehouse, and page links. Active warehouses get columns; an inactive one holds nothing.
 */
export function StockList({ locale, texts, page, search, onlyOut, canManage }: Props) {
  const warehouses = page.warehouses.filter((w) => w.isActive);
  const pages = Math.max(1, Math.ceil(page.total / page.pageSize));
  const base = `/${locale}/admin/inventory`;
  const href = (p: number) => `${base}?${new URLSearchParams({ ...(search ? { search } : {}), ...(onlyOut ? { onlyOut: "true" } : {}), page: String(p) }).toString()}`;

  return (
    <div className="flex flex-col gap-4">
      <form method="get" action={base} role="search" className="flex flex-wrap items-center gap-2">
        <input
          type="search"
          name="search"
          defaultValue={search}
          aria-label={texts.stockSearch}
          placeholder={texts.stockSearch}
          className="h-11 w-64 max-w-full rounded-xl border border-border bg-surface px-3"
        />
        <label className="flex h-11 items-center gap-2 text-sm">
          <input type="checkbox" name="onlyOut" value="true" defaultChecked={onlyOut} className="h-5 w-5" />
          {texts.stockOnlyOut}
        </label>
        <button type="submit" className="h-11 rounded-xl border border-border px-4 text-sm">
          {texts.stockShow}
        </button>
      </form>

      <p className="text-sm text-muted">{texts.stockCount.replace("{count}", formatNumber(page.total, locale))}</p>

      <div className="overflow-x-auto rounded-2xl border border-border bg-surface">
        <table className="w-full min-w-[36rem] text-sm">
          <thead className="bg-surface-muted text-start text-xs text-muted">
            <tr>
              <th scope="col" className="p-3 text-start font-medium">{texts.stockItem}</th>
              {warehouses.map((w) => (
                <th key={w.id} scope="col" className="p-3 text-end font-medium">
                  {w.name}
                </th>
              ))}
              <th scope="col" className="p-3 text-end font-medium">{texts.stockTotal}</th>
              <th scope="col" className="p-3">
                <span className="sr-only">{texts.stockHistory}</span>
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {page.items.map((row) => {
              const quantities = new Map(row.levels.map((l) => [l.warehouseId, l.quantity]));
              return (
                <tr key={row.itemId}>
                  <th scope="row" className="p-3 text-start font-normal">
                    <span className="block font-medium">{row.name}</span>
                    {row.variantLabel && <span className="block text-xs">{row.variantLabel}</span>}
                    <span dir="ltr" className="block text-xs text-muted">
                      #{row.productNumber} · {row.sku}
                    </span>
                    {!row.isPublished && <span className="text-xs text-muted">{texts.stockDraft}</span>}
                  </th>
                  {warehouses.map((w) => (
                    <td key={w.id} className="p-3 text-end tabular-nums">
                      {formatNumber(quantities.get(w.id) ?? 0, locale)}
                    </td>
                  ))}
                  <td className={`p-3 text-end font-bold tabular-nums ${row.total === 0 ? "text-danger" : ""}`}>{formatNumber(row.total, locale)}</td>
                  <td className="p-3 text-end">
                    <span className="flex flex-col items-end gap-1 text-xs">
                      <Link href={movementsHref(locale, { sku: row.sku })} className="text-primary underline-offset-2 hover:underline">
                        {texts.stockHistory}
                      </Link>
                      {canManage && (
                        <Link href={`${base}?${new URLSearchParams({ sku: row.sku }).toString()}#move`} className="text-primary underline-offset-2 hover:underline">
                          {texts.stockMove}
                        </Link>
                      )}
                    </span>
                  </td>
                </tr>
              );
            })}
            {page.items.length === 0 && (
              <tr>
                <td colSpan={warehouses.length + 3} className="p-4 text-muted">
                  {texts.stockNone}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {pages > 1 && (
        <nav aria-label={texts.pages} className="flex flex-wrap gap-1">
          {pageWindow(page.page, pages).map((p) => (
            <Link
              key={p}
              href={href(p)}
              aria-current={p === page.page ? "page" : undefined}
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
