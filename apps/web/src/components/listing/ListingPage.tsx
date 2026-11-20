import Link from "next/link";
import type { LoadedListing } from "@/lib/catalog/listing-page";
import { getDictionary } from "@/lib/i18n/dictionaries";
import { canonicalPath } from "@/lib/catalog/browse";
import { siteUrl } from "@/lib/seo/config";
import { listingJsonLd } from "@/lib/seo/listing";
import { jsonLdScript } from "@/lib/seo/product";
import { ProductListing } from "./ProductListing";

/** A listing page: breadcrumb, heading and description, the listing, and its structured data (breadcrumb + item list). */
export async function ListingPageBody({ listing, heading }: { listing: LoadedListing; heading: string }) {
  const { locale, money, params, result, basePath } = listing;
  const dict = await getDictionary(locale as Parameters<typeof getDictionary>[0]);
  const crumbs = result.scope.breadcrumb;
  const pageUrl = `${siteUrl()}${canonicalPath(basePath, params)}`;

  return (
    <main className="mx-auto flex w-full max-w-7xl flex-1 flex-col gap-4 px-4 py-6">
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdScript(listingJsonLd(result, locale, siteUrl(), dict.listing.home, pageUrl)) }} />
      {crumbs.length > 0 && (
        <nav aria-label="breadcrumb" className="text-sm text-muted">
          <ol className="flex flex-wrap items-center gap-1">
            <li>
              <Link href={`/${locale}`} className="hover:text-primary">
                {dict.listing.home}
              </Link>
            </li>
            {crumbs.map((c, i) => (
              <li key={c.path} className="flex items-center gap-1">
                <span aria-hidden>›</span>
                {i === crumbs.length - 1 ? (
                  <span aria-current="page">{c.name}</span>
                ) : (
                  <Link href={`/${locale}/${c.path}`} className="hover:text-primary">
                    {c.name}
                  </Link>
                )}
              </li>
            ))}
          </ol>
        </nav>
      )}
      <h1 className="text-2xl font-extrabold">{heading}</h1>
      {result.scope.description && <p className="max-w-3xl text-muted">{result.scope.description}</p>}
      <ProductListing locale={locale} dict={dict} result={result} basePath={basePath} params={params} money={money} />
    </main>
  );
}
