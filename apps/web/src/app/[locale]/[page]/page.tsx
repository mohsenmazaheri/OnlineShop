import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { Article } from "@/components/content/Article";
import { routeLocale } from "@/lib/account/page-helpers";
import { getContent } from "@/lib/api/storefront";
import { decodeSegment } from "@/lib/catalog/listing-page";
import { loadDisplayCurrency } from "@/lib/currency/load";
import { getDictionary } from "@/lib/i18n/dictionaries";
import { siteUrl } from "@/lib/seo/config";
import { contentJsonLd, contentPath, contentSeo } from "@/lib/seo/content";
import { jsonLdScript } from "@/lib/seo/product";
import { loadStore } from "@/lib/store/load";

type Props = PageProps<"/[locale]/[page]">;

async function load(params: Props["params"]) {
  const { locale: raw, page } = await params;
  const locale = routeLocale(raw);
  const store = await loadStore();
  const article = await getContent("Page", decodeSegment(page), locale).catch(() => null);
  return article ? { locale, store, article } : null;
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const data = await load(params);
  if (!data) return { robots: { index: false, follow: false } };
  const { locale, store, article } = data;
  const seo = contentSeo(article, locale, store.defaultLocale, siteUrl());
  const title = article.metaTitle || article.title;
  const description = article.metaDescription || article.summary || article.body.slice(0, 160);
  return {
    title,
    description,
    alternates: { canonical: seo.canonical, languages: seo.languages },
    robots: seo.indexable ? undefined : { index: false, follow: true },
    openGraph: { siteName: store.name, locale, type: "website", title, description, url: seo.canonical },
  };
}

/** A content page such as /fa/about (any address the shop itself does not use). */
async function ContentPage({ params }: { params: Props["params"] }) {
  const data = await load(params);
  if (!data) notFound();
  const { locale, store, article } = data;
  const [dict, { money }] = await Promise.all([getDictionary(locale), loadDisplayCurrency(locale, store.currency)]);
  const site = siteUrl();
  const pageUrl = `${site}${contentPath("Page", locale, article.slug)}`;
  const crumbs = [
    { name: dict.product.home, url: `${site}/${locale}` },
    { name: article.title, url: pageUrl },
  ];
  return (
    <>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdScript(contentJsonLd(article, locale, site, pageUrl, crumbs, store.name)) }} />
      <Article article={article} locale={locale} dict={dict} money={money} />
    </>
  );
}

export default function Page({ params }: Props) {
  return (
    <main className="mx-auto flex w-full max-w-7xl flex-1 flex-col px-4 py-6">
      <Suspense fallback={<div className="min-h-96 animate-pulse" />}>
        <ContentPage params={params} />
      </Suspense>
    </main>
  );
}
