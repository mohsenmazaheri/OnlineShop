import Link from "next/link";
import { Badge, type BadgeTone } from "@/components/ui/Surface";
import { purchaseLabel } from "@/lib/admin/inventory";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";
import { formatMoney, formatNumber } from "@/lib/i18n/format-money";

type ReceiptSummary = components["schemas"]["ReceiptSummary"];
type ReceiptView = components["schemas"]["ReceiptView"];
type Texts = Dictionary["admin"];

export const receiptTones: Record<ReceiptSummary["status"], BadgeTone> = { Draft: "warning", Posted: "success", Cancelled: "neutral" };

/** "3 items · 40 units · total €120.00" for a receipt. */
export function receiptTotals(summary: ReceiptSummary, texts: Texts, locale: string): string {
  return texts.purchaseSummary
    .replace("{lines}", formatNumber(summary.lines, locale))
    .replace("{units}", formatNumber(summary.units, locale))
    .replace("{total}", formatMoney(summary.total, summary.currency, locale));
}

/** Purchase receipts, newest first: number, supplier, warehouse, status, totals and dates. */
export function PurchaseList({ locale, texts, receipts }: { locale: string; texts: Texts; receipts: ReceiptSummary[] }) {
  if (receipts.length === 0) return <p className="text-sm text-muted">{texts.purchasesNone}</p>;

  return (
    <ul className="flex flex-col divide-y divide-border rounded-2xl border border-border bg-surface">
      {receipts.map((r) => (
        <li key={r.number}>
          <Link href={`/${locale}/admin/purchases/${r.number}`} className="flex flex-wrap items-center gap-x-3 gap-y-1 p-3 hover:bg-surface-muted">
            <span dir="ltr" className="font-bold">
              {purchaseLabel(r.number)}
            </span>
            <span className="font-medium">{r.supplier}</span>
            <Badge tone={receiptTones[r.status]}>{texts.purchaseStatuses[r.status]}</Badge>
            <span className="text-xs text-muted">{r.warehouse}</span>
            {r.supplierReference && (
              <span dir="ltr" className="text-xs text-muted">
                {r.supplierReference}
              </span>
            )}
            <span className="ms-auto text-xs text-muted">{formatDate(r.createdUtc, locale)}</span>
            <span className="w-full text-xs">{receiptTotals(r, texts, locale)}</span>
          </Link>
        </li>
      ))}
    </ul>
  );
}

/** A posted or cancelled receipt (or a draft for someone without inventory.manage): its lines, read-only. */
export function ReceiptLines({ locale, texts, receipt }: { locale: string; texts: Texts; receipt: ReceiptView }) {
  const money = (amount: number) => formatMoney(amount, receipt.summary.currency, locale);
  return (
    <div className="overflow-x-auto rounded-2xl border border-border bg-surface">
      <table className="w-full min-w-[32rem] text-sm">
        <thead className="bg-surface-muted text-xs text-muted">
          <tr>
            <th scope="col" className="p-3 text-start font-medium">{texts.stockItem}</th>
            <th scope="col" className="p-3 text-end font-medium">{texts.movementQuantity}</th>
            <th scope="col" className="p-3 text-end font-medium">{texts.purchaseUnitCost.replace("{currency}", receipt.summary.currency)}</th>
            <th scope="col" className="p-3 text-end font-medium">{texts.purchaseLineTotal}</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {receipt.lines.map((l) => (
            <tr key={l.itemId}>
              <th scope="row" className="p-3 text-start font-normal">
                <span className="block">{l.name}</span>
                {l.variantLabel && <span className="block text-xs">{l.variantLabel}</span>}
                <span dir="ltr" className="block text-xs text-muted">
                  {l.sku}
                </span>
              </th>
              <td className="p-3 text-end tabular-nums">{formatNumber(l.quantity, locale)}</td>
              <td className="p-3 text-end tabular-nums">{money(l.unitCost)}</td>
              <td className="p-3 text-end font-bold tabular-nums">{money(l.total)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
