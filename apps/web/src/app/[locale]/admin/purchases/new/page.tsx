import Link from "next/link";
import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { ReceiptEditor } from "@/components/admin/ReceiptEditor";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getSuppliers, getWarehouses } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { loadStore } from "@/lib/store/load";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/purchases/new">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.purchaseNew);
}

/** A new draft purchase receipt (inventory.manage). */
async function NewPurchase({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "inventory.manage", `/${locale}/admin/purchases/new`);
  const [suppliers, warehouses, store] = await Promise.all([getSuppliers(), getWarehouses(), loadStore()]);

  return (
    <AdminShell locale={locale} texts={texts} current="purchases" permissions={user.permissions}>
      <Link href={`/${locale}/admin/purchases`} className="text-sm text-primary underline-offset-2 hover:underline">
        {texts.purchaseBack}
      </Link>
      <AdminCard title={texts.purchaseNew} id="receipt">
        <ReceiptEditor locale={locale} texts={texts} currency={store.currency} suppliers={suppliers ?? []} warehouses={warehouses ?? []} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function NewPurchasePage({ params }: PageProps<"/[locale]/admin/purchases/new">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <NewPurchase locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
