import Image from "next/image";
import Link from "next/link";
import type { OrderPage } from "@/lib/account/session";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";
import { formatMoney, formatNumber } from "@/lib/i18n/format-money";
import { imageVariant } from "@/lib/media/image";
import { formatOrderNumber } from "@/lib/orders/format";
import { OrderStatusBadge } from "./OrderDetails";

type Props = {
  page: OrderPage;
  locale: string;
  texts: Dictionary["orders"];
  /** The page of one order, and of another list page (keeps the filters). */
  hrefFor: (number: number) => string;
  pageHref: (page: number) => string;
  /** Staff lists show who ordered. */
  staff?: boolean;
};

/** Orders as rows (newest first) with paging links; plain links, works without JavaScript. */
export function OrderList({ page, locale, texts, hrefFor, pageHref, staff = false }: Props) {
  if (page.items.length === 0) return <p className="text-sm text-muted">{texts.empty}</p>;
  const pages = Math.ceil(page.total / page.pageSize);
  return (
    <div className="flex flex-col gap-3">
      <ul className="flex flex-col divide-y divide-border rounded-2xl border border-border bg-surface">
        {page.items.map((order) => (
          <li key={order.number}>
            <Link href={hrefFor(order.number)} className="flex items-center gap-3 p-3 hover:bg-surface-muted sm:p-4">
              <span className="relative block h-12 w-12 shrink-0 overflow-hidden rounded-xl bg-surface-muted">
                {order.imageUrl && <Image src={imageVariant(order.imageUrl, 300)} alt="" width={48} height={48} unoptimized className="h-full w-full object-cover" />}
              </span>
              <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                <span className="flex flex-wrap items-center gap-2">
                  <span className="font-bold">{texts.orderNumber.replace("{number}", formatOrderNumber(order.number, locale))}</span>
                  <OrderStatusBadge status={order.status} texts={texts} />
                </span>
                <span className="truncate text-xs text-muted">
                  {[formatDate(order.orderedUtc, locale), texts.itemCount.replace("{count}", formatNumber(order.itemCount, locale)), staff ? order.recipientName : null]
                    .filter(Boolean)
                    .join(" · ")}
                </span>
              </span>
              <span className="shrink-0 text-sm font-bold">{formatMoney(order.total, order.currency, locale)}</span>
            </Link>
          </li>
        ))}
      </ul>
      {pages > 1 && (
        <nav aria-label={texts.pages} className="flex items-center justify-between text-sm">
          {page.page > 1 ? (
            <Link href={pageHref(page.page - 1)} className="text-primary hover:underline">
              {texts.newer}
            </Link>
          ) : (
            <span />
          )}
          <span className="text-muted">{texts.pageOf.replace("{page}", formatNumber(page.page, locale)).replace("{pages}", formatNumber(pages, locale))}</span>
          {page.page < pages ? (
            <Link href={pageHref(page.page + 1)} className="text-primary hover:underline">
              {texts.older}
            </Link>
          ) : (
            <span />
          )}
        </nav>
      )}
    </div>
  );
}
