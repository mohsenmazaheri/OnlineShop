import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { ProductEditor } from "@/components/admin/ProductEditor";
import { requirePermission } from "@/lib/admin/access";
import { languageNames } from "@/lib/admin/language-names";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getBrands, getCatalogTree, getSpecChoices } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/products/new">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.productNew);
}

async function NewProduct({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "catalog.manage", `/${locale}/admin/products/new`);
  const [tree, specs, brands] = await Promise.all([getCatalogTree(), getSpecChoices(locale), getBrands()]);
  const catalog = tree ?? { defaultLocale: locale, locales: [locale], categories: [] };

  return (
    <AdminShell locale={locale} texts={texts} current="products" permissions={user.permissions}>
      <AdminCard title={texts.productNew} id="product-new">
        <ProductEditor locale={locale} texts={texts} tree={catalog} specs={specs ?? []} brands={brands?.brands ?? []} languageNames={languageNames(catalog.locales)} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function NewProductPage({ params }: PageProps<"/[locale]/admin/products/new">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <NewProduct locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
