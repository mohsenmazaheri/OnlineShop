import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { PurchaseList } from "@/components/admin/PurchaseList";
import { ButtonLink } from "@/components/ui/Button";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getPurchases } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/purchases">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.purchasesTitle);
}

/** Purchase receipts (inventory.view); writing one needs inventory.manage. */
async function Purchases({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "inventory.view", `/${locale}/admin/purchases`);
  const receipts = (await getPurchases()) ?? [];

  return (
    <AdminShell locale={locale} texts={texts} current="purchases" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.purchasesIntro}</p>
      {user.permissions.includes("inventory.manage") && (
        <div>
          <ButtonLink href={`/${locale}/admin/purchases/new`}>+ {texts.purchaseNew}</ButtonLink>
        </div>
      )}
      <AdminCard title={texts.purchasesTitle} id="purchases">
        <PurchaseList locale={locale} texts={texts} receipts={receipts} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function PurchasesPage({ params }: PageProps<"/[locale]/admin/purchases">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Purchases locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
