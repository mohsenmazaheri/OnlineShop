import Image from "next/image";
import Link from "next/link";
import { CompareButton, type CompareButtonTexts } from "@/components/compare/CompareButton";
import type { ProductCard as ProductCardData } from "@/lib/api/storefront";
import { productPath } from "@/lib/catalog/product-path";
import { imageVariant } from "@/lib/media/image";
import type { DisplayCurrency } from "@/lib/currency/display";
import { Stars } from "@/components/ui/Stars";
import { formatNumber } from "@/lib/i18n/format-money";
import { Countdown, type CountdownTexts } from "./Countdown";
import { Price, type PriceTexts } from "./Price";

export type ProductCardTexts = PriceTexts & CompareButtonTexts & CountdownTexts & { outOfStock: string; ratingLabel: string };

type Props = {
  product: ProductCardData;
  locale: string;
  money: DisplayCurrency;
  texts: ProductCardTexts;
  /** Load the image early (the first cards above the fold). */
  priority?: boolean;
};

/**
 * One product in a rail or list: square image, two-line name, price. The card is one link; the compare button sits
 * beside it (on the image corner), not inside it.
 */
export function ProductCard({ product, locale, money, texts, priority = false }: Props) {
  return (
    <div className="relative h-full">
      <Link
        href={productPath(locale, product.number, product.slug)}
        className="flex h-full flex-col gap-2 rounded-2xl border border-border bg-surface p-3 text-foreground transition-colors hover:border-primary"
      >
        <span className="relative block aspect-square overflow-hidden rounded-xl bg-surface-muted">
          {product.imageUrl && (
            <Image
              src={imageVariant(product.imageUrl, 600)}
              alt=""
              width={300}
              height={300}
              unoptimized
              priority={priority}
              className="h-full w-full object-cover"
            />
          )}
          {!product.inStock && (
            <span className="absolute inset-x-2 bottom-2 rounded-lg bg-inverse/80 py-0.5 text-center text-xs text-white">{texts.outOfStock}</span>
          )}
        </span>
        <span className="line-clamp-2 min-h-[2.8em] text-sm leading-snug">{product.name}</span>
        {product.ratingCount > 0 && <RatingLine average={product.ratingAverage} count={product.ratingCount} locale={locale} label={texts.ratingLabel} />}
        <span className="mt-auto flex flex-col gap-1">
          {product.saleEndsUtc && <Countdown endsUtc={product.saleEndsUtc} locale={locale} texts={texts} className="text-danger" />}
          <Price price={product.price} compareAtPrice={product.compareAtPrice} money={money} locale={locale} texts={texts} />
        </span>
      </Link>
      <span className="absolute end-5 top-5">
        <CompareButton locale={locale} number={product.number} category={product.categorySlug} texts={texts} look="icon" />
      </span>
    </div>
  );
}

/** "★★★★½ 4.5 (12)": the stars and numbers for the eye, the full sentence for screen readers. */
export function RatingLine({ average, count, locale, label }: { average: number; count: number; locale: string; label: string }) {
  const value = formatNumber(average, locale, 1);
  const total = formatNumber(count, locale);
  return (
    <span className="flex items-center gap-1 text-xs text-muted">
      <Stars rating={average} size={12} label={null} />
      <span aria-hidden>
        {value} ({total})
      </span>
      <span className="sr-only">{label.replace("{average}", value).replace("{count}", total)}</span>
    </span>
  );
}
