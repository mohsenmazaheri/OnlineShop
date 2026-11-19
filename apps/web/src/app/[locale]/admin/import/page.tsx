import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { ProductTransfer } from "@/components/admin/ProductTransfer";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/import">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.importTitle);
}

/** Bulk import and export of products and stock as Excel or CSV (permission catalog.manage). */
async function Import({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "catalog.manage", `/${locale}/admin/import`);

  return (
    <AdminShell locale={locale} texts={texts} current="import" permissions={user.permissions}>
      <AdminCard title={texts.importProductsTitle} id="import-products">
        <div className="flex flex-col gap-3">
          <p className="text-sm text-muted">{texts.importProductsIntro}</p>
          <details className="text-sm">
            <summary className="cursor-pointer font-medium">{texts.importColumnsTitle}</summary>
            <ul className="mt-2 flex list-inside list-disc flex-col gap-1 text-muted">
              {texts.importColumnsHelp.map((line) => (
                <li key={line}>{line}</li>
              ))}
            </ul>
          </details>
          <ProductTransfer locale={locale} kind="products" texts={texts} />
        </div>
      </AdminCard>
      <AdminCard title={texts.importStockTitle} id="import-stock">
        <div className="flex flex-col gap-3">
          <p className="text-sm text-muted">{texts.importStockIntro}</p>
          <ProductTransfer locale={locale} kind="stock" texts={texts} />
        </div>
      </AdminCard>
    </AdminShell>
  );
}

export default async function ImportPage({ params }: PageProps<"/[locale]/admin/import">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Import locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
