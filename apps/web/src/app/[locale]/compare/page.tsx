import type { Metadata } from "next";
import { Suspense } from "react";
import { CompareTable } from "@/components/compare/CompareTable";
import { ButtonLink } from "@/components/ui/Button";
import { firstParam, routeLocale } from "@/lib/account/page-helpers";
import { getCompareList } from "@/lib/account/session";
import { getComparison } from "@/lib/api/storefront";
import { parseIds } from "@/lib/compare/list";
import { loadDisplayCurrency } from "@/lib/currency/load";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { loadStore } from "@/lib/store/load";

// A comparison is any combination of products: never indexed (robots.txt disallows /*/compare too).
export async function generateMetadata({ params }: PageProps<"/[locale]/compare">): Promise<Metadata> {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return {
    title: dict.compare.title,
    description: dict.compare.metaDescription,
    robots: { index: false, follow: true },
    alternates: { canonical: `/${locale}/compare` },
  };
}

type Search = Promise<Record<string, string | string[] | undefined>>;

/**
 * The products of ?ids= side by side (a shareable address). Opened without ids, a signed-in customer sees their saved
 * list. Prices and stock are read now, never from a cache.
 */
async function Comparison({ locale, dict, searchParams }: { locale: Locale; dict: Dictionary; searchParams: Search }) {
  const all = await searchParams;
  const store = await loadStore();
  const { money } = await loadDisplayCurrency(locale, store.currency);
  let ids = parseIds(all.ids);
  if (ids.length === 0) ids = parseIds((await getCompareList())?.numbers.join(","));
  const t = dict.compare;
  const empty = (
    <div className="flex flex-col items-start gap-4">
      <p className="text-muted">{t.empty}</p>
      <ButtonLink href={`/${locale}`} variant="secondary">
        {t.backHome}
      </ButtonLink>
    </div>
  );
  if (ids.length === 0) return empty;

  const comparison = await getComparison(ids, locale).catch(() => null);
  if (!comparison)
    return (
      <p role="alert" className="text-muted">
        {dict.home.unavailable}
      </p>
    );
  if (comparison.products.length === 0) return empty;
  return (
    <CompareTable
      comparison={comparison}
      locale={locale}
      money={money}
      onlyDifferences={firstParam(all.diff) === "1"}
      texts={{ compare: t, product: dict.product }}
    />
  );
}

export default async function ComparePage({ params, searchParams }: PageProps<"/[locale]/compare">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <main className="mx-auto flex w-full max-w-7xl flex-1 flex-col gap-4 px-4 py-6">
      <h1 className="text-xl font-extrabold">{dict.compare.title}</h1>
      <Suspense fallback={<p className="text-muted">…</p>}>
        <Comparison locale={locale} dict={dict} searchParams={searchParams} />
      </Suspense>
    </main>
  );
}
