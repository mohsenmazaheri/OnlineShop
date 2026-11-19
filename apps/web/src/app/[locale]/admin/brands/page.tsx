import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { BrandsEditor } from "@/components/admin/BrandsEditor";
import { requirePermission } from "@/lib/admin/access";
import { languageNames } from "@/lib/admin/language-names";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getBrands } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/brands">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.brandsTitle);
}

/** The shop's brands with texts per language and logos (permission catalog.manage). */
async function Brands({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "catalog.manage", `/${locale}/admin/brands`);
  const list = (await getBrands()) ?? { defaultLocale: locale, locales: [locale], brands: [] };

  return (
    <AdminShell locale={locale} texts={texts} current="brands" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.brandsIntro}</p>
      <AdminCard title={texts.brandsTitle} id="brands">
        <BrandsEditor locale={locale} texts={texts} list={list} languageNames={languageNames(list.locales)} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function BrandsPage({ params }: PageProps<"/[locale]/admin/brands">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Brands locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
