import { discountShare, formatMoney, formatPercent } from "@/lib/i18n/format-money";

export type PriceTexts = { oldPrice: string; discount: string };

type Props = {
  price: number;
  compareAtPrice?: number | null;
  currency: string;
  locale: string;
  texts: PriceTexts;
  size?: "md" | "lg";
};

/** The price, and for a discount the struck-out old price and a "20% off" badge. Server-rendered, no client code. */
export function Price({ price, compareAtPrice, currency, locale, texts, size = "md" }: Props) {
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
            {formatMoney(compareAtPrice!, currency, locale)}
          </del>
        </div>
      )}
      <strong className={size === "lg" ? "text-2xl font-extrabold" : "text-base font-extrabold"}>
        {formatMoney(price, currency, locale)}
      </strong>
    </div>
  );
}
