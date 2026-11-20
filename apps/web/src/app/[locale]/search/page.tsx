import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { Suspense } from "react";
import { SearchForm } from "@/components/layout/SearchForm";
import { ProductListing } from "@/components/listing/ProductListing";
import { ProductRail } from "@/components/product/ProductRail";
import { firstParam, routeLocale } from "@/lib/account/page-helpers";
import { getProducts } from "@/lib/api/storefront";
import { hasFilters } from "@/lib/catalog/browse";
import { loadListing } from "@/lib/catalog/listing-page";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

// Search result pages are never indexed (robots.txt disallows /*/search as well).
export async function generateMetadata({ params, searchParams }: PageProps<"/[locale]/search">): Promise<Metadata> {
  const dict = await getDictionary(routeLocale((await params).locale));
  const query = firstParam((await searchParams).q).trim().slice(0, 200);
  return { title: query ? dict.listing.searchTitle.replace("{query}", query) : dict.search.title, robots: { index: false, follow: true } };
}

type Search = Promise<Record<string, string | string[] | undefined>>;

/**
 * The results of a text search, with the same filters and sorts as a category (relevance first). A product code opens
 * the product; words typed on the wrong keyboard are searched as meant (and said so); a misspelling gets "did you
 * mean"; with nothing found at all, the newest products instead of an empty page.
 */
async function Results({ locale, dict, searchParams }: { locale: Locale; dict: Dictionary; searchParams: Search }) {
  const all = await searchParams;
  const query = firstParam(all.q).trim().slice(0, 200);
  if (!query) {
    return (
      <>
        <h1 className="text-xl font-extrabold">{dict.search.title}</h1>
        <SearchForm locale={locale} texts={dict.header} defaultValue="" />
        <p className="text-muted">{dict.search.prompt}</p>
      </>
    );
  }
  const listing = await loadListing(locale, { q: query }, all, `/${locale}/search`);
  const help = listing?.result.search;
  if (help?.exactNumber && listing && listing.result.page === 1 && !hasFilters(listing.params)) {
    // The slug is filled in by proxy.ts (301 to the product's address in this language).
    redirect(`/${locale}/p/${help.exactNumber}`);
  }
  const t = dict.listing;
  const nothing = listing !== null && listing.result.total === 0 && !listing.result.filtered;

  return (
    <>
      <h1 className="text-xl font-extrabold">{t.searchTitle.replace("{query}", help?.correctedText ?? query)}</h1>
      <SearchForm locale={locale} texts={dict.header} defaultValue={query} />
      {help?.correctedText && <p className="text-sm text-muted">{t.corrected.replace("{corrected}", help.correctedText).replace("{query}", query)}</p>}
      {help?.didYouMean && (
        <p className="text-sm">
          <Link href={`/${locale}/search?q=${encodeURIComponent(help.didYouMean)}`} className="font-medium text-primary underline-offset-2 hover:underline">
            {t.didYouMean.replace("{suggestion}", help.didYouMean)}
          </Link>
        </p>
      )}
      {!listing ? (
        <p role="alert" className="text-muted">
          {dict.home.unavailable}
        </p>
      ) : nothing ? (
        <>
          <p className="text-muted">{t.searchNone.replace("{query}", query)}</p>
          <Suspense fallback={null}>
            <Newest locale={locale} dict={dict} money={listing.money} />
          </Suspense>
        </>
      ) : (
        <ProductListing locale={locale} dict={dict} result={listing.result} basePath={listing.basePath} params={listing.params} money={listing.money} />
      )}
    </>
  );
}

/** Instead of an empty page: the newest products (best sellers once there are orders, phase 7). */
async function Newest({ locale, dict, money }: { locale: Locale; dict: Dictionary; money: Parameters<typeof ProductRail>[0]["money"] }) {
  const products = await getProducts("Newest", locale, 8).catch(() => []);
  return <ProductRail id="newest" heading={dict.listing.newestTitle} products={products} locale={locale} money={money} texts={dict.product} variant="grid" />;
}

export default async function SearchPage({ params, searchParams }: PageProps<"/[locale]/search">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <main className="mx-auto flex w-full max-w-7xl flex-1 flex-col gap-4 px-4 py-6">
      <Suspense fallback={<h1 className="text-xl font-extrabold">{dict.search.title}</h1>}>
        <Results locale={locale} dict={dict} searchParams={searchParams} />
      </Suspense>
    </main>
  );
}
