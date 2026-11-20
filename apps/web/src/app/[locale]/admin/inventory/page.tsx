import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { ItemSettingsForm } from "@/components/admin/ItemSettingsForm";
import { StockList } from "@/components/admin/StockList";
import { StockMovementForm } from "@/components/admin/StockMovementForm";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, firstParam, routeLocale } from "@/lib/account/page-helpers";
import { getStock } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/inventory">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.inventoryTitle);
}

type Search = Promise<Record<string, string | string[] | undefined>>;

/** Stock per item and warehouse (inventory.view); the movement form for inventory.manage. */
async function Inventory({ locale, texts, searchParams }: { locale: Locale; texts: Dictionary["admin"]; searchParams: Search }) {
  const user = await requirePermission(locale, "inventory.view", `/${locale}/admin/inventory`);
  const query = await searchParams;
  const search = firstParam(query.search).slice(0, 100);
  const onlyOut = firstParam(query.onlyOut) === "true";
  const onlyLow = firstParam(query.onlyLow) === "true";
  const sku = firstParam(query.sku).slice(0, 64);
  const pageNumber = Math.max(1, Number.parseInt(firstParam(query.page), 10) || 1);
  const page = (await getStock({ search, onlyOut, onlyLow, page: pageNumber, locale })) ?? {
    items: [],
    total: 0,
    page: 1,
    pageSize: 20,
    warehouses: [],
    lowCount: 0,
    currency: "",
  };
  const canManage = user.permissions.includes("inventory.manage");

  return (
    <AdminShell locale={locale} texts={texts} current="inventory" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.inventoryIntro}</p>
      {canManage && (
        <AdminCard title={texts.movementTitle} id="move">
          <StockMovementForm locale={locale} texts={texts} warehouses={page.warehouses} sku={sku} />
        </AdminCard>
      )}
      <AdminCard title={texts.inventoryTitle} id="stock">
        <StockList locale={locale} texts={texts} page={page} search={search} onlyOut={onlyOut} onlyLow={onlyLow} canManage={canManage} />
      </AdminCard>
      {canManage && (
        <AdminCard title={texts.itemSettingsTitle} id="settings">
          <ItemSettingsForm texts={texts} currency={page.currency} sku={sku} />
        </AdminCard>
      )}
    </AdminShell>
  );
}

export default async function InventoryPage({ params, searchParams }: PageProps<"/[locale]/admin/inventory">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Inventory locale={locale} texts={dict.admin} searchParams={searchParams} />
    </Suspense>
  );
}
