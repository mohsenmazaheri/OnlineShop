import { Suspense } from "react";
import { AdminCard, AdminShell } from "@/components/admin/AdminShell";
import { ShippingEditor } from "@/components/admin/ShippingEditor";
import { requirePermission } from "@/lib/admin/access";
import { languageNames } from "@/lib/admin/language-names";
import { accountMetadata, routeLocale } from "@/lib/account/page-helpers";
import { getSalesSettings, getShippingAdmin } from "@/lib/account/session";
import type { Locale } from "@/lib/i18n/config";
import { getDictionary, type Dictionary } from "@/lib/i18n/dictionaries";
import { loadStore } from "@/lib/store/load";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/shipping">) {
  const dict = await getDictionary(routeLocale((await params).locale));
  return accountMetadata(dict.admin.shippingTitle);
}

/** Shipping methods and the tax rate (settings.manage). */
async function Shipping({ locale, texts }: { locale: Locale; texts: Dictionary["admin"] }) {
  const user = await requirePermission(locale, "settings.manage", `/${locale}/admin/shipping`);
  const [data, sales, store] = await Promise.all([getShippingAdmin(), getSalesSettings(), loadStore()]);
  const locales = [store.defaultLocale, ...store.locales.filter((l) => l !== store.defaultLocale)];

  return (
    <AdminShell locale={locale} texts={texts} current="shipping" permissions={user.permissions}>
      <p className="text-sm text-muted">{texts.shippingIntro}</p>
      <AdminCard title={texts.shippingTitle} id="shipping">
        <ShippingEditor
          locale={locale}
          texts={texts}
          methods={data?.methods ?? []}
          tax={data?.tax ?? { rate: 0, pricesIncludeTax: true }}
          sales={sales ?? { sellerName: null, sellerAddress: null, sellerTaxNumber: null, invoiceNote: null, returnDays: 14 }}
          locales={locales}
          defaultLocale={store.defaultLocale}
          countries={store.checkout.shippingCountries}
          currency={store.currency}
          languageNames={languageNames(locales)}
        />
      </AdminCard>
    </AdminShell>
  );
}

export default async function ShippingAdminPage({ params }: PageProps<"/[locale]/admin/shipping">) {
  const locale = routeLocale((await params).locale);
  const dict = await getDictionary(locale);
  return (
    <Suspense fallback={<p role="status" className="p-6">{dict.account.working}</p>}>
      <Shipping locale={locale} texts={dict.admin} />
    </Suspense>
  );
}
