import type { ProductCard as ProductCardData } from "@/lib/api/storefront";
import { Icon } from "@/components/ui/Icon";
import type { DisplayCurrency } from "@/lib/currency/display";
import { ProductCard, type ProductCardTexts } from "./ProductCard";

type Props = {
  id: string;
  heading: string;
  products: ProductCardData[];
  locale: string;
  money: DisplayCurrency;
  texts: ProductCardTexts;
  /** "deals": a coloured band with a sideways-scrolling row; "grid": cards in a grid. */
  variant?: "deals" | "grid";
};

/** A titled row of product cards for the home page. Renders nothing for an empty list. */
export function ProductRail({ id, heading, products, locale, money, texts, variant = "grid" }: Props) {
  if (products.length === 0) return null;
  const headingId = `${id}-heading`;

  if (variant === "deals") {
    return (
      <section
        aria-labelledby={headingId}
        className="-mx-4 flex flex-col gap-3 bg-primary px-4 py-4 text-primary-foreground md:mx-0 md:flex-row md:items-stretch md:gap-4 md:rounded-[calc(var(--radius)*2)] md:p-5 dark:bg-[color-mix(in_srgb,var(--primary)_28%,var(--surface))] dark:text-foreground"
      >
        <div className="flex items-center gap-2 md:w-44 md:shrink-0 md:flex-col md:items-start md:justify-center md:gap-3">
          <span className="flex h-8 w-8 items-center justify-center rounded-xl bg-accent text-accent-foreground md:h-12 md:w-12">
            <Icon name="bolt" size={22} />
          </span>
          <h2 id={headingId} className="text-lg font-extrabold md:text-2xl md:leading-tight">
            {heading}
          </h2>
        </div>
        <ul className="flex snap-x gap-2 overflow-x-auto pb-1 md:gap-3">
          {products.map((product, i) => (
            <li key={product.number} className="w-40 shrink-0 snap-start md:w-48">
              <ProductCard product={product} locale={locale} money={money} texts={texts} priority={i < 2} />
            </li>
          ))}
        </ul>
      </section>
    );
  }

  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-4">
      <h2 id={headingId} className="text-lg font-extrabold md:text-2xl">
        {heading}
      </h2>
      <ul className="grid grid-cols-2 gap-2 sm:grid-cols-3 md:gap-3 lg:grid-cols-4 xl:grid-cols-6">
        {products.map((product) => (
          <li key={product.number}>
            <ProductCard product={product} locale={locale} money={money} texts={texts} />
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Space-keeping placeholder while a rail loads (no layout shift). */
export function ProductRailSkeleton({ label }: { label: string }) {
  return (
    <div role="status" aria-label={label} className="grid grid-cols-2 gap-2 sm:grid-cols-3 md:gap-3 lg:grid-cols-4 xl:grid-cols-6">
      {Array.from({ length: 6 }, (_, i) => (
        <div key={i} className={`h-72 animate-pulse rounded-2xl bg-border ${i >= 2 ? "hidden sm:block" : ""}`} />
      ))}
    </div>
  );
}
