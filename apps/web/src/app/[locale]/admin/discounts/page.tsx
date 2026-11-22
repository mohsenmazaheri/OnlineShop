import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { DiscountsEditor } from "@/components/admin/DiscountsEditor";
import { requirePermission } from "@/lib/admin/access";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getDiscountCodes } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { loadStore } from "@/lib/store/load";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/discounts">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.discountsTitle);
}

/** Discount codes (discounts.manage). */
async function Discounts({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "discounts.manage", `/${locale}/admin/discounts`);
  const [codes, store] = await Promise.all([getDiscountCodes(), loadStore()]);

  return (
    <AdminShell locale={locale} texts={texts} current="discounts" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.discountsIntro}</p>
      <AdminCard title={texts.discountsTitle} id="discounts">
        <DiscountsEditor locale={locale} texts={texts} codes={codes ?? []} currency={store.currency} />
      </AdminCard>
    </AdminShell>
  );
}

export default async function DiscountsAdminPage({ params }: PageProps<"/[locale]/admin/discounts">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Discounts locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
