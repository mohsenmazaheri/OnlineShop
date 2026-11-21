import Image from "next/image";
import Link from "next/link";
import { Price } from "@/components/product/Price";
import { ButtonLink } from "@/components/ui/Button";
import type { ProductComparison } from "@/lib/api/storefront";
import { productPath } from "@/lib/catalog/product-path";
import { formatCompareValue, visibleGroups } from "@/lib/compare/format";
import { comparePath } from "@/lib/compare/list";
import type { DisplayCurrency } from "@/lib/currency/display";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { imageVariant } from "@/lib/media/image";
import { CompareRemove } from "./CompareRemove";

type Props = {
  comparison: ProductComparison;
  locale: string;
  money: DisplayCurrency;
  onlyDifferences: boolean;
  texts: { compare: Dictionary["compare"]; product: Dictionary["product"] };
};

const column = "w-40 min-w-40 md:w-56 md:min-w-56";

/**
 * The comparison table (server-rendered, works without JavaScript): one column per product with its current price
 * and stock, then the comparable specs by group. Each spec's name is a full-width row above its values, so two
 * products fit side by side on a phone and more scroll sideways. The best value of a spec with a better direction is
 * marked; "only differences" hides rows where every product has the same value.
 */
export function CompareTable({ comparison, locale, money, onlyDifferences, texts }: Props) {
  const t = texts.compare;
  const { products } = comparison;
  const numbers = products.map((p) => p.number);
  const groups = visibleGroups(comparison.groups, onlyDifferences);
  const span = products.length;
  const productId = (number: number) => `cmp-p-${number}`;

  return (
    <div className="flex flex-col gap-4">
      {products.length > 1 && (
        <nav aria-label={t.title} className="flex gap-2 text-sm">
          <Link
            href={comparePath(locale, numbers)}
            rel="nofollow"
            aria-current={onlyDifferences ? undefined : "page"}
            className="rounded-full border border-border px-3 py-1.5 aria-[current=page]:border-primary aria-[current=page]:text-primary"
          >
            {t.showAll}
          </Link>
          <Link
            href={comparePath(locale, numbers, true)}
            rel="nofollow"
            aria-current={onlyDifferences ? "page" : undefined}
            className="rounded-full border border-border px-3 py-1.5 aria-[current=page]:border-primary aria-[current=page]:text-primary"
          >
            {t.onlyDifferences}
          </Link>
        </nav>
      )}
      {comparison.mixedCategories && (
        <p role="note" className="rounded-xl bg-surface-muted px-3 py-2 text-sm">
          {t.mixed}
        </p>
      )}

      <div className="-mx-4 overflow-x-auto px-4">
        <table className="w-max border-separate border-spacing-0 text-sm">
          <thead>
            <tr>
              {products.map((p) => (
                <th key={p.number} id={productId(p.number)} scope="col" className={`${column} border-b border-border p-2 text-start align-top font-normal`}>
                  <div className="flex flex-col gap-2">
                    <div className="flex items-start justify-between gap-2">
                      <span className="relative block aspect-square w-24 overflow-hidden rounded-xl bg-surface-muted md:w-32">
                        {p.imageUrl && (
                          <Image src={imageVariant(p.imageUrl, 300)} alt="" width={128} height={128} unoptimized className="h-full w-full object-cover" />
                        )}
                      </span>
                      <CompareRemove
                        href={comparePath(locale, numbers.filter((n) => n !== p.number), onlyDifferences)}
                        number={p.number}
                        label={t.remove.replace("{name}", p.name)}
                      />
                    </div>
                    <Link href={productPath(locale, p.number, p.slug)} className="line-clamp-2 min-h-[2.8em] font-bold leading-snug hover:text-primary">
                      {p.name}
                    </Link>
                    {p.brand && <span className="text-xs text-muted">{p.brand}</span>}
                  </div>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            <LabelRow id="cmp-price" span={span} label={t.price} />
            <tr>
              {products.map((p) => (
                <td key={p.number} headers={`cmp-price ${productId(p.number)}`} className="p-2 align-top">
                  <Price price={p.price} compareAtPrice={p.compareAtPrice} money={money} locale={locale} texts={texts.product} />
                </td>
              ))}
            </tr>
            <LabelRow id="cmp-stock" span={span} label={t.availability} />
            <tr>
              {products.map((p) => (
                <td key={p.number} headers={`cmp-stock ${productId(p.number)}`} className={`p-2 align-top font-medium ${p.inStock ? "text-success" : "text-danger"}`}>
                  {p.inStock ? texts.product.inStock : texts.product.outOfStock}
                </td>
              ))}
            </tr>
          </tbody>
          {groups.map((group, g) => (
            <tbody key={`${group.name}-${g}`}>
              {group.name && (
                <tr>
                  <th colSpan={span} scope="colgroup" className="bg-surface-muted px-2 py-2 text-start text-base font-extrabold">
                    {group.name}
                  </th>
                </tr>
              )}
              {group.rows.map((row) => {
                const rowId = `cmp-${row.code}`;
                return [
                  <LabelRow key={`${row.code}-label`} id={rowId} span={span} label={row.name} />,
                  <tr key={row.code}>
                    {row.values.map((value, i) => {
                      const best = row.best.includes(i);
                      return (
                        <td key={numbers[i]} headers={`${rowId} ${productId(numbers[i])}`} className={`p-2 align-top ${best ? "font-bold text-success" : ""}`}>
                          {formatCompareValue(value, row, locale, { yes: texts.product.yes, no: texts.product.no, noValue: t.noValue })}
                          {best && <span className="ms-1.5 rounded-full bg-success/10 px-2 py-0.5 text-xs">{t.best}</span>}
                        </td>
                      );
                    })}
                  </tr>,
                ];
              })}
            </tbody>
          ))}
          <tfoot>
            <tr>
              {products.map((p) => (
                <td key={p.number} className="p-2 pt-4">
                  <ButtonLink href={productPath(locale, p.number, p.slug)} variant="secondary" className="w-full">
                    {t.viewProduct}
                  </ButtonLink>
                </td>
              ))}
            </tr>
          </tfoot>
        </table>
      </div>
    </div>
  );
}

/** A spec's name across every column, read as the header of the values below it. */
function LabelRow({ id, span, label }: { id: string; span: number; label: string }) {
  return (
    <tr>
      <th id={id} colSpan={span} scope="colgroup" className="border-t border-border px-2 pt-3 pb-1 text-start text-xs font-medium text-muted">
        {label}
      </th>
    </tr>
  );
}
