import Link from "next/link";
import { Badge, type BadgeTone } from "@/components/ui/Surface";
import { stocktakeHref, stocktakeLabel } from "@/lib/admin/inventory";
import type { components } from "@/lib/api/schema";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";
import { formatNumber } from "@/lib/i18n/format-money";

type StocktakeSummary = components["schemas"]["StocktakeSummary"];
type Status = StocktakeSummary["status"];
type Texts = Dictionary["admin"];

export const statusTones: Record<Status, BadgeTone> = { Open: "warning", Posted: "success", Cancelled: "neutral" };

/** Every stocktake, newest first: number, warehouse, status, progress and dates; each opens its page. */
export function StocktakeList({ locale, texts, stocktakes }: { locale: string; texts: Texts; stocktakes: StocktakeSummary[] }) {
  if (stocktakes.length === 0) return <p className="text-sm text-muted">{texts.stocktakesNone}</p>;

  return (
    <ul className="flex flex-col divide-y divide-border rounded-2xl border border-border bg-surface">
      {stocktakes.map((s) => (
        <li key={s.number}>
          <Link href={stocktakeHref(locale, s.number)} className="flex flex-wrap items-center gap-x-3 gap-y-1 p-3 hover:bg-surface-muted">
            <span dir="ltr" className="font-bold">
              {stocktakeLabel(s.number)}
            </span>
            <span className="font-medium">{s.warehouse}</span>
            <Badge tone={statusTones[s.status]}>{texts.stocktakeStatuses[s.status]}</Badge>
            <span className="text-xs text-muted">
              {texts.stocktakeProgress.replace("{counted}", formatNumber(s.counted, locale)).replace("{lines}", formatNumber(s.lines, locale))}
            </span>
            <span className="ms-auto text-xs text-muted">
              {texts.stocktakeOpened.replace("{date}", formatDate(s.createdUtc, locale))}
              {s.closedUtc && ` · ${texts.stocktakeClosedAt.replace("{date}", formatDate(s.closedUtc, locale))}`}
            </span>
            {s.note && <span className="w-full text-xs text-muted">{s.note}</span>}
          </Link>
        </li>
      ))}
    </ul>
  );
}
