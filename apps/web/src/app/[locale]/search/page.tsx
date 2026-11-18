import type { Metadata } from "next";
import { Suspense } from "react";
import { SearchForm } from "@/components/layout/SearchForm";
import { ButtonLink } from "@/components/ui/Button";
import { firstParam, routeLocale } from "@/lib/account/page-helpers";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

// Search result pages are never indexed (robots.txt disallows /*/search as well).
export async function generateMetadata({ params }: PageProps<"/[locale]/search">): Promise<Metadata> {
  const dict = await getDictionary(routeLocale((await params).locale));
  return { title: dict.search.title, robots: { index: false, follow: true } };
}

type Search = Promise<Record<string, string | string[] | undefined>>;

/** The query is read per request; product search itself arrives in phase 6 (IProductSearch). */
async function Results({ locale, dict, searchParams }: { locale: Locale; dict: Dictionary; searchParams: Search }) {
  const query = firstParam((await searchParams).q).trim().slice(0, 200);
  return (
    <>
      <h1 className="text-xl font-extrabold">{query ? dict.search.resultsFor.replace("{query}", query) : dict.search.title}</h1>
      <SearchForm locale={locale} texts={dict.header} defaultValue={query} />
      <p className="text-muted">{query ? dict.search.soon : dict.search.prompt}</p>
    </>
  );
}

export default async function SearchPage({ params, searchParams }: PageProps<"/[locale]/search">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col gap-4 px-4 py-8">
      <Suspense fallback={<h1 className="text-xl font-extrabold">{dict.search.title}</h1>}>
        <Results locale={locale} dict={dict} searchParams={searchParams} />
      </Suspense>
      <ButtonLink href={`/${locale}`} variant="secondary" className="self-start">
        {dict.search.backHome}
      </ButtonLink>
    </main>
  );
}
