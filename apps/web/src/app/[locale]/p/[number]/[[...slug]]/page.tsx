import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { ProductView } from "@/components/product/ProductView";
import { getProduct } from "@/lib/api/storefront";
import { productPath } from "@/lib/catalog/product-path";
import { isLocale, type Locale } from "@/lib/i18n/config";
import { getDictionary } from "@/lib/i18n/dictionaries";
import { absoluteUrl, jsonLdScript, productAlternates, productJsonLd } from "@/lib/seo/product";
import { siteUrl } from "@/lib/seo/config";
import { loadStore } from "@/lib/store/load";

type Params = PageProps<"/[locale]/p/[number]/[[...slug]]">["params"];

/** The product number from the URL, or null for anything that is not one. */
function parseNumber(value: string): number | null {
  return /^\d{1,15}$/.test(value) ? Number(value) : null;
}

/** Loads the product once per request for metadata and page (the fetcher is cached). Wrong slugs never get here: proxy.ts 301s them. */
async function load(params: Params) {
  const { locale, number: raw } = await params;
  const number = parseNumber(raw);
  if (!isLocale(locale) || number === null) return null;
  const store = await loadStore();
  const product = await getProduct(number, locale).catch(() => null);
  return product ? { locale, store, product } : null;
}

export async function generateMetadata({ params }: PageProps<"/[locale]/p/[number]/[[...slug]]">): Promise<Metadata> {
  const data = await load(params);
  if (!data) return { robots: { index: false, follow: false } };
  const { locale, store, product } = data;

  const { alternates, indexable } = productAlternates(product, locale, store.locales, store.defaultLocale, siteUrl());
  const description = product.description?.slice(0, 160);
  const image = product.images[0];
  return {
    title: product.name,
    description,
    alternates,
    // A language without a translation shows default-language content: not indexed (canonical points to the default).
    robots: indexable ? undefined : { index: false, follow: true },
    openGraph: {
      siteName: store.name,
      locale,
      type: "website",
      title: product.name,
      description,
      url: alternates.canonical as string,
      // Link previews do not render SVG; real product photos (phase 4) will be used here.
      ...(image && !image.endsWith(".svg") ? { images: [absoluteUrl(siteUrl(), image)] } : {}),
    },
  };
}

async function Product({ params }: { params: Params }) {
  const data = await load(params);
  // Mid-stream 404: Next.js adds noindex; unknown numbers and unpublished products end here.
  if (!data) notFound();
  const { locale, store, product } = data;
  const dict = await getDictionary(locale as Locale);
  const pageUrl = `${siteUrl()}${productPath(locale, product.number, product.slug)}`;

  return (
    <>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdScript(productJsonLd(product, pageUrl, store.currency, siteUrl())) }} />
      <ProductView product={product} locale={locale} currency={store.currency} texts={dict.product} />
    </>
  );
}

/** Space-keeping placeholder with the page's layout (no shift when the product streams in). */
function ProductSkeleton() {
  return (
    <div aria-hidden className="flex flex-col gap-6">
      <div className="h-3 w-48 animate-pulse rounded bg-border" />
      <div className="grid gap-6 md:grid-cols-[minmax(0,5fr)_minmax(0,6fr)] lg:grid-cols-[minmax(0,5fr)_minmax(0,5fr)_minmax(0,3fr)]">
        <div className="aspect-square animate-pulse rounded-[calc(var(--radius)*1.75)] bg-border" />
        <div className="flex flex-col gap-3">
          <div className="h-7 w-4/5 animate-pulse rounded bg-border" />
          <div className="h-4 w-2/5 animate-pulse rounded bg-border" />
        </div>
        <div className="h-56 animate-pulse rounded-2xl bg-border md:col-span-2 lg:col-span-1" />
      </div>
    </div>
  );
}

/** Product page /{locale}/p/{number}/{slug} (docs/SEO-MARKETING.md): server-rendered, canonical, hreflang, JSON-LD. */
export default function ProductPage({ params }: PageProps<"/[locale]/p/[number]/[[...slug]]">) {
  return (
    <main className="mx-auto w-full max-w-7xl flex-1 px-4 pt-4 md:px-6 md:pt-6">
      <Suspense fallback={<ProductSkeleton />}>
        <Product params={params} />
      </Suspense>
    </main>
  );
}
