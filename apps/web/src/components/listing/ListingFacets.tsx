import Link from "next/link";
import type { ReactNode } from "react";
import { listHas, listingHref, toggled, type BrowseFacets, type ListingParams } from "@/lib/catalog/browse";
import type { Dictionary } from "@/lib/i18n/dictionaries";
import { formatNumber } from "@/lib/i18n/format-money";

type Texts = Dictionary["listing"];

type Props = {
  locale: string;
  texts: Texts;
  facets: BrowseFacets;
  basePath: string;
  params: ListingParams;
  /** A base-currency price in the display currency, as a number (for the price inputs' hints). */
  displayPrice: (amount: number) => number;
};

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-2 border-b border-border pb-4 last:border-b-0">
      <h3 className="text-sm font-bold">{title}</h3>
      {children}
    </section>
  );
}

/**
 * One filter choice as a real link (works without JavaScript; rel="nofollow" so crawlers do not wander through every
 * combination). A choice with no products under the other filters is shown but not linked.
 */
function Choice({ href, label, count, selected, locale }: { href: string; label: string; count?: number; selected: boolean; locale: string }) {
  const box = (
    <span
      aria-hidden
      className={`flex h-5 w-5 shrink-0 items-center justify-center rounded-md border ${selected ? "border-primary bg-primary text-primary-foreground" : "border-border"}`}
    >
      {selected ? "✓" : ""}
    </span>
  );
  const body = (
    <>
      {box}
      <span className="flex-1">{label}</span>
      {count !== undefined && <span className="text-xs text-muted tabular-nums">{formatNumber(count, locale)}</span>}
    </>
  );
  if (count === 0 && !selected) {
    return <span className="flex min-h-9 items-center gap-2 text-sm text-muted opacity-60">{body}</span>;
  }
  return (
    <Link href={href} rel="nofollow" aria-current={selected ? "true" : undefined} className="flex min-h-9 items-center gap-2 rounded-lg text-sm hover:text-primary">
      {body}
    </Link>
  );
}

/** A small GET form for a range (price, a number spec), keeping every other parameter of the address. */
function RangeForm({ basePath, params, minName, maxName, texts, hint, min, max }: {
  basePath: string;
  params: ListingParams;
  minName: string;
  maxName: string;
  texts: Texts;
  hint: string | null;
  min?: number;
  max?: number;
}) {
  const kept = Object.entries(params).filter(([key]) => key !== minName && key !== maxName && key !== "page");
  const input = "h-10 w-full min-w-0 rounded-lg border border-border bg-surface px-2 text-sm";
  return (
    <form method="get" action={basePath} className="flex flex-col gap-2">
      {kept.map(([key, value]) => (
        <input key={key} type="hidden" name={key} value={value} />
      ))}
      <div className="flex items-end gap-2">
        <label className="flex flex-1 flex-col gap-1 text-xs">
          {texts.from}
          <input name={minName} type="number" inputMode="decimal" min={0} step="any" defaultValue={params[minName] ?? ""} placeholder={min === undefined ? "" : String(min)} className={input} dir="ltr" />
        </label>
        <label className="flex flex-1 flex-col gap-1 text-xs">
          {texts.to}
          <input name={maxName} type="number" inputMode="decimal" min={0} step="any" defaultValue={params[maxName] ?? ""} placeholder={max === undefined ? "" : String(max)} className={input} dir="ltr" />
        </label>
        <button type="submit" className="h-10 rounded-lg border border-border px-3 text-sm">
          {texts.apply}
        </button>
      </div>
      {hint && <p className="text-xs text-muted">{hint}</p>}
    </form>
  );
}

/** The listing's filters, each counting with the other filters applied (the API's facets). */
export function ListingFacets({ locale, texts, facets, basePath, params, displayPrice }: Props) {
  const href = (key: string, value: string) => listingHref(basePath, params, { [key]: toggled(params[key], value) });
  const flag = (key: string) => listingHref(basePath, params, { [key]: params[key] === "1" ? null : "1" });
  const price = facets.price;

  return (
    <div className="flex flex-col gap-4">
      {facets.subCategories.length > 0 && (
        <Section title={texts.subCategories}>
          <ul className="flex flex-col gap-1">
            {facets.subCategories.map((s) => (
              <li key={s.path}>
                <Link href={`/${locale}/${s.path}`} className="flex min-h-9 items-center justify-between gap-2 text-sm hover:text-primary">
                  <span>{s.name}</span>
                  <span className="text-xs text-muted tabular-nums">{formatNumber(s.count, locale)}</span>
                </Link>
              </li>
            ))}
          </ul>
        </Section>
      )}

      <Section title={texts.availability}>
        <Choice href={flag("stock")} label={texts.inStock} count={facets.inStock} selected={facets.inStockSelected} locale={locale} />
        <Choice href={flag("sale")} label={texts.onSale} count={facets.discounted} selected={facets.discountedSelected} locale={locale} />
      </Section>

      {facets.brands.length > 0 && (
        <Section title={texts.brands}>
          {facets.brands.map((b) => (
            <Choice key={b.value} href={href("brand", b.value)} label={b.label} count={b.count} selected={b.selected} locale={locale} />
          ))}
        </Section>
      )}

      {facets.groups.length > 0 && (
        <Section title={texts.groups}>
          {facets.groups.map((g) => (
            <Choice key={g.value} href={href("group", g.value)} label={g.label} count={g.count} selected={g.selected} locale={locale} />
          ))}
        </Section>
      )}

      {price && (
        <Section title={texts.price}>
          <RangeForm
            basePath={basePath}
            params={params}
            minName="min"
            maxName="max"
            texts={texts}
            min={displayPrice(price.min)}
            max={displayPrice(price.max)}
            hint={texts.rangeHint.replace("{min}", formatNumber(displayPrice(price.min), locale)).replace("{max}", formatNumber(displayPrice(price.max), locale))}
          />
        </Section>
      )}

      {facets.specs.map((spec) => {
        const title = spec.unit && spec.kind === "Number" ? `${spec.name} (${spec.unit})` : spec.name;
        if (spec.kind === "Option") {
          if (spec.options.length === 0) return null;
          return (
            <Section key={spec.code} title={title}>
              {spec.options.map((o) => (
                <Choice
                  key={o.value}
                  href={href(`o.${spec.code}`, o.value)}
                  label={o.label}
                  count={o.count}
                  selected={o.selected || listHas(params[`o.${spec.code}`], o.value)}
                  locale={locale}
                />
              ))}
            </Section>
          );
        }
        if (spec.kind === "Number") {
          if (spec.min === null || spec.max === null || spec.min === spec.max) return null;
          return (
            <Section key={spec.code} title={title}>
              <RangeForm
                basePath={basePath}
                params={params}
                minName={`nmin.${spec.code}`}
                maxName={`nmax.${spec.code}`}
                texts={texts}
                min={spec.min}
                max={spec.max}
                hint={texts.rangeHint.replace("{min}", formatNumber(spec.min, locale)).replace("{max}", formatNumber(spec.max, locale))}
              />
            </Section>
          );
        }
        if (spec.yesCount === 0 && !spec.yesSelected) return null;
        return (
          <Section key={spec.code} title={title}>
            <Choice href={flag(`y.${spec.code}`)} label={texts.yes} count={spec.yesCount} selected={spec.yesSelected} locale={locale} />
          </Section>
        );
      })}
    </div>
  );
}
