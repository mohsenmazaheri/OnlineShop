import Link from "next/link";
import { allKinds, movementsHref, pageWindow, referenceLabel, signedQuantity } from "@/lib/admin/inventory";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";
import { formatNumber } from "@/lib/i18n/format-money";

type MovementPage = components["schemas"]["MovementPage"];
type WarehouseView = components["schemas"]["WarehouseView"];
type Texts = Dictionary["admin"];
type Filters = { sku: string; warehouseId: string; kind: string };

type Props = { locale: string; texts: Texts; page: MovementPage; warehouses: WarehouseView[]; filters: Filters };

/** The stock ledger: GET filters (SKU, warehouse, kind), one row per line, newest first, and page links. */
export function MovementList({ locale, texts, page, warehouses, filters }: Props) {
  const pages = Math.max(1, Math.ceil(page.total / page.pageSize));
  const control = "h-11 rounded-xl border border-border bg-surface px-3";

  return (
    <div className="flex flex-col gap-4">
      <form method="get" action={movementsHref(locale, {})} role="search" className="flex flex-wrap items-center gap-2">
        <input
          type="search"
          name="sku"
          defaultValue={filters.sku}
          aria-label={texts.movementSku}
          placeholder="SKU"
          dir="ltr"
          className={`${control} w-48`}
        />
        <select name="warehouseId" defaultValue={filters.warehouseId} aria-label={texts.movementWarehouse} className={control}>
          <option value="">{texts.movementsAllWarehouses}</option>
          {warehouses.map((w) => (
            <option key={w.id} value={w.id}>
              {w.name}
            </option>
          ))}
        </select>
        <select name="kind" defaultValue={filters.kind} aria-label={texts.movementType} className={control}>
          <option value="">{texts.movementsAllKinds}</option>
          {allKinds.map((k) => (
            <option key={k} value={k}>
              {texts.movementKinds[k]}
            </option>
          ))}
        </select>
        <button type="submit" className="h-11 rounded-xl border border-border px-4 text-sm">
          {texts.stockShow}
        </button>
      </form>

      <p className="text-sm text-muted">{texts.movementsCount.replace("{count}", formatNumber(page.total, locale))}</p>

      <div className="overflow-x-auto rounded-2xl border border-border bg-surface">
        <table className="w-full min-w-[48rem] text-sm">
          <thead className="bg-surface-muted text-xs text-muted">
            <tr>
              <th scope="col" className="p-3 text-start font-medium">{texts.movementsDate}</th>
              <th scope="col" className="p-3 text-start font-medium">{texts.stockItem}</th>
              <th scope="col" className="p-3 text-start font-medium">{texts.movementWarehouse}</th>
              <th scope="col" className="p-3 text-start font-medium">{texts.movementType}</th>
              <th scope="col" className="p-3 text-end font-medium">{texts.movementQuantity}</th>
              <th scope="col" className="p-3 text-end font-medium">{texts.movementsBalance}</th>
              <th scope="col" className="p-3 text-start font-medium">{texts.movementsReference}</th>
              <th scope="col" className="p-3 text-start font-medium">{texts.movementsUser}</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {page.items.map((m) => (
              <tr key={m.id}>
                <td className="whitespace-nowrap p-3 text-xs">{formatDate(m.createdUtc, locale, true)}</td>
                <td className="p-3">
                  <span className="block">{m.name}</span>
                  {m.variantLabel && <span className="block text-xs">{m.variantLabel}</span>}
                  <span dir="ltr" className="block text-xs text-muted">
                    {m.sku}
                  </span>
                </td>
                <td className="p-3">{m.warehouse}</td>
                <td className="p-3">{texts.movementKinds[m.kind]}</td>
                <td className={`p-3 text-end font-bold tabular-nums ${m.quantity < 0 ? "text-danger" : "text-success"}`} dir="ltr">
                  {signedQuantity(m.quantity, locale)}
                </td>
                <td className="p-3 text-end tabular-nums">{formatNumber(m.balanceAfter, locale)}</td>
                <td className="p-3 text-xs">
                  {referenceLabel(m.reference, texts.stockReferences) && <span className="block">{referenceLabel(m.reference, texts.stockReferences)}</span>}
                  {m.note && <span className="block text-muted">{m.note}</span>}
                </td>
                <td className="p-3 text-xs">{m.userName ?? texts.movementsSystem}</td>
              </tr>
            ))}
            {page.items.length === 0 && (
              <tr>
                <td colSpan={8} className="p-4 text-muted">
                  {texts.movementsNone}
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
              href={movementsHref(locale, { ...filters, page: p })}
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
