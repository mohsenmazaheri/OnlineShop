import Image from "next/image";
import Link from "next/link";
import { Button } from "@/components/ui/Button";
import type { ProductDetail } from "@/lib/api/storefront";
import { formatSpecValue } from "@/lib/catalog/specs";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { Price } from "./Price";
import { SpecTable } from "./SpecTable";

type Props = { product: ProductDetail; locale: string; currency: string; texts: Dictionary["product"] };

/**
 * The product page body (Digikala-style structure: breadcrumb, image, title with key facts, a price box; then the
 * description and the full specifications). Pure presentation; the page loads the data and writes the metadata.
 */
export function ProductView({ product, locale, currency, texts }: Props) {
  const keyFacts = product.specs.filter((s) => s.inSummary).slice(0, 6);

  return (
    <article className="flex flex-col gap-6 md:gap-8">
      <nav aria-label={texts.breadcrumb} className="text-xs text-muted">
        <ol className="flex flex-wrap items-center gap-1.5">
          <li>
            <Link href={`/${locale}`} className="hover:text-primary">
              {texts.home}
            </Link>
          </li>
          <li aria-hidden>/</li>
          <li>{product.category.name}</li>
          <li aria-hidden>/</li>
          <li>{product.subCategory.name}</li>
        </ol>
      </nav>

      <div className="grid gap-6 md:grid-cols-[minmax(0,5fr)_minmax(0,6fr)] lg:grid-cols-[minmax(0,5fr)_minmax(0,5fr)_minmax(0,3fr)]">
        <div className="overflow-hidden rounded-[calc(var(--radius)*1.75)] border border-border bg-surface">
          {product.images[0] && (
            // The main image is the page's largest element: loaded first, fixed aspect ratio (no layout shift).
            <Image src={product.images[0]} alt={product.name} width={600} height={600} unoptimized priority className="aspect-square h-auto w-full object-cover" />
          )}
        </div>

        <div className="flex flex-col gap-4">
          <h1 className="text-xl leading-snug font-extrabold md:text-2xl">{product.name}</h1>
          <p className="text-xs text-muted">
            {texts.sku}: <span dir="ltr">{product.sku}</span>
          </p>
          {keyFacts.length > 0 && (
            <section aria-labelledby="facts-heading" className="flex flex-col gap-2">
              <h2 id="facts-heading" className="text-sm font-bold">
                {texts.keyFacts}
              </h2>
              <ul className="flex flex-col gap-1.5 text-sm">
                {keyFacts.map((fact) => (
                  <li key={fact.code} className="flex gap-2">
                    <span aria-hidden className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                    <span>
                      <span className="text-muted">{fact.name}: </span>
                      {formatSpecValue(fact, locale, texts)}
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </div>

        <aside aria-label={texts.price} className="flex h-fit flex-col gap-4 rounded-2xl border border-border bg-surface p-4 md:col-span-2 lg:col-span-1">
          <Price price={product.price} compareAtPrice={product.compareAtPrice} currency={currency} locale={locale} texts={texts} size="lg" />
          <p className={`flex items-center gap-2 text-sm font-medium ${product.inStock ? "text-success" : "text-danger"}`}>
            <span aria-hidden className="h-2 w-2 rounded-full bg-current" />
            {product.inStock ? texts.inStock : texts.outOfStock}
          </p>
          {/* Checkout arrives in phase 7: the button is shown, honestly disabled. */}
          <Button size="lg" disabled aria-describedby="cart-soon">
            {texts.addToCart}
          </Button>
          <p id="cart-soon" className="text-xs text-muted">
            {texts.cartSoon}
          </p>
        </aside>
      </div>

      {product.description && (
        <section aria-labelledby="description-heading" className="flex flex-col gap-2">
          <h2 id="description-heading" className="text-lg font-extrabold">
            {texts.description}
          </h2>
          <p className="max-w-3xl leading-loose">{product.description}</p>
        </section>
      )}

      <SpecTable specs={product.specs} locale={locale} texts={texts} heading={texts.specs} />
    </article>
  );
}
