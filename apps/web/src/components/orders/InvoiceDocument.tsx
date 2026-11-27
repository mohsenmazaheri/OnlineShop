import type { InvoiceView } from "@/lib/account/session";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { regionName } from "@/lib/i18n/display-names";
import { formatDate } from "@/lib/i18n/format-date";
import { joinParts } from "@/lib/i18n/format-list";
import { formatMoney, formatNumber } from "@/lib/i18n/format-money";
import { formatOrderNumber } from "@/lib/orders/format";

type Props = {
  invoice: InvoiceView;
  locale: string;
  /** The shop's name, for invoices issued before seller details existed (their seller name is empty). */
  shopName: string;
  texts: Dictionary["orders"];
};

/**
 * The invoice as a document: seller, buyer, lines and totals, laid out for printing (the browser's "Save as PDF").
 * Server-rendered, presentation only; plain black on white when printed.
 */
export function InvoiceDocument({ invoice, locale, shopName, texts }: Props) {
  const order = invoice.order;
  const money = (amount: number) => formatMoney(amount, order.currency, locale);
  const address = order.address;
  const buyerAddress = address
    ? joinParts([address.line1, address.line2, address.city, address.region, address.postalCode, address.countryCode ? regionName(address.countryCode, locale) : null], locale)
    : null;
  const cell = "border-b border-border px-2 py-2 text-start print:border-black/30";
  return (
    <article className="mx-auto flex w-full max-w-3xl flex-col gap-6 rounded-2xl border border-border bg-surface p-6 text-sm print:max-w-none print:rounded-none print:border-0 print:p-0 print:text-black">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-col gap-1">
          <p className="text-lg font-extrabold">{invoice.sellerName || shopName}</p>
          {invoice.sellerAddress && <p className="text-muted print:text-black">{invoice.sellerAddress}</p>}
          {invoice.sellerTaxNumber && (
            <p className="text-muted print:text-black">
              {texts.invoiceTaxNumber}: <span dir="ltr">{invoice.sellerTaxNumber}</span>
            </p>
          )}
        </div>
        <dl className="grid grid-cols-[auto_auto] gap-x-4 gap-y-1">
          <dt className="text-muted print:text-black">{texts.invoiceTitle}</dt>
          <dd className="font-bold">{formatOrderNumber(invoice.number, locale)}</dd>
          <dt className="text-muted print:text-black">{texts.invoiceDate}</dt>
          <dd>{formatDate(invoice.issuedUtc, locale)}</dd>
          <dt className="text-muted print:text-black">{texts.invoiceOrder}</dt>
          <dd>{formatOrderNumber(order.number, locale)}</dd>
        </dl>
      </header>

      <section aria-labelledby="invoice-buyer" className="flex flex-col gap-1">
        <h2 id="invoice-buyer" className="text-xs font-bold text-muted print:text-black">
          {texts.invoiceBuyer}
        </h2>
        <p className="font-medium">{order.recipientName}</p>
        {buyerAddress && <p>{buyerAddress}</p>}
        <p dir="ltr" className="text-start">
          {[order.phoneNumber, order.email].filter(Boolean).join(" · ")}
        </p>
      </section>

      <table className="w-full border-collapse">
        <thead>
          <tr className="text-xs text-muted print:text-black">
            <th className={cell}>{texts.invoiceItem}</th>
            <th className={cell}>{texts.sku}</th>
            <th className={`${cell} text-end`}>{texts.invoiceQuantity}</th>
            <th className={`${cell} text-end`}>{texts.invoiceUnitPrice}</th>
            <th className={`${cell} text-end`}>{texts.invoiceLineTotal}</th>
          </tr>
        </thead>
        <tbody>
          {order.lines.map((line) => (
            <tr key={line.id}>
              <td className={cell}>
                {line.name}
                {line.variantLabel && <span className="block text-xs text-muted print:text-black">{line.variantLabel}</span>}
              </td>
              <td className={cell} dir="ltr">
                {line.sku}
              </td>
              <td className={`${cell} text-end`}>{formatNumber(line.quantity, locale)}</td>
              <td className={`${cell} text-end`}>{money(line.unitPrice)}</td>
              <td className={`${cell} text-end`}>{money(line.lineTotal)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <dl className="ms-auto flex w-full max-w-xs flex-col gap-1.5">
        <div className="flex justify-between">
          <dt>{texts.subtotal}</dt>
          <dd>{money(order.subtotal)}</dd>
        </div>
        {order.discount > 0 && (
          <div className="flex justify-between">
            <dt>{order.discountCode ? texts.discountCode.replace("{code}", order.discountCode) : texts.discount}</dt>
            <dd>−{money(order.discount)}</dd>
          </div>
        )}
        <div className="flex justify-between">
          <dt>
            {texts.shipping} ({order.isPickup ? texts.pickup : order.shippingMethodName})
          </dt>
          <dd>{order.shipping === 0 ? texts.free : money(order.shipping)}</dd>
        </div>
        {order.taxRate > 0 && (
          <div className="flex justify-between">
            <dt>{(order.pricesIncludeTax ? texts.invoiceTaxIncluded : texts.taxAdded).replace("{rate}", formatNumber(order.taxRate, locale))}</dt>
            <dd>{money(order.tax)}</dd>
          </div>
        )}
        <div className="flex justify-between border-t border-border pt-2 text-base font-bold print:border-black">
          <dt>{texts.total}</dt>
          <dd>{money(order.total)}</dd>
        </div>
        {order.refundedTotal > 0 && (
          <div className="flex justify-between text-xs">
            <dt>{texts.refunded}</dt>
            <dd>−{money(order.refundedTotal)}</dd>
          </div>
        )}
      </dl>

      {invoice.note && <p className="border-t border-border pt-4 text-center text-xs text-muted print:border-black/30 print:text-black">{invoice.note}</p>}
    </article>
  );
}
