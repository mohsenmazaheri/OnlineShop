import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { CatalogTreeEditor } from "@/components/admin/CatalogTreeEditor";
import { requirePermission } from "@/lib/admin/access";
import { languageNames } from "@/lib/admin/language-names";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getCatalogTree, getSpecChoices } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/catalog">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.catalogTitle);
}

/** Categories, sub-categories and product groups with their texts per language (permission catalog.manage). */
async function Catalog({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "catalog.manage", `/${locale}/admin/catalog`);
  const [loaded, specs] = await Promise.all([getCatalogTree(), getSpecChoices(locale)]);
  const tree = loaded ?? { defaultLocale: locale, locales: [locale], categories: [] };

  return (
    <AdminShell locale={locale} texts={texts} current="catalog" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.catalogIntro}</p>
      <AdminCard title={texts.catalogTitle} id="catalog-tree">
        <CatalogTreeEditor locale={locale} texts={texts} tree={tree} specs={specs ?? []} languageNames={languageNames(tree.locales)} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function CatalogPage({ params }: PageProps<"/[locale]/admin/catalog">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Catalog locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
