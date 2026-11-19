import { convertAmount, type DisplayCurrency } from "@/lib/currency/display";
import { discountShare, formatMoney, formatPercent } from "@/lib/i18n/format-money";

export type PriceTexts = { oldPrice: string; discount: string };

type Props = {
  /** Price in the shop's base currency (as the API returns it). */
  price: number;
  compareAtPrice?: number | null;
  /** The currency shown on this request; amounts are converted to it. */
  money: DisplayCurrency;
  locale: string;
  texts: PriceTexts;
  size?: "md" | "lg";
};

/** A base-currency amount as the visitor sees it: converted, rounded and formatted. */
export function displayMoney(amount: number, money: DisplayCurrency, locale: string): string {
  return formatMoney(convertAmount(amount, money), money.code, locale, { symbol: money.symbol, decimals: money.decimals });
}

/** The price, and for a discount the struck-out old price and a percentage badge. Server-rendered, no client code. */
export function Price({ price, compareAtPrice, money, locale, texts, size = "md" }: Props) {
  // The discount share comes from the base prices (conversion and rounding must not change it).
  const share = discountShare(price, compareAtPrice);
  return (
    <div className="flex flex-col gap-0.5">
      {share !== null && (
        <div className="flex items-center justify-between gap-2">
          {/* Just "28%" on screen (cards are narrow); "28% off" for screen readers. */}
          <span
            aria-label={texts.discount.replace("{percent}", formatPercent(share, locale))}
            className="rounded-md bg-accent px-1.5 py-px text-xs font-extrabold whitespace-nowrap text-accent-foreground"
          >
            {formatPercent(share, locale)}
          </span>
          <del className="truncate text-xs whitespace-nowrap text-muted" aria-label={texts.oldPrice}>
            {displayMoney(compareAtPrice!, money, locale)}
          </del>
        </div>
      )}
      <strong className={size === "lg" ? "text-2xl font-extrabold" : "text-base font-extrabold"}>{displayMoney(price, money, locale)}</strong>
    </div>
  );
}
