import Image from "next/image";
import Link from "next/link";
import type { ProductCard as ProductCardData } from "@/lib/api/storefront";
import { productPath } from "@/lib/catalog/product-path";
import type { DisplayCurrency } from "@/lib/currency/display";
import { Price, type PriceTexts } from "./Price";

export type ProductCardTexts = PriceTexts & { outOfStock: string };

type Props = {
  product: ProductCardData;
  locale: string;
  money: DisplayCurrency;
  texts: ProductCardTexts;
  /** Load the image early (the first cards above the fold). */
  priority?: boolean;
};

/** One product in a rail or list: square image, two-line name, price. The whole card is one link. */
export function ProductCard({ product, locale, money, texts, priority = false }: Props) {
  return (
    <Link
      href={productPath(locale, product.number, product.slug)}
      className="flex h-full flex-col gap-2 rounded-2xl border border-border bg-surface p-3 text-foreground transition-colors hover:border-primary"
    >
      <span className="relative block aspect-square overflow-hidden rounded-xl bg-surface-muted">
        {product.imageUrl && (
          <Image
            src={product.imageUrl}
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
      <span className="mt-auto">
        <Price price={product.price} compareAtPrice={product.compareAtPrice} money={money} locale={locale} texts={texts} />
      </span>
    </Link>
  );
}
