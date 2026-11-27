import Image from "next/image";
import Link from "next/link";
import type { OrderView } from "@/lib/account/session";
import { productPath } from "@/lib/catalog/product-path";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { regionName } from "@/lib/i18n/display-names";
import { formatDate } from "@/lib/i18n/format-date";
import { formatList } from "@/lib/i18n/format-list";
import { formatMoney, formatNumber } from "@/lib/i18n/format-money";
import { imageVariant } from "@/lib/media/image";
import { historyText, statusTint } from "@/lib/orders/format";

type Texts = Dictionary["orders"];

type Props = {
  order: OrderView;
  locale: string;
  texts: Texts;
  /** Staff see internal notes and who acted. */
  staff?: boolean;
};

/** A status as a small coloured label. */
export function OrderStatusBadge({ status, texts }: { status: OrderView["status"]; texts: Texts }) {
  return <span className={`${statusTint(status)} rounded-full px-2.5 py-0.5 text-xs font-bold`}>{texts.status[status]}</span>;
}

/**
 * One order: status, lines, amounts, delivery and the history. Server-rendered and presentation only; the pages load
 * the order (the customer's own, a guest's signed link, or any for staff).
 */
export function OrderDetails({ order, locale, texts, staff = false }: Props) {
  const money = (amount: number) => formatMoney(amount, order.currency, locale);
  const section = "flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4";
  const address = order.address;
  return (
    <div className="grid gap-4 lg:grid-cols-3">
      <div className="flex flex-col gap-4 lg:col-span-2">
        <section className={section} aria-labelledby="order-lines">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 id="order-lines" className="font-bold">
              {texts.linesTitle}
            </h2>
            <span className="text-xs text-muted">{texts.placedOn.replace("{date}", formatDate(order.orderedUtc, locale, true))}</span>
          </div>
          <ul className="flex flex-col divide-y divide-border">
            {order.lines.map((line) => (
              <li key={`${line.sku}-${line.number}`} className="flex items-center gap-3 py-3">
                <span className="relative block h-14 w-14 shrink-0 overflow-hidden rounded-xl bg-surface-muted">
                  {line.imageUrl && <Image src={imageVariant(line.imageUrl, 300)} alt="" width={56} height={56} unoptimized className="h-full w-full object-cover" />}
                </span>
                <span className="flex min-w-0 flex-1 flex-col gap-0.5 text-sm">
                  <Link href={productPath(locale, line.number, "")} className="line-clamp-2 font-medium hover:text-primary">
                    {line.name}
                  </Link>
                  <span className="text-xs text-muted">
                    {[line.variantLabel, `${texts.sku}: ${line.sku}`, `× ${formatNumber(line.quantity, locale)}`].filter(Boolean).join(" · ")}
                  </span>
                </span>
                <span className="shrink-0 text-sm font-bold">{money(line.lineTotal)}</span>
              </li>
            ))}
          </ul>
        </section>

        <section className={section} aria-labelledby="order-history">
          <h2 id="order-history" className="font-bold">
            {texts.historyTitle}
          </h2>
          <ol className="flex flex-col gap-3 border-s-2 border-border ps-4">
            {order.history.map((entry, index) => (
              <li key={index} className="relative flex flex-col gap-0.5 text-sm">
                <span aria-hidden className="absolute -start-[1.4rem] top-1.5 h-2.5 w-2.5 rounded-full bg-primary" />
                <span className={entry.isInternal ? "text-muted italic" : "font-medium"}>
                  {historyText(entry, texts)}
                  {staff && entry.isInternal && <span className="ms-2 rounded bg-surface-muted px-1.5 text-xs not-italic">{texts.internal}</span>}
                </span>
                <span className="text-xs text-muted">
                  {formatDate(entry.atUtc, locale, true)}
                  {staff && entry.byStaff && ` · ${texts.byStaff}`}
                </span>
              </li>
            ))}
          </ol>
        </section>
      </div>

      <div className="flex flex-col gap-4">
        <section className={section} aria-labelledby="order-summary">
          <h2 id="order-summary" className="font-bold">
            {texts.summaryTitle}
          </h2>
          <dl className="flex flex-col gap-1.5 text-sm">
            <div className="flex justify-between">
              <dt className="text-muted">{texts.subtotal}</dt>
              <dd>{money(order.subtotal)}</dd>
            </div>
            {order.discount > 0 && (
              <div className="flex justify-between text-success">
                <dt>{order.discountCode ? texts.discountCode.replace("{code}", order.discountCode) : texts.discount}</dt>
                <dd>−{money(order.discount)}</dd>
              </div>
            )}
            <div className="flex justify-between">
              <dt className="text-muted">{texts.shipping}</dt>
              <dd>{order.shipping === 0 ? texts.free : money(order.shipping)}</dd>
            </div>
            {order.taxRate > 0 && !order.pricesIncludeTax && (
              <div className="flex justify-between">
                <dt className="text-muted">{texts.taxAdded.replace("{rate}", formatNumber(order.taxRate, locale))}</dt>
                <dd>{money(order.tax)}</dd>
              </div>
            )}
            <div className="flex justify-between border-t border-border pt-2 text-base font-bold">
              <dt>{texts.total}</dt>
              <dd>{money(order.total)}</dd>
            </div>
            {order.taxRate > 0 && order.pricesIncludeTax && (
              <p className="text-xs text-muted">{texts.taxIncluded.replace("{amount}", money(order.tax)).replace("{rate}", formatNumber(order.taxRate, locale))}</p>
            )}
          </dl>
          {order.paymentDueUtc && <p className="rounded-xl bg-surface-muted p-3 text-xs">{texts.awaitingPayment.replace("{time}", formatDate(order.paymentDueUtc, locale, true))}</p>}
        </section>

        <section className={section} aria-labelledby="order-delivery">
          <h2 id="order-delivery" className="font-bold">
            {texts.deliveryTitle}
          </h2>
          <p className="text-sm font-medium">{order.isPickup ? texts.pickup : order.shippingMethodName}</p>
          <p className="text-sm">
            {order.recipientName}
            <span className="block text-muted" dir="ltr">
              {order.phoneNumber}
            </span>
            {staff && order.email && (
              <span className="block text-muted" dir="ltr">
                {order.email}
              </span>
            )}
          </p>
          {address && (
            <p className="text-sm text-muted">
              {formatList(
                [address.line1, address.line2, address.city, address.region, address.postalCode, address.countryCode ? regionName(address.countryCode, locale) : null].filter(
                  (part): part is string => Boolean(part),
                ),
                locale,
              )}
            </p>
          )}
          {order.trackingCode && (
            <p className="rounded-xl bg-surface-muted p-3 text-sm">
              {texts.tracking}: <span className="font-bold">{order.trackingCarrier}</span>{" "}
              <span dir="ltr" className="font-mono">
                {order.trackingCode}
              </span>
            </p>
          )}
          {order.note && (
            <p className="text-sm">
              <span className="text-muted">{texts.customerNote}: </span>
              {order.note}
            </p>
          )}
        </section>
      </div>
    </div>
  );
}
