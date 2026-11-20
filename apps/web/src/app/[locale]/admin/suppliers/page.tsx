import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { SuppliersEditor } from "@/components/admin/SuppliersEditor";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getSuppliers } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/suppliers">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.suppliersTitle);
}

/** The shop's suppliers (inventory.view; changes need inventory.manage). */
async function Suppliers({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "inventory.view", `/${locale}/admin/suppliers`);
  const suppliers = (await getSuppliers()) ?? [];

  return (
    <AdminShell locale={locale} texts={texts} current="suppliers" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.suppliersIntro}</p>
      <AdminCard title={texts.suppliersTitle} id="suppliers">
        <SuppliersEditor locale={locale} texts={texts} suppliers={suppliers} canManage={user.permissions.includes("inventory.manage")} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function SuppliersPage({ params }: PageProps<"/[locale]/admin/suppliers">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Suppliers locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
