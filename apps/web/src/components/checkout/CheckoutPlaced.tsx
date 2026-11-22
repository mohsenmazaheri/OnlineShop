import { ButtonLink } from "@/components/ui/Button";
import { Icon } from "@/components/ui/Icon";
import type { PlacedOrder } from "@/lib/checkout/form";
import type { DisplayCurrency } from "@/lib/currency/display";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatDate } from "@/lib/i18n/format-date";
import { formatMoney } from "@/lib/i18n/format-money";

type Props = { locale: string; order: PlacedOrder; money: DisplayCurrency; texts: Dictionary["checkout"] };

/** The confirmation after placing: the order number, the amount and until when the items are held for the payment. */
export function CheckoutPlaced({ locale, order, money, texts }: Props) {
  const number = new Intl.NumberFormat(locale, { useGrouping: false }).format(order.number);
  const total = formatMoney(order.total, order.currency, locale, { symbol: money.code === order.currency ? money.symbol : null, decimals: money.decimals });
  return (
    <section role="status" className="mx-auto flex max-w-lg flex-col items-center gap-4 rounded-2xl border border-border bg-surface p-6 text-center">
      <span className="tint tint-2 flex h-16 w-16 items-center justify-center rounded-2xl">
        <Icon name="receipt" size={30} />
      </span>
      <h2 className="text-lg font-extrabold">{texts.placedTitle}</h2>
      <p>
        {texts.placedNumber.replace("{number}", "")}
        <strong dir="ltr">{number}</strong>
      </p>
      <p>{texts.placedTotal.replace("{total}", total)}</p>
      <p className="text-sm text-muted">{texts.placedPayment.replace("{time}", formatDate(order.paymentDueUtc, locale, true))}</p>
      <ButtonLink href={`/${locale}`}>{texts.continueShopping}</ButtonLink>
    </section>
  );
}
