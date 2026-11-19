import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { SpecsEditor } from "@/components/admin/SpecsEditor";
import { requirePermission } from "@/lib/admin/access";
import { languageNames } from "@/lib/admin/language-names";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getSpecLibrary } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/specs">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.specsTitle);
}

/** The spec library: kinds, flags, texts per language and options (permission catalog.manage). */
async function Specs({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "catalog.manage", `/${locale}/admin/specs`);
  const list = (await getSpecLibrary()) ?? { defaultLocale: locale, locales: [locale], specs: [] };

  return (
    <AdminShell locale={locale} texts={texts} current="specs" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.specsIntro}</p>
      <AdminCard title={texts.specsTitle} id="specs">
        <SpecsEditor locale={locale} texts={texts} list={list} languageNames={languageNames(list.locales)} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function SpecsPage({ params }: PageProps<"/[locale]/admin/specs">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Specs locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
