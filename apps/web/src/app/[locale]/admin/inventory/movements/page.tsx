import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { MovementList } from "@/components/admin/MovementList";
import { requirePermission } from "@/lib/admin/access";
import { parseId, parseKind } from "@/lib/admin/inventory";
import { accountMetadata, firstParam, routeLocale } from "@/lib/account/page-helpers";
import { getMovements, getWarehouses } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/inventory/movements">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.movementsTitle);
}

type Search = Promise<Record<string, string | string[] | undefined>>;

/** The stock ledger with filters (inventory.view). */
async function Movements({ locale, texts, searchParams }: { locale: Locale; texts: Dictionary["admin"]; searchParams: Search }) {
  const user = await requirePermission(locale, "inventory.view", `/${locale}/admin/inventory/movements`);
  const query = await searchParams;
  const filters = {
    sku: firstParam(query.sku).trim().slice(0, 64),
    warehouseId: parseId(firstParam(query.warehouseId)) ?? "",
    kind: parseKind(firstParam(query.kind)) ?? "",
  };
  const pageNumber = Math.max(1, Number.parseInt(firstParam(query.page), 10) || 1);
  const [page, warehouses] = await Promise.all([getMovements({ ...filters, page: pageNumber, locale }), getWarehouses()]);

  return (
    <AdminShell locale={locale} texts={texts} current="movements" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.movementsIntro}</p>
      <AdminCard title={texts.movementsTitle} id="movements">
        <MovementList
          locale={locale}
          texts={texts}
          page={page ?? { items: [], total: 0, page: 1, pageSize: 50 }}
          warehouses={warehouses ?? []}
          filters={filters}
        />
      </AdminCard>
    </AdminShell>
  );
}

export default async function MovementsPage({ params, searchParams }: PageProps<"/[locale]/admin/inventory/movements">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Movements locale={locale} texts={dict.admin} searchParams={searchParams} />
    </Suspense>
  );
}
