import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { WarehousesEditor } from "@/components/admin/WarehousesEditor";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getWarehouses } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/warehouses">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.warehousesTitle);
}

/** The shop's warehouses (inventory.view; changes need inventory.manage). */
async function Warehouses({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "inventory.view", `/${locale}/admin/warehouses`);
  const warehouses = (await getWarehouses()) ?? [];

  return (
    <AdminShell locale={locale} texts={texts} current="warehouses" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.warehousesIntro}</p>
      <AdminCard title={texts.warehousesTitle} id="warehouses">
        <WarehousesEditor locale={locale} texts={texts} warehouses={warehouses} canManage={user.permissions.includes("inventory.manage")} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function WarehousesPage({ params }: PageProps<"/[locale]/admin/warehouses">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Warehouses locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
