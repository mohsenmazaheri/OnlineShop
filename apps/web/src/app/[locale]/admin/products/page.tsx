import { Suspense } from "react";
import { AdminShell } from "@/components/admin/AdminShell";
import { ProductList } from "@/components/admin/ProductList";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, firstParam, routeLocale } from "@/lib/account/page-helpers";
import { getAdminProducts } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { loadStore } from "@/lib/store/load";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/products">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.productsTitle);
}

type Search = Promise<Record<string, string | string[] | undefined>>;

/** All products (published or not) with search and paging (permission catalog.manage). */
async function Products({ locale, texts, searchParams }: { locale: Locale; texts: Dictionary["admin"]; searchParams: Search }) {
  const user = await requirePermission(locale, "catalog.manage", `/${locale}/admin/products`);
  const query = await searchParams;
  const search = firstParam(query.search).slice(0, 100);
  const pageNumber = Math.max(1, Number.parseInt(firstParam(query.page), 10) || 1);
  const [page, store] = await Promise.all([getAdminProducts({ search, page: pageNumber, locale }), loadStore()]);

  return (
    <AdminShell locale={locale} texts={texts} current="products" permissions={user.permissions}>
      <ProductList
        locale={locale}
        texts={texts}
        page={page ?? { items: [], total: 0, page: 1, pageSize: 20 }}
        search={search}
        currency={store.currency}
      />
    </AdminShell>
  );
}

export default async function ProductsPage({ params, searchParams }: PageProps<"/[locale]/admin/products">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Products locale={locale} texts={dict.admin} searchParams={searchParams} />
    </Suspense>
  );
}
