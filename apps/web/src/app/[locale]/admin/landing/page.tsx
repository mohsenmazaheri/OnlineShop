import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { LandingPagesEditor } from "@/components/admin/LandingPagesEditor";
import { requirePermission } from "@/lib/admin/access";
import { languageNames } from "@/lib/admin/language-names";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getLandingPages } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/landing">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.landingTitle);
}

/** Landing pages for much-searched combinations (content.manage). */
async function Landing({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "content.manage", `/${locale}/admin/landing`);
  const list = (await getLandingPages()) ?? { defaultLocale: locale, locales: [locale], pages: [] };

  return (
    <AdminShell locale={locale} texts={texts} current="landing" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.landingIntro}</p>
      <AdminCard title={texts.landingTitle} id="landing">
        <LandingPagesEditor locale={locale} texts={texts} list={list} languageNames={languageNames(list.locales)} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function LandingAdminPage({ params }: PageProps<"/[locale]/admin/landing">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Landing locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
