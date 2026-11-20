import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { RedirectsEditor } from "@/components/admin/RedirectsEditor";
import { SearchIndexPanel } from "@/components/admin/SearchIndexPanel";
import { SynonymsEditor } from "@/components/admin/SynonymsEditor";
import { requirePermission } from "@/lib/admin/access";
import { languageNames } from "@/lib/admin/language-names";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getRedirects, getSearchIndexStatus, getSynonyms } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/seo">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.seoPageTitle);
}

/** Search synonyms and redirects of old addresses (permission catalog.manage). */
async function Seo({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "catalog.manage", `/${locale}/admin/seo`);
  const [synonyms, redirects, index] = await Promise.all([getSynonyms(), getRedirects(), getSearchIndexStatus()]);
  const list = synonyms ?? { locales: [locale], groups: [] };

  return (
    <AdminShell locale={locale} texts={texts} current="seo" permissions={user.permissions}>
      <AdminCard title={texts.redirectsTitle} id="redirects">
        <RedirectsEditor locale={locale} texts={texts} redirects={redirects ?? []} />
      </AdminCard>
      <AdminCard title={texts.synonymsTitle} id="synonyms">
        <SynonymsEditor texts={texts} list={list} languageNames={languageNames(list.locales)} />
      </AdminCard>
      {index && (
        <AdminCard title={texts.searchIndexTitle} id="search-index">
          <SearchIndexPanel locale={locale} texts={texts} status={index} />
        </AdminCard>
      )}
    </AdminShell>
  );
}

export default async function SeoPage({ params }: PageProps<"/[locale]/admin/seo">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Seo locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
