import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { ProductCard } from "@/components/product/ProductCard";
import { routeLocale } from "@/lib/account/page-helpers";
import { browseProducts, getLandingPage } from "@/lib/api/storefront";
import { sortName, sortsFor } from "@/lib/catalog/browse";
import { decodeSegment } from "@/lib/catalog/listing-page";
import { loadDisplayCurrency } from "@/lib/currency/load";
import { getDictionary } from "@/lib/i18n/dictionaries";
import { formatNumber } from "@/lib/i18n/format-money";
import { siteUrl } from "@/lib/seo/config";
import { landingApiQuery, landingChoices, landingJsonLd, landingPath, landingSeo } from "@/lib/seo/landing";
import { jsonLdScript } from "@/lib/seo/product";
import { loadStore } from "@/lib/store/load";

type Props = PageProps<"/[locale]/l/[slug]">;

/** The page, the visitor's sort and page, and the products (the fetchers are cached). */
async function load({ params, searchParams }: Pick<Props, "params" | "searchParams">) {
  const { locale: raw, slug } = await params;
  const locale = routeLocale(raw);
  const store = await loadStore();
  const page = await getLandingPage(decodeSegment(slug), locale).catch(() => null);
  if (!page) return null;
  const choices = landingChoices(await searchParams);
  const result = await browseProducts(landingApiQuery(page, locale, choices)).catch(() => null);
  return { locale, store, page, choices, result };
}

export async function generateMetadata(props: Props): Promise<Metadata> {
  const data = await load(props);
  if (!data) return { robots: { index: false, follow: false } };
  const { locale, store, page, choices } = data;
  const seo = landingSeo(page, locale, store.defaultLocale, siteUrl(), choices);
  const title = page.metaTitle || page.title;
  const description = page.metaDescription || page.intro?.slice(0, 160);
  return {
    title,
    description,
    alternates: { canonical: seo.canonical, languages: seo.languages },
    robots: seo.indexable ? undefined : { index: false, follow: true },
    openGraph: { siteName: store.name, locale, type: "website", title, description, url: seo.canonical },
  };
}

/**
 * A landing page (/{locale}/l/{slug}): a clean, indexed address for a much-searched combination, with its own title
 * and text above the products of a fixed listing. Visitors may sort and page (those addresses are not indexed or are
 * their own page); the filters are part of the page, so there is no filter panel.
 */
async function Landing(props: Pick<Props, "params" | "searchParams">) {
  const data = await load(props);
  if (!data) notFound();
  const { locale, store, page, choices, result } = data;
  const [dict, { money }] = await Promise.all([getDictionary(locale), loadDisplayCurrency(locale, store.currency)]);
  const t = dict.listing;
  const path = landingPath(locale, page.slug);
  const href = (change: { sort?: string | null; page?: number }) => {
    const query = new URLSearchParams();
    const sort = change.sort === undefined ? choices.sort : change.sort;
    if (sort) query.set("sort", sort);
    if ((change.page ?? 1) > 1) query.set("page", String(change.page));
    const text = query.toString();
    return text ? `${path}?${text}` : path;
  };
  const current = result ? sortName(result.sort) : "newest";
  const pages = result ? Math.max(1, Math.ceil(result.total / result.pageSize)) : 1;

  return (
    <>
      {result && (
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: jsonLdScript(landingJsonLd(page, result, locale, siteUrl(), t.home, `${siteUrl()}${path}`)) }}
        />
      )}
      <nav aria-label={dict.product.breadcrumb} className="text-xs text-muted">
        <ol className="flex flex-wrap items-center gap-1.5">
          <li>
            <Link href={`/${locale}`} className="hover:text-primary">
              {t.home}
            </Link>
          </li>
          <li aria-hidden>/</li>
          <li aria-current="page">{page.title}</li>
        </ol>
      </nav>
      <h1 className="text-xl font-extrabold md:text-2xl">{page.title}</h1>
      {page.intro && (
        <div className="flex max-w-3xl flex-col gap-2 leading-loose text-muted">
          {page.intro.split(/\n\s*\n/).map((paragraph, i) => (
            <p key={i}>{paragraph}</p>
          ))}
        </div>
      )}
      {!result ? (
        <p role="alert" className="text-muted">
          {dict.home.unavailable}
        </p>
      ) : result.total === 0 ? (
        <p className="text-muted">{t.emptyScope}</p>
      ) : (
        <>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-sm text-muted">{t.productCount.replace("{count}", formatNumber(result.total, locale))}</p>
            <nav aria-label={t.sortLabel} className="flex flex-wrap items-center gap-1 text-sm">
              <span className="text-muted">{t.sortLabel}:</span>
              {sortsFor(false).map((s) => (
                <Link
                  key={s}
                  href={href({ sort: s === "newest" ? null : s })}
                  rel="nofollow"
                  aria-current={s === current ? "true" : undefined}
                  className="rounded-lg px-2 py-1 hover:bg-surface-muted aria-[current=true]:bg-primary/10 aria-[current=true]:font-bold aria-[current=true]:text-primary"
                >
                  {t.sorts[s]}
                </Link>
              ))}
            </nav>
          </div>
          <ul className="grid grid-cols-2 gap-2 sm:grid-cols-3 md:gap-3 lg:grid-cols-4 xl:grid-cols-5">
            {result.items.map((product, i) => (
              <li key={product.number}>
                <ProductCard product={product} locale={locale} money={money} texts={dict.product} priority={i < 4} />
              </li>
            ))}
          </ul>
          {pages > 1 && (
            <nav aria-label={t.pages} className="flex flex-wrap gap-1 text-sm">
              {Array.from({ length: pages }, (_, i) => i + 1).map((p) => (
                <Link
                  key={p}
                  href={href({ page: p })}
                  aria-current={p === result.page ? "page" : undefined}
                  className="grid h-10 min-w-10 place-items-center rounded-lg border border-border px-2 aria-[current=page]:border-primary aria-[current=page]:font-bold aria-[current=page]:text-primary"
                >
                  {formatNumber(p, locale)}
                </Link>
              ))}
            </nav>
          )}
        </>
      )}
    </>
  );
}

export default function LandingPage({ params, searchParams }: Props) {
  return (
    <main className="mx-auto flex w-full max-w-7xl flex-1 flex-col gap-4 px-4 py-6">
      <Suspense fallback={<div className="min-h-96 animate-pulse" />}>
        <Landing params={params} searchParams={searchParams} />
      </Suspense>
    </main>
  );
}
